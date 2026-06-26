using Godot;

namespace HoverTank
{
    // Throwaway hull-inspection harness. Renders just the craft hull
    // (TankMeshBuilder) from four fixed viewpoints — TOP, FRONT, LEFT, and a
    // behind-the-ship 3/4 ANGLE (matching the reference art's vantage) — laid out
    // in a 2x2 grid like a 3D editor, then captures the window to
    // tools/hull-preview.png and quits. No terrain, no gameplay. Run:
    //   Godot --path . res://tools/HullPreview.tscn
    public partial class HullPreview : Node
    {
        private int _frame;

        private const int Cell = 480;   // panel size
        private const int Pad = 8;
        private const int LabelH = 22;

        public override void _Ready()
        {
            int w = Pad + 2 * (Cell + Pad);
            int h = Pad + 2 * (LabelH + Cell + Pad);
            GetWindow().Size = new Vector2I(w, h);

            var root = new Control();
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(root);

            // Orthographic elevations for clean measurement, perspective for the 3/4.
            AddView(root, 0, "TOP (nose up)",   new Vector3(0f, 8f, 0f),     new Vector3(0, 0, -1), true, 3.4f);
            AddView(root, 1, "FRONT (nose-on)", new Vector3(0f, 0.4f, -8f),  Vector3.Up,           true, 3.4f);
            AddView(root, 2, "LEFT (side)",     new Vector3(-8f, 0.4f, 0f),  Vector3.Up,           true, 3.4f);
            AddView(root, 3, "ANGLE (behind)",  new Vector3(2.6f, 2.0f, 5.2f), Vector3.Up,         false, 0f);
        }

        private void AddView(Control root, int index, string title, Vector3 camPos, Vector3 up, bool ortho, float orthoSize)
        {
            int col = index % 2, rowi = index / 2;
            float x = Pad + col * (Cell + Pad);
            float y = Pad + rowi * (LabelH + Cell + Pad);

            root.AddChild(new Label
            {
                Text = title,
                Position = new Vector2(x, y),
                Size = new Vector2(Cell, LabelH),
                HorizontalAlignment = HorizontalAlignment.Center,
            });

            var container = new SubViewportContainer
            {
                Stretch = true,
                Position = new Vector2(x, y + LabelH),
                Size = new Vector2(Cell, Cell),
            };
            root.AddChild(container);

            var vp = new SubViewport
            {
                Size = new Vector2I(Cell, Cell),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            };
            container.AddChild(vp);

            // Flat neutral background + cool ambient so the form reads from any side.
            var worldEnv = new WorldEnvironment();
            var env = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.22f, 0.24f, 0.28f),
                AmbientLightColor = new Color(0.60f, 0.66f, 0.78f),
                AmbientLightEnergy = 2.4f,
            };
            worldEnv.Environment = env;
            vp.AddChild(worldEnv);

            var key = new DirectionalLight3D { LightEnergy = 1.3f };
            key.RotationDegrees = new Vector3(-50f, 35f, 0f);
            vp.AddChild(key);

            // Straight-down fill so flat top decks read in the TOP view.
            var top = new DirectionalLight3D { LightEnergy = 1.8f, ShadowEnabled = false };
            top.RotationDegrees = new Vector3(-90f, 0f, 0f);
            vp.AddChild(top);

            var fill = new DirectionalLight3D
            {
                LightColor = new Color(0.6f, 0.72f, 0.95f),
                LightEnergy = 0.45f,
                ShadowEnabled = false,
            };
            fill.RotationDegrees = new Vector3(-15f, -140f, 0f);
            vp.AddChild(fill);

            vp.AddChild(new TankMeshBuilder());   // builds its own mesh + material on _Ready

            var cam = new Camera3D { Position = camPos };
            if (ortho)
            {
                cam.Projection = Camera3D.ProjectionType.Orthogonal;
                cam.Size = orthoSize;
            }
            else
            {
                cam.Fov = 48f;
            }
            vp.AddChild(cam);
            cam.LookAt(Vector3.Zero, up);
        }

        public override void _Process(double delta)
        {
            _frame++;
            if (_frame != 30) return;   // let all four subviewports render

            var img = GetViewport().GetTexture().GetImage();
            const string path = "D:/dev/hover-tank/tools/hull-preview.png";
            var err = img.SavePng(path);
            GD.Print($"[HULL] -> {path} ({err})");
            GetTree().Quit();
        }
    }
}
