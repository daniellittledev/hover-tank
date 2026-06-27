using Godot;

namespace HoverTank
{
    // Throwaway visual-capture harness. Instances Main.tscn in TestDrive, lets the
    // world settle, then flies a free camera to three different viewpoints over the
    // terrain (varying angle, altitude and position) and saves a PNG of each:
    //   tools/terrain-shot-1.png  high aerial overview
    //   tools/terrain-shot-2.png  low ground-skim across the dunes
    //   tools/terrain-shot-3.png  wide mid-altitude from the opposite corner
    // Run with:
    //   Godot --path . res://tools/TerrainShots.tscn
    // Not shipped.
    public partial class TerrainShots : Node
    {
        private int _frame;
        private int _shot = -1;     // -1 = waiting for the world to settle
        private int _shotFrame;
        private Camera3D _cam = null!;
        private TerrainGenerator? _terrain;

        public override void _Ready()
        {
            var gs = GameState.Instance;
            gs.Mode = GameMode.SinglePlayer;
            gs.SinglePlayerMode = SinglePlayerMode.TestDrive;

            var main = GD.Load<PackedScene>("res://scenes/Main.tscn").Instantiate();
            GetTree().Root.CallDeferred(Node.MethodName.AddChild, main);
        }

        public override void _Process(double delta)
        {
            _frame++;

            if (_shot < 0)
            {
                // ~240 frames ≈ 4 s: terrain build + chunk stream + tank settle.
                if (_frame < 240) return;

                _terrain = GetTree().GetFirstNodeInGroup("terrain") as TerrainGenerator;
                _cam = new Camera3D { Fov = 65, Far = 3000 };
                GetTree().Root.AddChild(_cam);
                _cam.MakeCurrent();
                _shot = 0;
                _shotFrame = 0;
                PlaceCamera(0);
                return;
            }

            _shotFrame++;
            if (_shotFrame < 5) return;   // let the moved camera render a clean frame

            CaptureCurrent();
            _shot++;
            if (_shot >= 3) { GetTree().Quit(); return; }
            _shotFrame = 0;
            PlaceCamera(_shot);
        }

        // Terrain surface height at (x, z); 0 if the terrain isn't resolvable.
        private float H(float x, float z) => _terrain?.HeightAt(x, z) ?? 0f;

        private void PlaceCamera(int i)
        {
            Vector3 pos, look;
            switch (i)
            {
                case 0: // High aerial overview, angled down at the basin
                    pos = new Vector3(0f, H(0f, 70f) + 70f, 70f);
                    look = new Vector3(0f, H(0f, 0f), 0f);
                    break;
                case 1: // Low ground-skim, eye-level across the rolling dunes
                    pos = new Vector3(35f, H(35f, 35f) + 4f, 35f);
                    look = new Vector3(-25f, H(-25f, -15f) + 2f, -15f);
                    break;
                default: // Wide, mid-altitude from the opposite corner
                    pos = new Vector3(-85f, H(-85f, -85f) + 32f, -85f);
                    look = new Vector3(0f, H(0f, 0f) + 4f, 0f);
                    break;
            }
            _cam.GlobalPosition = pos;
            _cam.LookAt(look, Vector3.Up);
        }

        private void CaptureCurrent()
        {
            var img = GetViewport().GetTexture().GetImage();
            string path = ProjectSettings.GlobalizePath($"res://tools/terrain-shot-{_shot + 1}.png");
            var err = img.SavePng(path);
            GD.Print($"[SHOT] {_shot + 1} -> {path} ({err})");
        }
    }
}
