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
    }
}
