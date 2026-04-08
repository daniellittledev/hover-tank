using Godot;

namespace HoverTank
{
    // CanvasLayer overlay: health bar, three-weapon ammo list, crosshair.
    // Attaches to the first HoverTank that enters the scene tree.
    public partial class HUD : CanvasLayer
    {
        private HoverTank? _tank;

        // Health panel refs
        private ProgressBar  _healthBar   = null!;
        private Label        _healthLabel = null!;
        private StyleBoxFlat _healthFill  = null!;

        // Weapon panel refs — one row per weapon (MiniGun/Rocket/Shell)
        private Label[] _weaponNameLabels = null!;
        private Label[] _ammoLabels       = null!;

        private static readonly string[] WeaponDisplayNames = { "MINIGUN", "ROCKET ", "CANNON " };

        public override void _Ready()
        {
            Layer = 10;
            BuildUI();

            // Listen for tanks being added (spawned by NetworkManager after F1/F2)
            GetTree().NodeAdded += OnNodeAdded;

            // In case a tank already exists when the HUD is added
            foreach (var node in GetTree().GetNodesInGroup("hover_tanks"))
                if (node is HoverTank t) { _tank = t; break; }
        }

        private void OnNodeAdded(Node node)
        {
            if (_tank == null && node is HoverTank t)
                _tank = t;
        }

        // ── Layout construction ──────────────────────────────────────────────
        private void BuildUI()
        {
            var root = new Control();
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.MouseFilter = Control.MouseFilterEnum.Ignore;
            AddChild(root);

            BuildHealthPanel(root);
            BuildWeaponsPanel(root);
            BuildCrosshair(root);
        }

        private static StyleBoxFlat PanelStyle() => new StyleBoxFlat
        {
            BgColor                 = new Color(0f, 0f, 0f, 0.52f),
            CornerRadiusTopLeft     = 6,
            CornerRadiusTopRight    = 6,
            CornerRadiusBottomLeft  = 6,
            CornerRadiusBottomRight = 6,
            ContentMarginLeft       = 12,
            ContentMarginRight      = 12,
            ContentMarginTop        = 10,
            ContentMarginBottom     = 10,
        };

        private void BuildHealthPanel(Control root)
        {
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", PanelStyle());
            // Bottom-left corner
            panel.AnchorLeft   = 0f; panel.OffsetLeft   = 20f;
            panel.AnchorTop    = 1f; panel.OffsetTop    = -128f;
            panel.AnchorRight  = 0f; panel.OffsetRight  = 230f;
            panel.AnchorBottom = 1f; panel.OffsetBottom = -20f;
            root.AddChild(panel);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 5);
            panel.AddChild(vbox);

            var title = new Label { Text = "HEALTH" };
            title.AddThemeColorOverride("font_color", new Color(0.45f, 0.95f, 0.55f));
            vbox.AddChild(title);

            _healthFill = new StyleBoxFlat { BgColor = new Color(0.20f, 0.85f, 0.30f) };
            var healthBg = new StyleBoxFlat  { BgColor = new Color(0.10f, 0.10f, 0.10f) };

            _healthBar = new ProgressBar
            {
                MinValue              = 0,
                MaxValue              = 100,
                Value                 = 100,
                ShowPercentage        = false,
                CustomMinimumSize     = new Vector2(190f, 14f),
            };
            _healthBar.AddThemeStyleboxOverride("fill",       _healthFill);
            _healthBar.AddThemeStyleboxOverride("background", healthBg);
            vbox.AddChild(_healthBar);

            _healthLabel = new Label { Text = "100 / 100" };
            _healthLabel.AddThemeColorOverride("font_color", new Color(0.85f, 0.85f, 0.85f));
            vbox.AddChild(_healthLabel);
        }

        private void BuildWeaponsPanel(Control root)
        {
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", PanelStyle());
            // Bottom-right corner
            panel.AnchorLeft   = 1f; panel.OffsetLeft   = -250f;
            panel.AnchorTop    = 1f; panel.OffsetTop    = -168f;
            panel.AnchorRight  = 1f; panel.OffsetRight  = -20f;
            panel.AnchorBottom = 1f; panel.OffsetBottom = -20f;
            root.AddChild(panel);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 6);
            panel.AddChild(vbox);

            _weaponNameLabels = new Label[3];
            _ammoLabels       = new Label[3];

            for (int i = 0; i < 3; i++)
            {
                var row = new HBoxContainer();
                row.AddThemeConstantOverride("separation", 10);
                vbox.AddChild(row);

                _weaponNameLabels[i] = new Label
                {
                    Text              = WeaponDisplayNames[i],
                    CustomMinimumSize = new Vector2(75f, 0f),
                };
                _weaponNameLabels[i].AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
                row.AddChild(_weaponNameLabels[i]);

                _ammoLabels[i] = new Label { Text = "---" };
                _ammoLabels[i].AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
                row.AddChild(_ammoLabels[i]);
            }
        }

        private void BuildCrosshair(Control root)
        {
            var ch = new Crosshair { MouseFilter = Control.MouseFilterEnum.Ignore };
            // Centre it: anchored to (0.5, 0.5) with ±40 px offsets
            ch.AnchorLeft   = 0.5f; ch.OffsetLeft   = -40f;
            ch.AnchorTop    = 0.5f; ch.OffsetTop    = -40f;
            ch.AnchorRight  = 0.5f; ch.OffsetRight  =  40f;
            ch.AnchorBottom = 0.5f; ch.OffsetBottom =  40f;
            root.AddChild(ch);
        }

        // ── Per-frame updates ────────────────────────────────────────────────
        public override void _Process(double delta)
        {
            if (_tank == null) return;

            UpdateHealth();
            if (_tank.Weapons != null)
                UpdateWeapons(_tank.Weapons);
        }

        private void UpdateHealth()
        {
            _healthBar.MaxValue = _tank!.MaxHealth;
            _healthBar.Value    = _tank.Health;
            _healthLabel.Text   = $"{(int)_tank.Health} / {(int)_tank.MaxHealth}";

            float pct = _tank.Health / _tank.MaxHealth;
            _healthFill.BgColor = pct > 0.50f
                ? new Color(0.20f, 0.85f, 0.30f)
                : pct > 0.25f
                    ? new Color(0.90f, 0.75f, 0.10f)
                    : new Color(0.90f, 0.20f, 0.20f);
        }

        private void UpdateWeapons(WeaponManager wm)
        {
            for (int i = 0; i < 3; i++)
            {
                var wType  = (WeaponType)i;
                bool active = wm.CurrentWeapon == wType;
                var (cur, max) = wm.GetAmmo(wType);

                _weaponNameLabels[i].Text = (active ? "> " : "  ") + WeaponDisplayNames[i];
                _weaponNameLabels[i].AddThemeColorOverride("font_color",
                    active ? new Color(0.20f, 1.00f, 0.40f) : new Color(0.50f, 0.50f, 0.50f));

                _ammoLabels[i].Text = $"{cur}/{max}";
                _ammoLabels[i].AddThemeColorOverride("font_color",
                    active ? new Color(1.00f, 1.00f, 1.00f) : new Color(0.50f, 0.50f, 0.50f));
            }
        }
    }
}
