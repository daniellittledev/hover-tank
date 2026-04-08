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
    public partial class SplitScreenManager : Node3D
    {
        private HoverTank _tank1 = null!;
        private HoverTank _tank2 = null!;

        private Node3D _camHolder1 = null!;
        private Node3D _camHolder2 = null!;

        public override void _Ready()
        {
            var tanksRoot = GetNode<Node3D>("Tanks");

            // ── Spawn tanks ───────────────────────────────────────────────────
            _tank1 = SpawnTank(tanksRoot, "Tank_P1", new Vector3(-4f, 5f, 0f), 0);
            _tank2 = SpawnTank(tanksRoot, "Tank_P2", new Vector3( 4f, 5f, 0f), 1);

            // ── Disable the in-scene cameras — SubViewport cameras take over ──
            DisableTankCamera(_tank1);
            DisableTankCamera(_tank2);

            // ── Build split-screen viewports ──────────────────────────────────
            Vector2I viewportSize = DisplayServer.WindowGetSize();
            int halfW = viewportSize.X / 2;
            int h     = viewportSize.Y;

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

            // ── HUD per player (inside SubViewport so it clips to that half) ──
            var hud1 = new HUD();
            subVP1.AddChild(hud1);
            hud1.SetTank(_tank1);

            var hud2 = new HUD();
            subVP2.AddChild(hud2);
            hud2.SetTank(_tank2);

            // ── Divider line in the centre ────────────────────────────────────
            AddDivider(uiLayer, viewportSize);
        }

        public override void _Process(double _)
        {
            // Sync each SubViewport camera to the tank's CameraMount transform.
            // CameraMount is at local +7.5 Z and angled downward — copy it directly.
            var mount1 = _tank1.GetNodeOrNull<Node3D>("CameraMount");
            var mount2 = _tank2.GetNodeOrNull<Node3D>("CameraMount");

            if (mount1 != null) _camHolder1.GlobalTransform = mount1.GlobalTransform;
            if (mount2 != null) _camHolder2.GlobalTransform = mount2.GlobalTransform;
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static HoverTank SpawnTank(Node3D root, string name, Vector3 pos, int playerIndex)
        {
            var tank = GD.Load<PackedScene>("res://scenes/HoverTank.tscn")
                         .Instantiate<HoverTank>();
            tank.Name           = name;
            tank.GlobalPosition = pos;
            root.AddChild(tank);

            // Input handler
            var handler = new LocalInputHandler { Target = tank, PlayerIndex = playerIndex };
            tank.AddChild(handler);

            // Weapon input prefix for P2
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
                Stretch = true,
                CustomMinimumSize = new Vector2(w, h),
            };
            container.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            hbox.AddChild(container);

            var viewport = new SubViewport
            {
                Size                  = new Vector2I(w, h),
                OwnWorld3D            = false,
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                // AudioListenerEnable3D makes this viewport respond to audio sources.
                AudioListenerEnable3D = true,
            };
            container.AddChild(viewport);

            // Camera holder: its GlobalTransform is updated each _Process to track
            // the corresponding tank's CameraMount.
            var camHolder = new Node3D { Name = "CameraHolder" };
            viewport.AddChild(camHolder);

            var cam = new Camera3D { Current = true };
            camHolder.AddChild(cam);

            return (viewport, camHolder);
        }

        private static void AddDivider(CanvasLayer layer, Vector2I size)
        {
            var line = new ColorRect
            {
                Color = new Color(0.20f, 1.00f, 0.40f, 0.6f),
            };
            // 2-pixel vertical bar at the centre of the window.
            line.AnchorLeft   = 0.5f; line.OffsetLeft   = -1f;
            line.AnchorTop    = 0f;   line.OffsetTop    = 0f;
            line.AnchorRight  = 0.5f; line.OffsetRight  =  1f;
            line.AnchorBottom = 1f;   line.OffsetBottom = 0f;

            var overlay = new CanvasLayer { Layer = 5 };
            layer.AddChild(overlay);

            var ctrl = new Control();
            ctrl.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            ctrl.MouseFilter = Control.MouseFilterEnum.Ignore;
            overlay.AddChild(ctrl);
            ctrl.AddChild(line);
        }
    }
}
