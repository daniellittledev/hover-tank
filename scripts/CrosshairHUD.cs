using Godot;

namespace HoverTank
{
    /// <summary>
    /// Minimal crosshair drawn in screen centre.
    /// Attach to a Control node inside a CanvasLayer in HoverTank.tscn.
    /// </summary>
    public partial class CrosshairHUD : Control
    {
        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Ignore;
            SetAnchorsPreset(LayoutPreset.FullRect);
            // Redraw once on startup, then again whenever the viewport is resized.
            GetViewport().SizeChanged += QueueRedraw;
            QueueRedraw();
        }

        public override void _Draw()
        {
            Vector2 center = GetViewportRect().Size / 2f;
            var color = new Color(1f, 1f, 1f, 0.85f);
            const float arm   = 10f;
            const float gap   = 4f;
            const float width = 1.5f;

            DrawLine(center + Vector2.Left  * (arm + gap), center + Vector2.Left  * gap, color, width);
            DrawLine(center + Vector2.Right * gap,         center + Vector2.Right * (arm + gap), color, width);
            DrawLine(center + Vector2.Up    * (arm + gap), center + Vector2.Up    * gap, color, width);
            DrawLine(center + Vector2.Down  * gap,         center + Vector2.Down  * (arm + gap), color, width);
            DrawCircle(center, 1.5f, color);
        }
    }
}
