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
                        ApplyDreamAtmosphere(); // TestDrive: swap to the pastel sandbox look
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
            }

            // Warm the key sun slightly and lower its angle for longer, raking
            // shadows that reveal the terrain's shape (flat noon light hides it).
            var sun = GetNodeOrNull<DirectionalLight3D>("Sun");
            if (sun != null)
            {
                sun.LightColor      = new Color(1.0f, 0.93f, 0.82f);
                sun.RotationDegrees = new Vector3(-22f, 38f, 0f);
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

        // ── TestDrive "dream" atmosphere ──────────────────────────────────────
        // Replaces the shared military-grey WorldEnvironment + Sun with the soft
        // pastel palette of the reference video, but ONLY for the TestDrive
        // sandbox — combat/multiplayer modes keep Main.tscn's environment. A
        // peach→lavender sky, warm low sun, and thick aerial-perspective fog that
        // fades distant swells to teal-grey, plus stronger additive bloom so the
        // terrain's emissive crests glow like neon ridgelines.
        //
        // DISABLED by default: as authored this environment washed the whole
        // screen out to white in TestDrive. Until the offending setting (most
        // likely the volumetric fog or the low-threshold additive glow) is
        // isolated and retuned, leave it off so TestDrive uses Main.tscn's plain
        // environment. Flip the export to iterate on it.
        [Export] public bool DreamAtmosphereEnabled = false;

        private void ApplyDreamAtmosphere()
        {
            if (!DreamAtmosphereEnabled) return;

            var we = GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
            if (we == null) return;

            var sky = new ProceduralSkyMaterial
            {
                SkyTopColor       = new Color(0.40f, 0.48f, 0.74f), // lavender-blue zenith
                SkyHorizonColor   = new Color(0.97f, 0.83f, 0.78f), // peach horizon
                SkyCurve          = 0.12f,
                GroundHorizonColor = new Color(0.95f, 0.80f, 0.76f),
                GroundBottomColor  = new Color(0.55f, 0.50f, 0.58f),
                SunAngleMax       = 30f,
                SunCurve          = 0.08f,
            };

            var env = new Godot.Environment
            {
                BackgroundMode      = Godot.Environment.BGMode.Sky,
                Sky                 = new Sky { SkyMaterial = sky },
                AmbientLightSource  = Godot.Environment.AmbientSource.Sky,
                AmbientLightSkyContribution = 0.6f,
                AmbientLightColor   = new Color(0.70f, 0.74f, 0.86f),
                AmbientLightEnergy  = 1.0f,

                // ACES gives a filmic, cinematic highlight rolloff — well suited
                // to this emissive/bloom-heavy HDR look. (AgX isn't exposed in the
                // 4.3 C# binding's ToneMapper enum.)
                TonemapMode         = Godot.Environment.ToneMapper.Aces,
                TonemapExposure     = 1.05f,

                // Soft additive bloom makes the teal crests read as glowing light.
                // Kept restrained: only genuine HDR highlights bloom, so the screen
                // doesn't wash out (threshold high, intensity/bloom low).
                GlowEnabled         = true,
                GlowIntensity       = 0.25f,
                GlowBloom           = 0.04f,
                GlowBlendMode       = Godot.Environment.GlowBlendModeEnum.Additive,
                GlowHdrThreshold    = 1.1f,

                // Aerial-perspective + height fog: distant swells fade to a teal
                // haze, and a layer of mist pools in the troughs between crests.
                FogEnabled          = true,
                FogMode             = Godot.Environment.FogModeEnum.Exponential,
                FogLightColor       = new Color(0.64f, 0.76f, 0.82f),
                FogLightEnergy      = 0.9f,
                FogSunScatter       = 0.2f,   // slight glow toward the low sun
                FogDensity          = 0.012f,
                FogAerialPerspective = 0.9f,
                FogSkyAffect        = 0.35f,
                FogHeight           = 6f,     // mist sits below this world height…
                FogHeightDensity    = 0.18f,  // …and thickens toward the valleys

                // Volumetric fog: the low sun rakes through the haze and the
                // emissive crests inject a faint teal glow into the air. Forward+
                // only; ignored on other renderers.
                VolumetricFogEnabled      = true,
                VolumetricFogDensity      = 0.018f,
                VolumetricFogAlbedo       = new Color(0.72f, 0.80f, 0.86f),
                VolumetricFogEmission     = new Color(0.10f, 0.32f, 0.40f),
                VolumetricFogEmissionEnergy = 0.4f,
                VolumetricFogGIInject     = 0.6f,
                VolumetricFogAnisotropy   = 0.4f,
                VolumetricFogLength       = 180f,
                VolumetricFogSkyAffect    = 0.3f,

                // Subtle colour grade: a touch more contrast/saturation plus a
                // split-tone LUT (teal shadows, warm highlights) for the dreamy
                // cinematic feel. Generated in code — no baked asset.
                AdjustmentEnabled        = true,
                AdjustmentContrast       = 1.06f,
                AdjustmentSaturation     = 1.10f,
                AdjustmentBrightness     = 1.0f,
                AdjustmentColorCorrection = MakeSplitToneLut(),
            };
            we.Environment = env;

            var sun = GetNodeOrNull<DirectionalLight3D>("Sun");
            if (sun != null)
            {
                sun.LightColor           = new Color(1.00f, 0.84f, 0.70f); // warm golden-hour
                sun.LightEnergy          = 1.0f;
                sun.RotationDegrees      = new Vector3(-16f, 42f, 0f);     // low, raking light
                sun.ShadowEnabled        = true;
                sun.LightAngularDistance = 1.2f;   // wider penumbra → softer shadows
                sun.ShadowOpacity        = 0.85f;  // let a little light into shadow
            }

            // Cool sky-fill from the opposite side fakes skylight bounce so
            // shadows read blue against the warm key — the classic golden-hour
            // contrast. Dim and shadowless so it only lifts the ambient tone.
            var fill = new DirectionalLight3D
            {
                Name            = "SkyFill",
                LightColor      = new Color(0.55f, 0.68f, 0.95f),
                LightEnergy     = 0.35f,
                ShadowEnabled   = false,
                RotationDegrees = new Vector3(-30f, -140f, 0f),
            };
            AddChild(fill);
        }

        // Builds a 256-px 1D LUT for Environment colour correction: a gentle
        // split-tone that pushes shadows teal and highlights warm. Asset-free,
        // matching the project's runtime-generation convention.
        private static GradientTexture1D MakeSplitToneLut()
        {
            var grad = new Gradient();
            grad.SetColor(0, new Color(0.86f, 0.97f, 1.02f)); // shadow tint → teal/cool
            grad.SetColor(1, new Color(1.04f, 0.98f, 0.88f)); // highlight tint → warm
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
