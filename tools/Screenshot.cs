using Godot;

namespace HoverTank
{
    // Throwaway visual-capture harness. Run as the scene argument, e.g.:
    //   Godot --path . res://tools/Screenshot.tscn ++ waves
    //   Godot --path . res://tools/Screenshot.tscn ++ testdrive
    // It sets up GameState, instances Main.tscn under the root (so this node
    // stays alive across the gameplay scene), lets the world settle, captures a
    // viewport screenshot to tools/shot-<mode>.png, then quits. Not shipped.
    public partial class Screenshot : Node
    {
        private int _frame;
        private string _mode = "testdrive";
        private bool _overhead;

        public override void _Ready()
        {
            foreach (var a in OS.GetCmdlineUserArgs())
            {
                if (a == "testdrive" || a == "waves") _mode = a;
                if (a == "overhead") _overhead = true;
            }

            var gs = GameState.Instance;
            gs.Mode = GameMode.SinglePlayer;
            gs.SinglePlayerMode = _mode == "testdrive"
                ? SinglePlayerMode.TestDrive
                : SinglePlayerMode.StandardWaves;

            var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate();
            GetTree().Root.CallDeferred(Node.MethodName.AddChild, main);
        }

        public override void _Process(double delta)
        {
            _frame++;

            // Just before capture, override the active camera with a top-down
            // orthographic view that frames the whole arena (dev-only diagnostic).
            if (_overhead && _frame == 220)
            {
                // Fog hazes the whole arena to flat teal when viewed through
                // ~300 m of air, so kill it for this top-down diagnostic.
                foreach (var n in GetTree().Root.FindChildren("*", "WorldEnvironment", true, false))
                    if (n is WorldEnvironment we && we.Environment is Godot.Environment env)
                        env.FogEnabled = false;

                var cam = new Camera3D
                {
                    Projection      = Camera3D.ProjectionType.Orthogonal,
                    Size            = 440f,
                    Position        = new Vector3(0f, 300f, 0f),
                    RotationDegrees = new Vector3(-90f, 0f, 0f),
                    Far             = 1000f,
                };
                GetTree().Root.AddChild(cam);
                cam.MakeCurrent();
            }

            // ~240 frames ≈ 4 s: enough for terrain build, tank spawn + settle.
            if (_frame != 240) return;

            var img = GetViewport().GetTexture().GetImage();
            string suffix = _overhead ? "-overhead" : "";
            string path = $"D:/dev/hover-tank/tools/shot-{_mode}{suffix}.png";
            var err = img.SavePng(path);
            GD.Print($"[SHOT] {_mode}{suffix} -> {path} ({err})");
            GetTree().Quit();
        }
    }
}
