using Godot;
using HoverTank.Network;
using System.Collections.Generic;

namespace HoverTank
{
    // Runs only when this peer is the server (host).
    // Maintains authoritative physics for all tanks, applies client inputs from
    // per-client jitter buffers, and broadcasts state snapshots at ~20 Hz.
    public class ServerSimulation
    {
        // Snapshots are broadcast every N physics ticks (60 Hz / 3 = 20 Hz).
        private const int SnapshotInterval = 3;

        // Inputs with tick <= (serverTick - JitterDelay) are consumed each tick.
        // 2-tick buffer absorbs single-packet jitter without adding much latency.
        private const int JitterDelay = 2;

        // Server rejects inputs that imply movement faster than this multiplier
        // over the tank's configured MaxSpeed.
        private const float MaxSpeedMultiplier = 1.5f;

        private readonly NetworkManager _net;

        // One physics tank node per connected peer (lives in Main/Tanks).
        private readonly Dictionary<int, HoverTank> _tanks = new();

        // Jitter buffer: queued InputPackets per peer, ordered by Tick.
        private readonly Dictionary<int, Queue<InputPacket>> _jitter = new();

        // Most recent acked sequence per peer (for snapshot piggyback).
        private readonly Dictionary<int, int> _ackedSequence = new();

        // Last applied input per peer (used for extrapolation when buffer empty).
        private readonly Dictionary<int, TankInput> _lastInput = new();

        public ServerSimulation(NetworkManager net)
        {
            _net = net;
        }

        // Called by NetworkManager when a tank node is spawned for a peer.
        public void RegisterTank(int peerId, HoverTank tank)
        {
            _tanks[peerId]       = tank;
            _jitter[peerId]      = new Queue<InputPacket>();
            _ackedSequence[peerId] = 0;
            _lastInput[peerId]   = TankInput.Empty;
        }

        // Called when a peer disconnects.
        public void UnregisterTank(int peerId)
        {
            _tanks.Remove(peerId);
            _jitter.Remove(peerId);
            _ackedSequence.Remove(peerId);
            _lastInput.Remove(peerId);
        }

        // Called by NetworkManager when an InputPacket arrives from a client.
        public void OnInputReceived(int peerId, InputPacket pkt)
        {
            if (!_jitter.ContainsKey(peerId)) return;

            // Basic sanity: drop packets from the far future or distant past.
            int serverTick = _net.CurrentTick;
            if (pkt.Tick > serverTick + 10 || pkt.Tick < serverTick - 30) return;

            _jitter[peerId].Enqueue(pkt);
        }

        // Called every physics tick from NetworkManager._PhysicsProcess.
        public void Tick(int serverTick)
        {
            foreach (var (peerId, tank) in _tanks)
            {
                var input = DrainInput(peerId, serverTick);
                ValidateInput(tank, input);
                tank.SetInput(input);
            }

            if (serverTick % SnapshotInterval == 0)
                BroadcastSnapshot(serverTick);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        // Pop the best input for this tick from the jitter buffer.
        // Falls back to the last received input (extrapolation) if nothing ready.
        private TankInput DrainInput(int peerId, int serverTick)
        {
            var queue = _jitter[peerId];

            // Discard stale packets (older than what we need this tick).
            while (queue.Count > 0 && queue.Peek().Tick < serverTick - JitterDelay)
                queue.Dequeue();

            if (queue.Count > 0 && queue.Peek().Tick <= serverTick - JitterDelay + 1)
            {
                var pkt = queue.Dequeue();
                _ackedSequence[peerId] = Mathf.Max(_ackedSequence[peerId], pkt.Sequence);
                _lastInput[peerId] = pkt.Input;

                // JumpJustPressed is a one-shot latch — don't extrapolate it.
                return pkt.Input;
            }

            // Extrapolate: repeat last known input but clear the one-shot latch.
            var ext = _lastInput[peerId];
            ext.JumpJustPressed = false;
            return ext;
        }

        // Reject inputs that would make the tank move faster than physically
        // possible. Clamps rather than disconnecting to be lenient with lag.
        private void ValidateInput(HoverTank tank, TankInput input)
        {
            float maxAllowed = tank.MaxSpeed * MaxSpeedMultiplier;
            if (tank.LinearVelocity.LengthSquared() > maxAllowed * maxAllowed)
            {
                // Clamp velocity — the physics forces will do the rest next tick.
                tank.LinearVelocity = tank.LinearVelocity.Normalized() * maxAllowed;
            }
        }

        // Collect state from all tanks and send to each connected peer.
        private void BroadcastSnapshot(int serverTick)
        {
            var snap = new StateSnapshot { ServerTick = serverTick };

            var entities = new List<EntityState>(_tanks.Count);
            foreach (var (peerId, tank) in _tanks)
            {
                entities.Add(new EntityState
                {
                    PeerId          = peerId,
                    Position        = tank.GlobalPosition,
                    Rotation        = tank.GlobalBasis.GetRotationQuaternion(),
                    LinearVelocity  = tank.LinearVelocity,
                    AngularVelocity = tank.AngularVelocity,
                });
                snap.AckedSequences[peerId] = _ackedSequence.GetValueOrDefault(peerId, 0);
            }
            snap.Entities = entities.ToArray();

            _net.BroadcastSnapshot(snap);
        }
    }
}
