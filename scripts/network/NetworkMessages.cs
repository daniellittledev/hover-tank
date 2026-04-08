using Godot;
using System.Collections.Generic;

namespace HoverTank.Network
{
    // ── Input ────────────────────────────────────────────────────────────────
    // Boolean input snapshot packed into 1 byte. Sent client → server each tick.
    // JumpJustPressed is a one-shot latch: true only on the tick the key went
    // down, so the impulse fires exactly once regardless of network rate.
    public struct TankInput
    {
        public bool Forward;
        public bool Backward;
        public bool Left;
        public bool Right;
        public bool JumpJet;
        public bool JumpJustPressed;

        public static readonly TankInput Empty = default;

        public byte Pack()
        {
            byte b = 0;
            if (Forward)         b |= 1 << 0;
            if (Backward)        b |= 1 << 1;
            if (Left)            b |= 1 << 2;
            if (Right)           b |= 1 << 3;
            if (JumpJet)         b |= 1 << 4;
            if (JumpJustPressed) b |= 1 << 5;
            return b;
        }

        public static TankInput Unpack(byte b) => new TankInput
        {
            Forward         = (b & (1 << 0)) != 0,
            Backward        = (b & (1 << 1)) != 0,
            Left            = (b & (1 << 2)) != 0,
            Right           = (b & (1 << 3)) != 0,
            JumpJet         = (b & (1 << 4)) != 0,
            JumpJustPressed = (b & (1 << 5)) != 0,
        };
    }

    // ── Input packet ─────────────────────────────────────────────────────────
    // Sent client → server every physics tick.
    // Tick: client's physics tick counter.
    // Sequence: monotonically increasing per client; server acks the highest
    //           sequence it applied in each StateSnapshot.
    public struct InputPacket
    {
        public int Tick;
        public int Sequence;
        public TankInput Input;
    }

    // ── Entity state ─────────────────────────────────────────────────────────
    // Physics state for one tank inside a StateSnapshot.
    public struct EntityState
    {
        public int PeerId;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 LinearVelocity;
        public Vector3 AngularVelocity;
    }

    // ── State snapshot ───────────────────────────────────────────────────────
    // Produced by the server every 3 ticks (~20 Hz).
    // AckedSequences: per-client highest InputPacket.Sequence the server applied.
    //                 Used by each client to trim its prediction ring buffer.
    // When decoded on the client, AckedSequence holds the value for that client.
    public class StateSnapshot
    {
        public int ServerTick;
        public int AckedSequence;          // populated on client after decode
        public EntityState[] Entities = System.Array.Empty<EntityState>();

        // Server-side: one acked sequence per peer.
        public readonly Dictionary<int, int> AckedSequences = new();

        public int GetAckedSequenceFor(int peerId) =>
            AckedSequences.TryGetValue(peerId, out int seq) ? seq : 0;
    }
}
