using Godot;

namespace HoverTank
{
    // Shared menu/overlay styling: the green-on-dark palette and the panel,
    // button-box, and separator builders that were previously copy-pasted across
    // MainMenu, PauseMenu, GameSetup and WaveManager. Per-screen specifics (font
    // sizes, panel margin/alpha, button min-size) stay at the call site; the
    // canonical colours, corner radii and box construction live here so a theme
    // change is a single edit. Kept code-only to match the no-baked-assets style.
    public static class UiTheme
    {
        // Palette (matches the HUD's green-on-near-black look).
        public static readonly Color Green    = new(0.20f, 1.00f, 0.40f);
        public static readonly Color Dim      = new(0.50f, 0.50f, 0.50f);
        public static readonly Color Text     = new(0.85f, 0.85f, 0.85f);
        public static readonly Color BtnHover = new(0.20f, 1.00f, 0.40f, 0.12f);

        // Rounded dark panel. bgAlpha and marginX vary per screen; the vertical
        // margin is 8 px tighter than the horizontal at every existing call site.
        public static StyleBoxFlat PanelStyle(float bgAlpha = 0.82f, int marginX = 32) => new()
        {
            BgColor                 = new Color(0f, 0f, 0f, bgAlpha),
            CornerRadiusTopLeft     = 8, CornerRadiusTopRight    = 8,
            CornerRadiusBottomLeft  = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft       = marginX,     ContentMarginRight  = marginX,
            ContentMarginTop        = marginX - 8, ContentMarginBottom = marginX - 8,
        };

        // Invisible box for a button's normal/focus states.
        public static StyleBoxFlat TransparentBox() => new() { BgColor = Colors.Transparent };

        // Faint green fill for a button's hover/pressed states.
        public static StyleBoxFlat HoverBox() => new()
        {
            BgColor                 = BtnHover,
            CornerRadiusTopLeft     = 4, CornerRadiusTopRight    = 4,
            CornerRadiusBottomLeft  = 4, CornerRadiusBottomRight = 4,
        };

        // Thin grey rule used between menu sections.
        public static HSeparator Separator()
        {
            var sep   = new HSeparator();
            sep.AddThemeStyleboxOverride("separator",
                new StyleBoxFlat { BgColor = new Color(0.25f, 0.25f, 0.25f) });
            return sep;
        }
    }
}
