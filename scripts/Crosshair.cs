using Godot;

namespace HoverTank
{
    // Draws a military-style third-person crosshair at the center of its rect.
    public partial class Crosshair : Control
    {
        private static readonly Color CrossColor = new Color(0.20f, 1.00f, 0.35f, 0.85f);

        public override void _Draw()
        {
            Vector2 c     = Size / 2f;
            float   gap   = 9f;   // gap around the center dot
            float   len   = 13f;  // length of each arm
            float   thick = 1.8f;
            float   r     = 18f;  // circle radius

            // Outer circle
            DrawArc(c, r, 0f, Mathf.Tau, 64, CrossColor, thick);

            // Center dot
            DrawCircle(c, 1.8f, CrossColor);

            // Four arms with a gap around center
            DrawLine(c + new Vector2(0,  -gap), c + new Vector2(0,  -(gap + len)), CrossColor, thick);
            DrawLine(c + new Vector2(0,   gap), c + new Vector2(0,   (gap + len)), CrossColor, thick);
            DrawLine(c + new Vector2(-gap,  0), c + new Vector2(-(gap + len),  0), CrossColor, thick);
            DrawLine(c + new Vector2( gap,  0), c + new Vector2( (gap + len),  0), CrossColor, thick);
        }
    }
}
