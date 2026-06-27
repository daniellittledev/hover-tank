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

        // Hard cap on queued inputs per peer. A healthy 60 Hz client with a 2-tick
        // drain keeps only ~1-3 packets buffered; more than ~0.25 s of inputs means
        // the client is flooding or badly stalled. We drop the oldest beyond this so
        // a misbehaving (or malicious) peer can't grow the buffer without bound.
        private const int MaxJitterQueue = 16;

        // After this many consecutive extrapolated ticks with no fresh input, treat
        // the peer as stalled and feed neutral input so its tank coasts to a stop
        // instead of driving on its last command forever.
        private const int MaxExtrapolationTicks = 30; // 0.5 s at 60 Hz

        private readonly NetworkManager _net;

        // One physics tank node per connected peer (lives in Main/Tanks).
        private readonly Dictionary<int, HoverTank> _tanks = new();

        // Jitter buffer: queued InputPackets per peer, ordered by Tick.
        private readonly Dictionary<int, Queue<InputPacket>> _jitter = new();

        // Most recent acked sequence per peer (for snapshot piggyback).
        private readonly Dictionary<int, int> _ackedSequence = new();

        // Last applied input per peer (used for extrapolation when buffer empty).
        private readonly Dictionary<int, TankInput> _lastInput = new();

        // Consecutive extrapolated ticks since a real packet was applied, per peer.
        private readonly Dictionary<int, int> _staleTicks = new();

        // Throttle for buffer-overflow log spam.
        private int _overflowLog;

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
            _staleTicks[peerId]  = 0;
        }

        // Called when a peer disconnects.
        public void UnregisterTank(int peerId)
        {
            _tanks.Remove(peerId);
            _jitter.Remove(peerId);
            _ackedSequence.Remove(peerId);
            _lastInput.Remove(peerId);
            _staleTicks.Remove(peerId);
        }

        // Called by NetworkManager when an InputPacket arrives from a client.
        public void OnInputReceived(int peerId, InputPacket pkt)
        {
            if (!_jitter.ContainsKey(peerId)) return;

            // Basic sanity: drop packets from the far future or distant past.
            int serverTick = _net.CurrentTick;
            if (pkt.Tick > serverTick + 10 || pkt.Tick < serverTick - 30) return;

            var queue = _jitter[peerId];
            queue.Enqueue(pkt);

            // Bound the buffer: drop the oldest beyond the cap. This both prevents
            // unbounded memory growth and acts as a rate limit — a flooding peer
            // simply loses its stalest inputs while the freshest survive.
            while (queue.Count > MaxJitterQueue)
            {
                queue.Dequeue();
                if (_overflowLog++ % 120 == 0)
                    GD.Print($"[Net] Peer {peerId} input buffer over cap; dropping oldest.");
            }
        }

        // Reusable scratch list for grid rebuilding — avoids a per-tick allocation.
        private readonly List<Vector3> _gridPositions = new();

        // Called every physics tick from NetworkManager._PhysicsProcess.
        public void Tick(int serverTick)
        {
            // Rebuild the spatial grid from current tank positions so projectiles
            // can skip ray casts when no tank is near their movement step.
            _gridPositions.Clear();
            foreach (var tank in _tanks.Values)
                _gridPositions.Add(tank.GlobalPosition);
            ProjectileSpatialGrid.Instance.Rebuild(_gridPositions);

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
                _staleTicks[peerId] = 0;

                // JumpJustPressed is a one-shot latch — don't extrapolate it.
                return pkt.Input;
            }

            // No fresh input this tick. Briefly extrapolate the last command to ride
            // out normal jitter; but if a peer goes silent for too long, stop driving
            // its tank on a stale command and let it coast to a halt instead.
            if (++_staleTicks[peerId] > MaxExtrapolationTicks)
                return TankInput.Empty;

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
                    Health          = tank.Health,
                });
                snap.AckedSequences[peerId] = _ackedSequence.GetValueOrDefault(peerId, 0);
            }
            snap.Entities = entities.ToArray();

            _net.BroadcastSnapshot(snap);
        }
    }
}
