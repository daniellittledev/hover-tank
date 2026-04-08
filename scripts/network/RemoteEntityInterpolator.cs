using Godot;
using HoverTank.Network;

namespace HoverTank
{
    // Attached to each remote tank ghost node. Buffers server snapshots and
    // smoothly interpolates position/rotation between them for display.
    //
    // Render time is kept 2 snapshot intervals behind the latest received
    // snapshot, providing a jitter absorption window of ~100 ms at 20 Hz.
    public partial class RemoteEntityInterpolator : Node
    {
        // Target ghost node to drive (a frozen HoverTank or any Node3D).
        public Node3D? TargetNode { get; set; }

        // Snapshot buffer depth — 8 entries covers ~400 ms at 20 Hz.
        private const int BufferSize = 8;

        // How many snapshot intervals behind the latest snapshot to render.
        // 2 × (1/20 Hz) = 100 ms interpolation delay.
        private const float InterpolationDelay = 2f / 20f;

        private readonly SnapshotEntry[] _buffer = new SnapshotEntry[BufferSize];
        private int _head = -1;   // index of most recently received snapshot
        private int _count;

        // Render time in "snapshot time" (server ticks converted to seconds).
        private float _renderTime = -1f;

        private struct SnapshotEntry
        {
            public int Tick;
            public float Time;   // Tick / 20f (seconds, same timescale for lerp)
            public Vector3 Position;
            public Quaternion Rotation;
        }

        // ── Public API ────────────────────────────────────────────────────────

        // Called by ClientSimulation each time a snapshot arrives.
        public void PushSnapshot(int serverTick, EntityState state)
        {
            _head = (_head + 1) % BufferSize;
            _buffer[_head] = new SnapshotEntry
            {
                Tick     = serverTick,
                Time     = serverTick / 20f,
                Position = state.Position,
                Rotation = state.Rotation,
            };
            if (_count < BufferSize) _count++;

            // Bootstrap render time on first arrival.
            if (_renderTime < 0f)
                _renderTime = _buffer[_head].Time - InterpolationDelay;

            // Health is discrete — apply immediately rather than interpolating.
            if (TargetNode is HoverTank ghost)
                ghost.Health = state.Health;
        }

        // ── Godot callbacks ───────────────────────────────────────────────────

        public override void _Process(double delta)
        {
            if (TargetNode == null || _count < 2) return;

            // Advance render clock.
            _renderTime += (float)delta;

            // Find the two snapshots bracketing _renderTime.
            if (!FindBracket(out SnapshotEntry from, out SnapshotEntry to)) return;

            float span = to.Time - from.Time;
            float t = span > 0f ? (_renderTime - from.Time) / span : 1f;
            t = Mathf.Clamp(t, 0f, 1f);

            TargetNode.GlobalPosition = from.Position.Lerp(to.Position, t);
            TargetNode.GlobalBasis    = new Basis(from.Rotation.Slerp(to.Rotation, t));
        }

        // ── Private helpers ───────────────────────────────────────────────────

        // Locate the pair of buffered snapshots that straddle _renderTime.
        // Returns false if the buffer has fewer than 2 entries or render time
        // is outside the buffered window (ghost pauses — no extrapolation).
        private bool FindBracket(out SnapshotEntry from, out SnapshotEntry to)
        {
            from = default;
            to   = default;

            // Walk backwards from newest to find the first entry at or before renderTime.
            SnapshotEntry? prev = null;
            for (int i = 0; i < _count; i++)
            {
                int idx = (_head - i + BufferSize) % BufferSize;
                var entry = _buffer[idx];

                if (entry.Time <= _renderTime)
                {
                    from = entry;
                    to   = prev ?? entry;
                    return true;
                }
                prev = entry;
            }
            return false;
        }
    }
}
