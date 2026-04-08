using Godot;
using HoverTank.Network;
using System.Collections.Generic;

namespace HoverTank
{
    // Runs on each client. Owns:
    //   - Local player input capture and ring-buffer storage (prediction).
    //   - Server snapshot reconciliation (snap + re-apply unacked inputs).
    //   - Forwarding of remote entity states to their interpolators.
    public class ClientSimulation
    {
        // Ring buffer size — must be power-of-2 or at least larger than the
        // maximum number of unacknowledged frames (RTT * tickRate).
        // 256 frames = ~4 seconds at 60 Hz — enough for any reasonable RTT.
        private const int RingSize = 256;

        // Snap to server position if predicted error exceeds this distance (m).
        private const float ReconcileThreshold = 0.5f;

        private readonly NetworkManager _net;
        private HoverTank? _localTank;

        // Sequence counter: increments every tick, acked by server in snapshots.
        private int _sequence;

        // One-shot latch: set when jump key goes down, cleared after sending.
        private bool _jumpLatch;

        // ── Prediction ring buffer ────────────────────────────────────────────
        private readonly PredictedFrame[] _ring = new PredictedFrame[RingSize];

        private struct PredictedFrame
        {
            public int Tick;
            public int Sequence;
            public TankInput Input;
            // Physics state at the START of this tick (before forces are applied).
            // We compare this against the server's state for the same tick.
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 LinearVelocity;
            public Vector3 AngularVelocity;
            public bool Valid;
        }

        public ClientSimulation(NetworkManager net)
        {
            _net = net;
        }

        // Called by NetworkManager after spawning the local tank node.
        public void SetLocalTank(HoverTank tank)
        {
            _localTank = tank;
        }

        // ── Per-tick update ───────────────────────────────────────────────────

        // Called from NetworkManager._PhysicsProcess, before the physics step.
        public void Tick(int clientTick, float delta)
        {
            if (_localTank == null) return;

            // Snapshot the pre-tick state into the ring buffer.
            int slot = clientTick % RingSize;
            _ring[slot] = new PredictedFrame
            {
                Tick            = clientTick,
                Sequence        = _sequence,
                Position        = _localTank.GlobalPosition,
                Rotation        = _localTank.GlobalBasis.GetRotationQuaternion(),
                LinearVelocity  = _localTank.LinearVelocity,
                AngularVelocity = _localTank.AngularVelocity,
                Valid           = true,
            };

            // Capture input for this tick.
            var input = CaptureInput();
            _ring[slot].Input = input;

            // Apply to local tank for immediate prediction.
            _localTank.SetInput(input);

            // Send to server.
            _net.SendInput(clientTick, _sequence, input);
            _sequence++;

            // Clear the one-shot latch after sending.
            _jumpLatch = false;
        }

        // ── Snapshot reconciliation ───────────────────────────────────────────

        // Called by NetworkManager when a StateSnapshot arrives from the server.
        public void OnSnapshotReceived(StateSnapshot snap, Dictionary<int, RemoteEntityInterpolator> remotes)
        {
            int localId = _net.Multiplayer.GetUniqueId();

            foreach (var entity in snap.Entities)
            {
                if (entity.PeerId == localId)
                    ReconcileLocalTank(snap, entity);
                else if (remotes.TryGetValue(entity.PeerId, out var interp))
                    interp.PushSnapshot(snap.ServerTick, entity);
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private TankInput CaptureInput()
        {
            bool jumpDown = Input.IsActionPressed("jump_jet");

            // Detect rising edge for the one-shot latch.
            if (Input.IsActionJustPressed("jump_jet"))
                _jumpLatch = true;

            // GetAxis returns -1..+1 and handles both keyboard (binary) and
            // analog sticks (continuous) through the unified action map.
            return new TankInput
            {
                Throttle        = Input.GetAxis("move_backward", "move_forward"),
                Steer           = Input.GetAxis("move_right",    "move_left"),
                JumpJet         = jumpDown,
                JumpJustPressed = _jumpLatch,
            };
        }

        private void ReconcileLocalTank(StateSnapshot snap, EntityState serverState)
        {
            if (_localTank == null) return;

            int slot = snap.ServerTick % RingSize;
            ref var predicted = ref _ring[slot];
            if (!predicted.Valid || predicted.Tick != snap.ServerTick) return;

            float error = (serverState.Position - predicted.Position).Length();
            if (error <= ReconcileThreshold) return;

            GD.Print($"[Client] Reconcile at tick {snap.ServerTick}, error={error:F2}m");

            // Snap to server-authoritative state.
            _localTank.GlobalPosition   = serverState.Position;
            _localTank.GlobalBasis      = new Basis(serverState.Rotation);
            _localTank.LinearVelocity   = serverState.LinearVelocity;
            _localTank.AngularVelocity  = serverState.AngularVelocity;

            // Re-apply all unacked inputs that the server has not yet confirmed.
            // This is an approximate re-simulation: it accumulates the same forces
            // the server will apply on its next ticks, which converges quickly for
            // the slow physics of a hover tank.
            for (int i = 1; i < RingSize; i++)
            {
                int t = (snap.ServerTick + i) % RingSize;
                ref var frame = ref _ring[t];
                if (!frame.Valid) continue;
                if (frame.Sequence <= snap.AckedSequence) continue;
                if (frame.Tick <= snap.ServerTick) continue;

                _localTank.ApplyInputForces(frame.Input);
            }
        }
    }
}
