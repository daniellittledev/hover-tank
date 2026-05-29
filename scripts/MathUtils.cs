using Godot;

namespace HoverTank
{
    internal static class MathUtils
    {
        /// <summary>
        /// Returns the shortest signed angle from 'from' to 'to', in [-π, π].
        /// Positive result means 'to' is counter-clockwise from 'from'.
        ///
        /// Uses Mathf.PosMod (not C#'s '%') because '%' preserves the sign of
        /// the dividend. When accumulated mouse yaw grows large and negative,
        /// '%' produces a result outside [-π, π], feeding a garbage error into
        /// the auto-steer PD controller and sending the tank into an unstoppable
        /// spin. PosMod returns a value in [0, divisor) regardless of sign.
        /// </summary>
        public static float AngleDiff(float to, float from)
        {
            return Mathf.PosMod(to - from + Mathf.Pi, Mathf.Pi * 2f) - Mathf.Pi;
        }

        /// <summary>
        /// Wraps an angle in radians to [-π, π]. Use to keep accumulated yaw
        /// values (e.g. camera free-look) bounded so single-precision float
        /// resolution doesn't degrade after prolonged play.
        /// </summary>
        public static float WrapAngle(float angle)
        {
            return Mathf.PosMod(angle + Mathf.Pi, Mathf.Pi * 2f) - Mathf.Pi;
        }

        /// <summary>
        /// Squared horizontal length of the Z axis below which a body's heading
        /// is treated as undefined — roughly the forward axis within ~10° of
        /// vertical. See <see cref="TryGetHeading"/>.
        /// </summary>
        public const float HeadingHorizThresholdSq = 0.03f;

        /// <summary>
        /// Reads a body's heading (yaw) from its basis using the "behind" angle
        /// convention shared by the camera, hull auto-steer, and turret:
        /// Atan2(basis.Z.X, basis.Z.Z), so yaw=0 places the camera behind a tank
        /// whose forward axis is -Z.
        ///
        /// When the body pitches or rolls so its forward axis nears vertical, the
        /// horizontal projection of Z vanishes and the angle is undefined
        /// (Atan2(0,0)→0). Returns false in that case so callers can hold their
        /// previous heading instead of snapping to an arbitrary 0 — which would
        /// otherwise feed a garbage error into the auto-steer PD and spin an
        /// already-tilted tank.
        /// </summary>
        public static bool TryGetHeading(Basis basis, out float yaw)
        {
            if (basis.Z.X * basis.Z.X + basis.Z.Z * basis.Z.Z < HeadingHorizThresholdSq)
            {
                yaw = 0f;
                return false;
            }
            yaw = Mathf.Atan2(basis.Z.X, basis.Z.Z);
            return true;
        }
    }
}
