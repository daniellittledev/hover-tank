using System.Collections.Generic;
using Godot;

namespace HoverTank
{
    /// <summary>
    /// Offline-mode counterpart to <see cref="HoverTank.Network.ServerSimulation"/>.
    /// Runs in SinglePlayer and SplitScreen modes where no server tick loop
    /// exists. Currently its sole responsibility is to rebuild the
    /// <see cref="ProjectileSpatialGrid"/> each physics tick so projectile
    /// ray-casts can short-circuit when no tank is in range — the same
    /// pre-filter the server uses.
    ///
    /// Added as a child of <see cref="GameSetup"/> by the offline branches of
    /// its mode switch.
    /// </summary>
    public partial class OfflineSimulation : Node
    {
        // Reused each tick to avoid per-frame allocation.
        private readonly List<Vector3> _tankPositions = new();

        public override void _PhysicsProcess(double delta)
        {
            _tankPositions.Clear();
            foreach (Node node in GetTree().GetNodesInGroup("hover_tanks"))
            {
                if (node is HoverTank tank)
                    _tankPositions.Add(tank.GlobalPosition);
            }
            ProjectileSpatialGrid.Instance.Rebuild(_tankPositions);
        }
    }
}
