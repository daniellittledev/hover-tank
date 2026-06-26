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

        public override void _Ready()
        {
            foreach (var a in OS.GetCmdlineUserArgs())
                if (a == "testdrive" || a == "waves") _mode = a;

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
            // ~240 frames ≈ 4 s: enough for terrain build, tank spawn + settle.
            if (_frame != 240) return;

            var img = GetViewport().GetTexture().GetImage();
            string path = $"D:/dev/hover-tank/tools/shot-{_mode}.png";
            var err = img.SavePng(path);
            GD.Print($"[SHOT] {_mode} -> {path} ({err})");
            GetTree().Quit();
        }
    }
}
