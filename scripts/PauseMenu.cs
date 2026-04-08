using Godot;

namespace HoverTank
{
    // CanvasLayer overlay shown when the player presses Escape during a game.
    // Pauses the scene tree (ProcessMode.WhenPaused keeps this menu responsive)
    // and offers Resume, Quit to Menu, and Quit Game.
    //
    // Usage: add as child of any game-root node (GameSetup / SplitScreenManager).
    // The owner calls Show() on Escape; this node handles its own Hide() on Resume.
    public partial class PauseMenu : CanvasLayer
    {
        private static readonly Color ColGreen    = new(0.20f, 1.00f, 0.40f);
        private static readonly Color ColText     = new(0.85f, 0.85f, 0.85f);
        private static readonly Color ColBg       = new(0.00f, 0.00f, 0.00f, 0.82f);
        private static readonly Color ColBtnHover = new(0.20f, 1.00f, 0.40f, 0.12f);

        public override void _Ready()
        {
            Layer = 20; // Above HUD (layer 10) and split-screen viewports (layer -1/5)

            // Keep this node processing while the tree is paused.
            ProcessMode = ProcessModeEnum.WhenPaused;

            BuildUI();
            Hide();
        }

        // ── UI ───────────────────────────────────────────────────────────────

        private void BuildUI()
        {
            // Dim the whole screen
            var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.55f) };
            dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            AddChild(dim);

            // Centred panel
            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", PanelStyle());
            panel.AnchorLeft   = 0.5f; panel.OffsetLeft   = -140f;
            panel.AnchorTop    = 0.5f; panel.OffsetTop    = -110f;
            panel.AnchorRight  = 0.5f; panel.OffsetRight  =  140f;
            panel.AnchorBottom = 0.5f; panel.OffsetBottom =  110f;
            AddChild(panel);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 12);
            panel.AddChild(vbox);

            var title = new Label
            {
                Text                = "PAUSED",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            title.AddThemeColorOverride("font_color", ColGreen);
            title.AddThemeFontSizeOverride("font_size", 26);
            vbox.AddChild(title);

            vbox.AddChild(MakeSep());

            var btnResume = MakeBtn("RESUME");
            btnResume.Pressed += OnResume;
            vbox.AddChild(btnResume);

            var btnMenu = MakeBtn("QUIT TO MENU");
            btnMenu.Pressed += OnQuitToMenu;
            vbox.AddChild(btnMenu);

            var btnQuit = MakeBtn("QUIT GAME");
            btnQuit.Pressed += () => GetTree().Quit();
            vbox.AddChild(btnQuit);
        }

        // ── Input ─────────────────────────────────────────────────────────────

        public override void _Input(InputEvent evt)
        {
            if (!Visible) return;
            if (evt is InputEventKey key && key.Pressed && !key.Echo
                && key.PhysicalKeycode == Key.Escape)
            {
                OnResume();
                GetViewport().SetInputAsHandled();
            }
        }

        // ── Callbacks ─────────────────────────────────────────────────────────

        private void OnResume()
        {
            GetTree().Paused = false;
            Hide();
        }

        private void OnQuitToMenu()
        {
            GetTree().Paused = false;

            // Close any active network session before leaving the game scene.
            var nm = GetNodeOrNull<NetworkManager>("/root/NetworkManager");
            nm?.Disconnect();

            GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        }

        // ── Style helpers ─────────────────────────────────────────────────────

        private static StyleBoxFlat PanelStyle() => new()
        {
            BgColor                 = ColBg,
            CornerRadiusTopLeft     = 8,
            CornerRadiusTopRight    = 8,
            CornerRadiusBottomLeft  = 8,
            CornerRadiusBottomRight = 8,
            ContentMarginLeft       = 32,
            ContentMarginRight      = 32,
            ContentMarginTop        = 24,
            ContentMarginBottom     = 24,
        };

        private static Button MakeBtn(string text)
        {
            var btn = new Button { Text = text };
            btn.AddThemeColorOverride("font_color",         ColText);
            btn.AddThemeColorOverride("font_hover_color",   ColGreen);
            btn.AddThemeColorOverride("font_pressed_color", ColGreen);
            btn.AddThemeFontSizeOverride("font_size", 18);
            btn.AddThemeStyleboxOverride("normal",  TransparentBox());
            btn.AddThemeStyleboxOverride("hover",   HoverBox());
            btn.AddThemeStyleboxOverride("pressed", HoverBox());
            btn.AddThemeStyleboxOverride("focus",   TransparentBox());
            btn.CustomMinimumSize = new Vector2(0f, 38f);
            btn.Alignment         = HorizontalAlignment.Center;
            return btn;
        }

        private static HSeparator MakeSep()
        {
            var sep   = new HSeparator();
            var style = new StyleBoxFlat { BgColor = new Color(0.25f, 0.25f, 0.25f) };
            sep.AddThemeStyleboxOverride("separator", style);
            return sep;
        }

        private static StyleBoxFlat TransparentBox() =>
            new() { BgColor = Colors.Transparent };

        private static StyleBoxFlat HoverBox() => new()
        {
            BgColor                 = ColBtnHover,
            CornerRadiusTopLeft     = 4,
            CornerRadiusTopRight    = 4,
            CornerRadiusBottomLeft  = 4,
            CornerRadiusBottomRight = 4,
        };
    }
}
