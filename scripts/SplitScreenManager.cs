using Godot;

namespace HoverTank
{
    // Attached to the root of SplitScreen.tscn.
    // Sets up a two-player local split-screen session:
    //   • Spawns two tanks at offset start positions.
    //   • Creates two SubViewports (shared World3D) side by side, each rendering
    //     from its own camera that tracks its player's CameraMount.
    //   • Attaches LocalInputHandler to each tank (P1=WASD, P2=arrow keys).
    //   • Sets WeaponManager.InputPrefix so P2's weapon keys don't clash with P1.
    //   • Adds a per-player HUD inside each SubViewport.
    //   • Handles Escape → pause menu with "Quit to Menu" option.
    public partial class SplitScreenManager : Node3D
    {
        private HoverTank _tank1 = null!;
        private HoverTank _tank2 = null!;

        // Cached CameraMount refs — resolved once in _Ready, synced every _Process.
        private Node3D? _mount1;
        private Node3D? _mount2;

        private Node3D _camHolder1 = null!;
        private Node3D _camHolder2 = null!;

        private PauseMenu _pauseMenu = null!;

        public override void _Ready()
        {
            var tanksRoot = GetNode<Node3D>("Tanks");

            // ── Spawn tanks ───────────────────────────────────────────────────
            _tank1 = SpawnTank(tanksRoot, "Tank_P1", new Vector3(-4f, 5f, 0f), 0);
            _tank2 = SpawnTank(tanksRoot, "Tank_P2", new Vector3( 4f, 5f, 0f), 1);

            // Cache CameraMount references — these don't change after spawn.
            _mount1 = _tank1.GetNodeOrNull<Node3D>("CameraMount");
            _mount2 = _tank2.GetNodeOrNull<Node3D>("CameraMount");

            // ── Disable the in-scene cameras — SubViewport cameras take over ──
            DisableTankCamera(_tank1);
            DisableTankCamera(_tank2);

            // ── Build split-screen viewports ──────────────────────────────────
            Vector2I windowSize = DisplayServer.WindowGetSize();
            int halfW = windowSize.X / 2;
            int h     = windowSize.Y;

            // Layer -1: renders behind all UI so the viewports fill the screen.
            var uiLayer = new CanvasLayer { Layer = -1 };
            AddChild(uiLayer);

            var hbox = new HBoxContainer();
            hbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            hbox.AddThemeConstantOverride("separation", 0);
            uiLayer.AddChild(hbox);

            var (subVP1, camHolder1) = AddViewportSide(hbox, halfW, h);
            var (subVP2, camHolder2) = AddViewportSide(hbox, halfW, h);
            _camHolder1 = camHolder1;
            _camHolder2 = camHolder2;

            // ── HUD per player (inside each SubViewport so it clips correctly) ─
            var hud1 = new HUD();
            subVP1.AddChild(hud1);
            hud1.SetTank(_tank1);

            var hud2 = new HUD();
            subVP2.AddChild(hud2);
            hud2.SetTank(_tank2);

            // ── Centre divider — separate CanvasLayer above the viewports ─────
            AddDivider();

            // ── Pause menu ────────────────────────────────────────────────────
            _pauseMenu = new PauseMenu();
            AddChild(_pauseMenu);
        }

        public override void _Process(double _)
        {
            // Sync each SubViewport camera to the tank's CameraMount transform.
            // CameraMount is at local +7.5 Z and angled downward — copy it directly.
            // (Parenting across viewport boundaries is not possible in Godot 4;
            //  manual sync here is the correct pattern.)
            if (_mount1 != null) _camHolder1.GlobalTransform = _mount1.GlobalTransform;
            if (_mount2 != null) _camHolder2.GlobalTransform = _mount2.GlobalTransform;
        }

        public override void _Input(InputEvent evt)
        {
            if (evt is InputEventKey key && key.Pressed && !key.Echo
                && key.PhysicalKeycode == Key.Escape)
            {
                TogglePause();
                GetViewport().SetInputAsHandled();
            }
        }

        private void TogglePause()
        {
            bool pausing = !GetTree().Paused;
            GetTree().Paused = pausing;

            if (pausing)
                _pauseMenu.Show();
            else
                _pauseMenu.Hide();
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static HoverTank SpawnTank(Node3D root, string name, Vector3 pos, int playerIndex)
        {
            var tank = GD.Load<PackedScene>("res://scenes/HoverTank.tscn")
                         .Instantiate<HoverTank>();
            tank.Name           = name;
            tank.GlobalPosition = pos;
            root.AddChild(tank);

            var handler = new LocalInputHandler { Target = tank, PlayerIndex = playerIndex };
            tank.AddChild(handler);

            if (playerIndex == 1 && tank.Weapons != null)
                tank.Weapons.InputPrefix = "p2_";

            return tank;
        }

        private static void DisableTankCamera(HoverTank tank)
        {
            var cam = tank.GetNodeOrNull<Camera3D>("CameraMount/Camera");
            if (cam == null) return;
            cam.Current     = false;
            cam.ProcessMode = ProcessModeEnum.Disabled;
        }

        // Creates one side of the split — returns (SubViewport, CameraHolder).
        private static (SubViewport, Node3D) AddViewportSide(HBoxContainer hbox, int w, int h)
        {
            var container = new SubViewportContainer
            {
                Stretch           = true,
                CustomMinimumSize = new Vector2(w, h),
            };
            container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hbox.AddChild(container);

            var viewport = new SubViewport
            {
                Size                   = new Vector2I(w, h),
                OwnWorld3D             = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                AudioListenerEnable3D  = true,
            };
            container.AddChild(viewport);

            // Camera holder: GlobalTransform synced to CameraMount in _Process.
            var camHolder = new Node3D { Name = "CameraHolder" };
            viewport.AddChild(camHolder);

            var cam = new Camera3D { Current = true };
            camHolder.AddChild(cam);

            return (viewport, camHolder);
        }

        // 2-pixel green divider line at the centre of the screen.
        // Uses its own CanvasLayer (layer 5) so it renders above the SubViewports
        // (layer -1) but below the pause menu (layer 20).
        private void AddDivider()
        {
            var dividerLayer = new CanvasLayer { Layer = 5 };
            AddChild(dividerLayer);

            var ctrl = new Control();
            ctrl.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            ctrl.MouseFilter = Control.MouseFilterEnum.Ignore;
            dividerLayer.AddChild(ctrl);

            var line = new ColorRect
            {
                Color         = new Color(0.20f, 1.00f, 0.40f, 0.6f),
                AnchorLeft    = 0.5f, AnchorTop    = 0f,
                AnchorRight   = 0.5f, AnchorBottom = 1f,
                OffsetLeft    = -1f,  OffsetRight   = 1f,
            };
            ctrl.AddChild(line);
        }
    }
}
