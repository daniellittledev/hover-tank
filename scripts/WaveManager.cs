using Godot;

namespace HoverTank
{
    /// <summary>
    /// Manages single-player enemy wave spawning and progression.
    /// Added as a child of the Main node by GameSetup when in SinglePlayer mode.
    ///
    /// Each wave spawns N enemy tanks evenly distributed around the origin.
    /// Difficulty scales per wave: more enemies, better accuracy, heavier weapons.
    /// </summary>
    public partial class WaveManager : Node
    {
        // ── Wave difficulty config ────────────────────────────────────────────
        private readonly struct WaveConfig
        {
            public readonly int          Count;
            public readonly float        Accuracy;       // 0=perfect, 1=terrible
            public readonly WeaponType   Weapon;
            public readonly bool         LeadTarget;
            public readonly float        EngageRange;
            public readonly float        FireAngle;      // radians

            public WaveConfig(int count, float accuracy, WeaponType weapon,
                              bool lead, float engage, float fireAngle)
            {
                Count       = count;
                Accuracy    = accuracy;
                Weapon      = weapon;
                LeadTarget  = lead;
                EngageRange = engage;
                FireAngle   = fireAngle;
            }
        }

        private static WaveConfig GetWaveConfig(int wave) => wave switch
        {
            1 => new WaveConfig(2, 0.50f, WeaponType.MiniGun,   false, 50f, 0.35f),
            2 => new WaveConfig(3, 0.35f, WeaponType.MiniGun,   false, 50f, 0.28f),
            3 => new WaveConfig(4, 0.22f, WeaponType.Rocket,    false, 55f, 0.22f),
            4 => new WaveConfig(5, 0.15f, WeaponType.Rocket,    true,  55f, 0.18f),
            _ => new WaveConfig(2 + wave, 0.10f, WeaponType.TankShell, true, 60f, 0.14f),
        };

        // ── State ─────────────────────────────────────────────────────────────
        private enum WaveState { Starting, InProgress, WaveComplete, GameOver }
        private WaveState    _state       = WaveState.Starting;
        private int          _currentWave = 0;
        private int          _enemiesAlive;
        private int          _score;

        // ── Scene refs ────────────────────────────────────────────────────────
        private Node3D?      _tanksContainer;
        private PackedScene  _tankScene    = null!;
        private bool         _playerConnected;

        // ── HUD ───────────────────────────────────────────────────────────────
        private Label?       _enemyCountLabel;
        private Label?       _scoreLabel;
        private Label?       _bannerLabel;
        private float        _bannerTimer;
        private const float  BannerDuration = 2.5f;

        // Spawn radius from world origin (metres).
        private const float SpawnRadius = 65f;

        public override void _Ready()
        {
            _tankScene      = GD.Load<PackedScene>("res://scenes/HoverTank.tscn");
            _tanksContainer = GetTree().Root.GetNodeOrNull<Node3D>("Main/Tanks");

            BuildHUD();

            // Defer so terrain physics and the player tank are fully initialised.
            CallDeferred(nameof(SpawnStartingAllies));
            CallDeferred(nameof(StartNextWave));
        }

        // ── Ally spawning ─────────────────────────────────────────────────────

        private void SpawnStartingAllies()
        {
            SpawnAlly(new Vector3(-6f, 5f,  3f));
            SpawnAlly(new Vector3( 6f, 5f,  3f));
        }

        private void SpawnAlly(Vector3 position)
        {
            var tank = _tankScene.Instantiate<HoverTank>();
            tank.IsFriendlyAI = true;

            var ai = new AllyAI { Name = "AllyAI" };
            tank.AddChild(ai);

            _tanksContainer?.AddChild(tank);
            tank.GlobalPosition = position;

            SetAllyHullColor(tank);
        }

        private static void SetAllyHullColor(HoverTank tank)
        {
            var body = tank.GetNodeOrNull<MeshInstance3D>("Body");
            if (body == null) return;
            body.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
            {
                AlbedoColor = new Color(0.15f, 0.75f, 0.25f),
                Roughness   = 0.55f,
                Metallic    = 0.55f,
            });
        }

        public override void _Process(double delta)
        {
            // Banner fade
            if (_bannerTimer > 0f)
            {
                _bannerTimer -= (float)delta;
                if (_bannerTimer <= 0f && _bannerLabel != null)
                    _bannerLabel.Visible = false;
            }

            // Retry player connection each frame until found.
            if (!_playerConnected)
                TryConnectPlayer();
        }

        // ── Wave lifecycle ────────────────────────────────────────────────────

        private void StartNextWave()
        {
            if (_state == WaveState.GameOver) return;

            _currentWave++;
            _state = WaveState.InProgress;

            WaveConfig cfg = GetWaveConfig(_currentWave);
            SpawnWave(cfg);
            ShowBanner($"WAVE  {_currentWave}");
            UpdateEnemyCount();
        }

        private void SpawnWave(WaveConfig cfg)
        {
            _enemiesAlive = cfg.Count;

            for (int i = 0; i < cfg.Count; i++)
            {
                float angle = Mathf.Tau * i / cfg.Count;
                float x     = Mathf.Sin(angle) * SpawnRadius;
                float z     = Mathf.Cos(angle) * SpawnRadius;
                float y     = GetTerrainHeight(x, z) + 3f;

                SpawnEnemy(new Vector3(x, y, z), cfg);
            }
        }

        private void SpawnEnemy(Vector3 position, WaveConfig cfg)
        {
            var tank = _tankScene.Instantiate<HoverTank>();
            tank.IsEnemy = true; // must be set before AddChild so _Ready sees it

            // Build AI child before adding to tree so it's ready when _Ready fires.
            var ai = new EnemyAI
            {
                AimAccuracy        = cfg.Accuracy,
                PreferredWeapon    = cfg.Weapon,
                LeadTarget         = cfg.LeadTarget,
                EngageRange        = cfg.EngageRange,
                FireAngleThreshold = cfg.FireAngle,
            };
            tank.AddChild(ai);

            _tanksContainer?.AddChild(tank);
            tank.GlobalPosition = position;

            // Paint hull red so the player can tell enemies apart.
            SetEnemyHullColor(tank);

            tank.Died += () => OnEnemyDied(tank);
        }

        private static void SetEnemyHullColor(HoverTank tank)
        {
            var body = tank.GetNodeOrNull<MeshInstance3D>("Body");
            if (body == null) return;

            body.SetSurfaceOverrideMaterial(0, new StandardMaterial3D
            {
                AlbedoColor = new Color(0.85f, 0.10f, 0.10f),
                Roughness   = 0.55f,
                Metallic    = 0.55f,
            });
        }

        private void OnEnemyDied(HoverTank tank)
        {
            _enemiesAlive--;
            _score += 100;
            UpdateEnemyCount();
            UpdateScore();

            // Immediately hide and freeze the tank so it stops shooting and moving.
            // QueueFree after a short delay lets in-flight projectiles finish.
            tank.Visible = false;
            tank.Freeze  = true;
            GetTree().CreateTimer(1.5f).Timeout += tank.QueueFree;

            if (_enemiesAlive <= 0 && _state == WaveState.InProgress)
            {
                _state = WaveState.WaveComplete;
                ShowBanner("WAVE COMPLETE!");
                GetTree().CreateTimer(4.0f).Timeout += StartNextWave;
            }
        }

        // ── Player death ──────────────────────────────────────────────────────

        private void TryConnectPlayer()
        {
            foreach (Node node in GetTree().GetNodesInGroup("hover_tanks"))
            {
                if (node is HoverTank tank && !tank.IsEnemy)
                {
                    tank.Died     += OnPlayerDied;
                    _playerConnected = true;
                    return;
                }
            }
        }

        private void OnPlayerDied()
        {
            if (_state == WaveState.GameOver) return;
            _state = WaveState.GameOver;
            ShowGameOverOverlay();
        }

        // ── Terrain height probe ──────────────────────────────────────────────

        private float GetTerrainHeight(float x, float z)
        {
            var spaceState = GetWorld3D().DirectSpaceState;
            var query = PhysicsRayQueryParameters3D.Create(
                new Vector3(x, 150f, z),
                new Vector3(x, -50f,  z)
            );
            var result = spaceState.IntersectRay(query);
            if (result.Count > 0)
                return result["position"].As<Vector3>().Y;
            return 0f;
        }

        // ── HUD ───────────────────────────────────────────────────────────────

        private void BuildHUD()
        {
            var layer = new CanvasLayer { Layer = 8, Name = "WaveHUD" };
            AddChild(layer);

            // Score — top right
            _scoreLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                AnchorLeft          = 1f, AnchorRight  = 1f,
                AnchorTop           = 0f, AnchorBottom = 0f,
                OffsetLeft          = -200f, OffsetRight  = -16f,
                OffsetTop           = 12f,   OffsetBottom = 40f,
            };
            _scoreLabel.AddThemeColorOverride("font_color",   new Color(1f, 0.90f, 0.30f));
            _scoreLabel.AddThemeFontSizeOverride("font_size", 18);
            layer.AddChild(_scoreLabel);

            // Enemy counter — top right, below score
            _enemyCountLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                AnchorLeft          = 1f, AnchorRight  = 1f,
                AnchorTop           = 0f, AnchorBottom = 0f,
                OffsetLeft          = -200f, OffsetRight  = -16f,
                OffsetTop           = 40f,   OffsetBottom = 68f,
            };
            _enemyCountLabel.AddThemeColorOverride("font_color",   new Color(1f, 0.35f, 0.35f));
            _enemyCountLabel.AddThemeFontSizeOverride("font_size", 18);
            layer.AddChild(_enemyCountLabel);

            // Centre banner (wave number / wave complete)
            _bannerLabel = new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                AnchorLeft          = 0.5f, AnchorRight  = 0.5f,
                AnchorTop           = 0.2f, AnchorBottom = 0.2f,
                OffsetLeft          = -250f, OffsetRight  = 250f,
                OffsetTop           = -30f,  OffsetBottom = 30f,
                Visible             = false,
            };
            _bannerLabel.AddThemeColorOverride("font_color",   new Color(1f, 0.90f, 0.30f));
            _bannerLabel.AddThemeFontSizeOverride("font_size", 36);
            layer.AddChild(_bannerLabel);

            UpdateScore();
        }

        private void ShowBanner(string text)
        {
            if (_bannerLabel == null) return;
            _bannerLabel.Text    = text;
            _bannerLabel.Visible = true;
            _bannerTimer         = BannerDuration;
        }

        private void UpdateEnemyCount()
        {
            if (_enemyCountLabel != null)
                _enemyCountLabel.Text = $"ENEMIES  {_enemiesAlive}";
        }

        private void UpdateScore()
        {
            if (_scoreLabel != null)
                _scoreLabel.Text = $"SCORE  {_score}";
        }

        private void ShowGameOverOverlay()
        {
            var layer = new CanvasLayer { Layer = 20 };
            AddChild(layer);

            var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.72f) };
            dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(dim);

            var vbox = new VBoxContainer();
            vbox.AnchorLeft   = 0.5f; vbox.OffsetLeft   = -160f;
            vbox.AnchorTop    = 0.5f; vbox.OffsetTop    = -100f;
            vbox.AnchorRight  = 0.5f; vbox.OffsetRight  =  160f;
            vbox.AnchorBottom = 0.5f; vbox.OffsetBottom =  100f;
            vbox.AddThemeConstantOverride("separation", 18);
            layer.AddChild(vbox);

            var title = new Label
            {
                Text                = "GAME OVER",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            title.AddThemeColorOverride("font_color",   new Color(0.90f, 0.20f, 0.20f));
            title.AddThemeFontSizeOverride("font_size", 36);
            vbox.AddChild(title);

            var waveLbl = new Label
            {
                Text                = $"Wave {_currentWave}   Score {_score}",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            waveLbl.AddThemeColorOverride("font_color",   new Color(0.70f, 0.70f, 0.70f));
            waveLbl.AddThemeFontSizeOverride("font_size", 18);
            vbox.AddChild(waveLbl);

            var restartBtn = MakeMenuButton("RESTART");
            restartBtn.Pressed += () =>
                GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
            vbox.AddChild(restartBtn);

            var menuBtn = MakeMenuButton("MAIN MENU");
            menuBtn.Pressed += () =>
                GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
            vbox.AddChild(menuBtn);
        }

        private static Button MakeMenuButton(string text)
        {
            var btn = new Button { Text = text };
            btn.AddThemeColorOverride("font_color",       new Color(0.85f, 0.85f, 0.85f));
            btn.AddThemeColorOverride("font_hover_color", new Color(0.20f, 1.00f, 0.40f));
            btn.AddThemeFontSizeOverride("font_size", 18);
            btn.AddThemeStyleboxOverride("normal",  new StyleBoxFlat { BgColor = Colors.Transparent });
            btn.AddThemeStyleboxOverride("focus",   new StyleBoxFlat { BgColor = Colors.Transparent });
            var hover = new StyleBoxFlat
            {
                BgColor              = new Color(0.20f, 1.00f, 0.40f, 0.12f),
                CornerRadiusTopLeft  = 4, CornerRadiusTopRight    = 4,
                CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            };
            btn.AddThemeStyleboxOverride("hover",   hover);
            btn.AddThemeStyleboxOverride("pressed", hover);
            return btn;
        }
    }
}
