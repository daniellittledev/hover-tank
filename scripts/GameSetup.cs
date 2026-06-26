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
        private CanvasLayer? _connectOverlay;

        public override void _Ready()
        {
            var nm = GetNode<NetworkManager>("/root/NetworkManager");
            nm.Initialize(GetNode<Node3D>("Tanks"));

            // Tasteful baseline look for every Main.tscn mode (combat/MP/etc).
            // TestDrive may later replace the whole environment via the dream
            // atmosphere, but that's gated off for now, so this applies there too.
            ApplyBaselineVisuals();

            var mode = GameState.Instance.Mode;
            switch (mode)
            {
                case GameMode.SinglePlayer:
                    nm.StartSinglePlayer();
                    // TestDrive = empty sandbox; WaveManager also owns ally spawns,
                    // so skipping it leaves just the player tank on the map.
                    if (GameState.Instance.SinglePlayerMode == SinglePlayerMode.StandardWaves)
                        AddChild(new WaveManager { Name = "WaveManager" });
                    else
                        ApplyDuskAtmosphere(); // TestDrive: pastel sunset + teal-haze sandbox
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

            // Offline modes need something to maintain the projectile spatial
            // grid; in networked host mode ServerSimulation handles it.
            if (mode is GameMode.SinglePlayer or GameMode.SplitScreen)
                AddChild(new OfflineSimulation { Name = "OfflineSimulation" });

            _pauseMenu = new PauseMenu();
            AddChild(_pauseMenu);
        }

        // ── Baseline visuals (all modes) ──────────────────────────────────────
        // Low-risk, asset-free polish applied on top of Main.tscn's environment:
        //  • a gentle split-tone colour grade (teal shadows / warm highlights)
        //    plus a touch more contrast and saturation, so the image reads as
        //    deliberately graded rather than flat;
        //  • a cool, shadowless fill light from behind-opposite the sun, which
        //    fakes skylight bounce and separates the tank silhouette from the
        //    terrain — the single biggest readability win for a one-sun scene.
        // All generated in code to match the project's no-baked-assets convention.
        private void ApplyBaselineVisuals()
        {
            var we = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
            if (we?.Environment is Godot.Environment env)
            {
                env.AdjustmentEnabled         = true;
                env.AdjustmentContrast        = 1.06f;
                env.AdjustmentSaturation      = 1.10f;
                env.AdjustmentBrightness      = 1.0f;
                env.AdjustmentColorCorrection = MakeSplitToneLut();

                // A touch more atmospheric depth: slightly denser distance fog
                // plus a shallow ground mist that pools in the craters. Kept
                // deliberately subtle — overdone fog hazes the scene out to white.
                env.FogEnabled           = true;
                env.FogDensity           = 0.005f;
                env.FogAerialPerspective = 0.7f;
                env.FogHeight            = 2.0f;
                env.FogHeightDensity     = 0.04f;
            }

            // Warm the key sun slightly and lower its angle for longer, raking
            // shadows that reveal the terrain's shape (flat noon light hides it).
            // A wider angular size softens the shadow penumbra with distance —
            // soft contact-hardening shadows rather than a hard uniform edge.
            var sun = GetNodeOrNull<DirectionalLight3D>("Sun");
            if (sun != null)
            {
                sun.LightColor           = new Color(1.0f, 0.93f, 0.82f);
                sun.RotationDegrees      = new Vector3(-22f, 38f, 0f);
                sun.LightAngularDistance = 0.6f;
            }

            // Cool fill from the opposite side: dim, shadowless skylight bounce so
            // shadows read blue against the warm key — classic key/fill contrast.
            AddChild(new DirectionalLight3D
            {
                Name            = "SkyFill",
                LightColor      = new Color(0.55f, 0.68f, 0.95f),
                LightEnergy     = 0.35f,
                ShadowEnabled   = false,
                RotationDegrees = new Vector3(-28f, -140f, 0f),
            });
        }

        // ── TestDrive dusk atmosphere ─────────────────────────────────────────
        // Soft, atmospheric sandbox look (per reference): a pastel sunset sky —
        // lavender zenith fading to a warm orange horizon — with distant dunes
        // fading into a teal-blue haze via aerial fog. Bright and airy, not the
        // dark neon of the earlier Tron attempt. Combat/MP keep Main.tscn's env.
        private void ApplyDuskAtmosphere()
        {
            var we = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
            if (we == null) return;

            var sky = new ProceduralSkyMaterial
            {
                SkyTopColor        = new Color(0.52f, 0.55f, 0.72f), // soft lavender zenith
                SkyHorizonColor    = new Color(0.98f, 0.80f, 0.66f), // warm sunset orange
                SkyCurve           = 0.09f,
                GroundHorizonColor = new Color(0.70f, 0.66f, 0.64f),
                GroundBottomColor  = new Color(0.22f, 0.26f, 0.32f),
            };

            we.Environment = new Godot.Environment
            {
                BackgroundMode      = Godot.Environment.BGMode.Sky,
                Sky                 = new Sky { SkyMaterial = sky },
                AmbientLightSource  = Godot.Environment.AmbientSource.Sky,
                AmbientLightSkyContribution = 0.8f,
                AmbientLightEnergy  = 1.0f,

                TonemapMode         = Godot.Environment.ToneMapper.Aces,
                TonemapExposure     = 1.0f,

                // Gentle bloom — just lifts the craft underglow and sky highlights.
                GlowEnabled         = true,
                GlowIntensity       = 0.3f,
                GlowBloom           = 0.05f,
                GlowBlendMode       = Godot.Environment.GlowBlendModeEnum.Additive,
                GlowHdrThreshold    = 1.0f,

                // Teal-blue aerial fog: distant dunes fade to a teal haze while the
                // sky keeps its sunset colour — low aerial-perspective so the fog
                // stays teal instead of taking the orange sky tint.
                FogEnabled           = true,
                FogMode              = Godot.Environment.FogModeEnum.Exponential,
                FogLightColor        = new Color(0.40f, 0.60f, 0.70f), // teal-blue
                FogLightEnergy       = 1.0f,
                FogDensity           = 0.010f,
                FogAerialPerspective = 0.15f,
                FogSkyAffect         = 0.0f,

                // Subtle grade: a little contrast/saturation + cool-shadow/warm-
                // highlight tone for cohesion. Endpoints span ~black..white.
                AdjustmentEnabled         = true,
                AdjustmentContrast        = 1.05f,
                AdjustmentSaturation      = 1.10f,
                AdjustmentBrightness      = 1.0f,
                AdjustmentColorCorrection = MakeSplitToneLut(),
            };

            // Warm, low sunset key for soft directional shading on the dunes.
            var sun = GetNodeOrNull<DirectionalLight3D>("Sun");
            if (sun != null)
            {
                sun.LightColor           = new Color(1.0f, 0.86f, 0.72f);
                sun.LightEnergy          = 1.0f;
                sun.RotationDegrees      = new Vector3(-12f, 40f, 0f);
                sun.ShadowEnabled        = true;
                sun.LightAngularDistance = 1.0f;
            }
        }

        // Builds a 256-px 1D LUT for Environment colour correction: a near-identity
        // tone curve with a gentle split-tone — cool, near-black shadows and warm
        // highlights. The endpoints MUST span ~black..~white: a curve like
        // 0.86..1.04 maps black up to ~0.9 and washes the whole image to near-white
        // (this was the cause of the full-screen whiteout). Asset-free.
        private static GradientTexture1D MakeSplitToneLut()
        {
            var grad = new Gradient();
            grad.SetColor(0, new Color(0.02f, 0.03f, 0.05f)); // shadows: cool, near-black
            grad.SetColor(1, new Color(1.00f, 0.97f, 0.90f)); // highlights: warm white
            return new GradientTexture1D { Gradient = grad, Width = 256 };
        }

        // ── Escape / pause ────────────────────────────────────────────────────
        // Use _UnhandledInput so UnitCommander can consume Escape to deselect
        // units without also triggering the pause menu.

        public override void _UnhandledInput(InputEvent evt)
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
