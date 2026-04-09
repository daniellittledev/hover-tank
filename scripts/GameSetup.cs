using Godot;

namespace HoverTank
{
    // Attached to the root node of Main.tscn.
    // Bridges the menu → game transition: tells NetworkManager which node to use
    // as the tanks container, then starts the appropriate game mode from GameState.
    //
    // For NetworkJoin mode it also shows a "Connecting…" overlay while the ENet
    // handshake is in progress. If the connection fails the player sees an error
    // message with a "Back to Menu" button instead of a silent black screen.
    public partial class GameSetup : Node
    {
        private PauseMenu _pauseMenu      = null!;
        private Control?  _connectOverlay;

        public override void _Ready()
        {
            var nm = GetNode<NetworkManager>("/root/NetworkManager");
            nm.Initialize(GetNode<Node3D>("Tanks"));

            switch (GameState.Instance.Mode)
            {
                case GameMode.SinglePlayer:
                    nm.StartSinglePlayer();
                    var waveManager = new WaveManager { Name = "WaveManager" };
                    AddChild(waveManager);
                    break;

                case GameMode.NetworkHost:
                    nm.StartHost();
                    break;

                case GameMode.NetworkJoin:
                    ShowConnectingOverlay(GameState.Instance.JoinAddress);
                    nm.ConnectedToServer += OnConnectedToServer;
                    nm.ConnectionFailed  += OnConnectionFailed;
                    nm.StartClient(GameState.Instance.JoinAddress);
                    break;

                case GameMode.SplitScreen:
                    nm.StartSinglePlayer();
                    break;
            }

            _pauseMenu = new PauseMenu();
            AddChild(_pauseMenu);
        }

        // ── Escape / pause ────────────────────────────────────────────────────

        public override void _Input(InputEvent evt)
        {
            if (evt is InputEventKey key && key.Pressed && !key.Echo
                && key.PhysicalKeycode == Key.Escape)
            {
                // Don't allow pause while the connection overlay is visible.
                if (_connectOverlay != null && _connectOverlay.Visible) return;

                TogglePause();
                GetViewport().SetInputAsHandled();
            }
        }

        private void TogglePause()
        {
            bool pausing = !GetTree().Paused;
            GetTree().Paused = pausing;

            if (pausing)
                _pauseMenu.Show();
            else
                _pauseMenu.Hide();
        }

        // ── Connecting overlay ────────────────────────────────────────────────

        private void ShowConnectingOverlay(string address)
        {
            var layer = new CanvasLayer { Layer = 15 };
            AddChild(layer);

            var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.75f) };
            dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(dim);

            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", PanelStyle());
            panel.AnchorLeft   = 0.5f; panel.OffsetLeft   = -180f;
            panel.AnchorTop    = 0.5f; panel.OffsetTop    = -80f;
            panel.AnchorRight  = 0.5f; panel.OffsetRight  =  180f;
            panel.AnchorBottom = 0.5f; panel.OffsetBottom =  80f;
            layer.AddChild(panel);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 14);
            panel.AddChild(vbox);

            var titleLbl = new Label
            {
                Text                = "CONNECTING",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            titleLbl.AddThemeColorOverride("font_color", new Color(0.20f, 1.00f, 0.40f));
            titleLbl.AddThemeFontSizeOverride("font_size", 22);
            vbox.AddChild(titleLbl);

            var addrLbl = new Label
            {
                Text                = address + ":7777",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            addrLbl.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
            addrLbl.AddThemeFontSizeOverride("font_size", 14);
            vbox.AddChild(addrLbl);

            var cancelBtn = new Button { Text = "CANCEL" };
            cancelBtn.AddThemeColorOverride("font_color",       new Color(0.85f, 0.85f, 0.85f));
            cancelBtn.AddThemeColorOverride("font_hover_color", new Color(0.20f, 1.00f, 0.40f));
            cancelBtn.AddThemeFontSizeOverride("font_size", 16);
            cancelBtn.AddThemeStyleboxOverride("normal",  TransparentBox());
            cancelBtn.AddThemeStyleboxOverride("hover",   HoverBox());
            cancelBtn.AddThemeStyleboxOverride("pressed", HoverBox());
            cancelBtn.AddThemeStyleboxOverride("focus",   TransparentBox());
            cancelBtn.Pressed += BackToMenu;
            vbox.AddChild(cancelBtn);

            _connectOverlay = layer;
        }

        private void OnConnectedToServer()
        {
            // Tear down the overlay — we're in; let the HUD take over.
            _connectOverlay?.QueueFree();
            _connectOverlay = null;
        }

        private void OnConnectionFailed()
        {
            if (_connectOverlay == null) return;

            // Replace "Connecting…" content with an error message + back button.
            _connectOverlay.QueueFree();
            _connectOverlay = null;

            var layer = new CanvasLayer { Layer = 15 };
            AddChild(layer);

            var dim = new ColorRect { Color = new Color(0f, 0f, 0f, 0.75f) };
            dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            layer.AddChild(dim);

            var panel = new PanelContainer();
            panel.AddThemeStyleboxOverride("panel", PanelStyle());
            panel.AnchorLeft   = 0.5f; panel.OffsetLeft   = -180f;
            panel.AnchorTop    = 0.5f; panel.OffsetTop    = -80f;
            panel.AnchorRight  = 0.5f; panel.OffsetRight  =  180f;
            panel.AnchorBottom = 0.5f; panel.OffsetBottom =  80f;
            layer.AddChild(panel);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 14);
            panel.AddChild(vbox);

            var errTitle = new Label
            {
                Text                = "CONNECTION FAILED",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            errTitle.AddThemeColorOverride("font_color", new Color(0.90f, 0.20f, 0.20f));
            errTitle.AddThemeFontSizeOverride("font_size", 20);
            vbox.AddChild(errTitle);

            var errMsg = new Label
            {
                Text                = $"Could not reach {GameState.Instance.JoinAddress}",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            errMsg.AddThemeColorOverride("font_color", new Color(0.55f, 0.55f, 0.55f));
            errMsg.AddThemeFontSizeOverride("font_size", 14);
            vbox.AddChild(errMsg);

            var backBtn = new Button { Text = "BACK TO MENU" };
            backBtn.AddThemeColorOverride("font_color",       new Color(0.85f, 0.85f, 0.85f));
            backBtn.AddThemeColorOverride("font_hover_color", new Color(0.20f, 1.00f, 0.40f));
            backBtn.AddThemeFontSizeOverride("font_size", 16);
            backBtn.AddThemeStyleboxOverride("normal",  TransparentBox());
            backBtn.AddThemeStyleboxOverride("hover",   HoverBox());
            backBtn.AddThemeStyleboxOverride("pressed", HoverBox());
            backBtn.AddThemeStyleboxOverride("focus",   TransparentBox());
            backBtn.Pressed += BackToMenu;
            vbox.AddChild(backBtn);

            _connectOverlay = layer;
        }

        private void BackToMenu()
        {
            var nm = GetNode<NetworkManager>("/root/NetworkManager");
            nm.Disconnect();
            GetTree().ChangeSceneToFile("res://scenes/MainMenu.tscn");
        }

        // ── Style helpers (duplicated from MainMenu/PauseMenu to keep each
        //    script self-contained — only 3 methods, not worth extracting) ────

        private static StyleBoxFlat PanelStyle() => new()
        {
            BgColor                 = new Color(0f, 0f, 0f, 0.82f),
            CornerRadiusTopLeft     = 8, CornerRadiusTopRight    = 8,
            CornerRadiusBottomLeft  = 8, CornerRadiusBottomRight = 8,
            ContentMarginLeft = 32, ContentMarginRight  = 32,
            ContentMarginTop  = 24, ContentMarginBottom = 24,
        };

        private static StyleBoxFlat TransparentBox() =>
            new() { BgColor = Colors.Transparent };

        private static StyleBoxFlat HoverBox() => new()
        {
            BgColor                 = new Color(0.20f, 1.00f, 0.40f, 0.12f),
            CornerRadiusTopLeft     = 4, CornerRadiusTopRight    = 4,
            CornerRadiusBottomLeft  = 4, CornerRadiusBottomRight = 4,
        };
    }
}
