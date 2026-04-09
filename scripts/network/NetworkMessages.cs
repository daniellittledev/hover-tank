using Godot;
using System.Collections.Generic;

namespace HoverTank.Network
{
    // ── Input ────────────────────────────────────────────────────────────────
    // Analog input snapshot. Throttle/Steer are -1..+1 floats so controller
    // analog sticks produce proportional force. JumpJustPressed is a one-shot
    // latch: true only on the tick the button went down.
    public struct TankInput
    {
        // +1 = full forward, -1 = full backward.
        public float Throttle;
        // +1 = full left, -1 = full right.
        public float Steer;
        public bool JumpJet;
        public bool JumpJustPressed;
        // World-space camera yaw (radians). Used for auto-steer and turret rotation.
        public float AimYaw;
        // Camera pitch (radians). Used for barrel elevation.
        public float AimPitch;

        public static readonly TankInput Empty = default;

        // Encodes only the boolean flags — axes are passed as separate floats.
        public byte PackFlags()
        {
            byte b = 0;
            if (JumpJet)         b |= 1 << 0;
            if (JumpJustPressed) b |= 1 << 1;
            return b;
        }

        public static TankInput FromParts(byte flags, float throttle, float steer,
                                          float aimYaw = 0f, float aimPitch = 0f) => new TankInput
        {
            Throttle        = throttle,
            Steer           = steer,
            JumpJet         = (flags & (1 << 0)) != 0,
            JumpJustPressed = (flags & (1 << 1)) != 0,
            AimYaw          = aimYaw,
            AimPitch        = aimPitch,
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
        // Server-authoritative health — clients apply this to keep damage in sync.
        public float Health;
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
