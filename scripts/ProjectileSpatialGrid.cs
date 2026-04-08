using Godot;
using System.Collections.Generic;

namespace HoverTank
{
    // 2-D (XZ-plane) spatial hash grid.
    //
    // The server rebuilds the grid once per physics tick (O(n_tanks)) in
    // ServerSimulation.Tick. Projectiles then consult it as a fast pre-filter
    // before entering the Godot physics ray-cast path.
    //
    // On clients the grid is never populated (Count == 0), so visual-only
    // projectiles always fall through to their normal no-collision path.
    // In offline (Standalone) mode the grid is also empty, so every projectile
    // performs the full ray cast — preserving correct single-player behaviour.
    public sealed class ProjectileSpatialGrid
    {
        // Global singleton — lives for the process lifetime.
        public static readonly ProjectileSpatialGrid Instance = new();

        // Each cell covers this many metres per axis. 20 m means at most
        // ceil(queryRadius/20)² cell reads for typical query radii.
        public const float CellSize = 20f;

        private readonly Dictionary<(int x, int z), List<Vector3>> _cells = new();
        private int _count;

        private ProjectileSpatialGrid() { }

        // Number of positions currently stored in the grid.
        public int Count => _count;

        // Discard all entries and insert fresh positions.
        // Called once per physics tick by ServerSimulation.
        public void Rebuild(List<Vector3> positions)
        {
            // Reuse existing list objects to avoid per-tick GC pressure.
            foreach (var list in _cells.Values) list.Clear();
            _count = 0;

            foreach (var pos in positions)
            {
                var key = Cell(pos);
                if (!_cells.TryGetValue(key, out var list))
                {
                    list = new List<Vector3>(4);
                    _cells[key] = list;
                }
                list.Add(pos);
                _count++;
            }
        }

        // Returns true if any stored position is within 'radius' metres of 'point'.
        // Only the grid cells that overlap the query sphere are visited.
        public bool HasAnyWithin(Vector3 point, float radius)
        {
            if (_count == 0) return false;

            float r2     = radius * radius;
            int   span   = Mathf.CeilToInt(radius / CellSize);
            int   cx     = Mathf.FloorToInt(point.X / CellSize);
            int   cz     = Mathf.FloorToInt(point.Z / CellSize);

            for (int dx = -span; dx <= span; dx++)
            for (int dz = -span; dz <= span; dz++)
            {
                if (!_cells.TryGetValue((cx + dx, cz + dz), out var list)) continue;
                foreach (var p in list)
                {
                    float ex = p.X - point.X;
                    float ey = p.Y - point.Y;
                    float ez = p.Z - point.Z;
                    if (ex * ex + ey * ey + ez * ez <= r2)
                        return true;
                }
            }
            return false;
        }

        private static (int x, int z) Cell(Vector3 pos) =>
            (Mathf.FloorToInt(pos.X / CellSize), Mathf.FloorToInt(pos.Z / CellSize));
    }
}
