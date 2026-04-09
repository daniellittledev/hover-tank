using Godot;

namespace HoverTank
{
    internal static class MathUtils
    {
        /// <summary>
        /// Returns the shortest signed angle from 'from' to 'to', in [-π, π].
        /// Positive result means 'to' is counter-clockwise from 'from'.
        /// </summary>
        public static float AngleDiff(float to, float from)
        {
            float d = (to - from + Mathf.Pi * 3f) % (Mathf.Pi * 2f) - Mathf.Pi;
            return d;
        }
    }
}
