using Godot;

namespace HoverTank
{
	// Root of MainMenu.tscn. Builds the entire menu UI in code (consistent with
	// HUD.cs). Manages five panels that are shown/hidden as the user navigates:
	//
	//   _mainPanel       – top-level: Single Player / Multiplayer / Settings / Quit
	//   _multiPanel      – Multiplayer: Network / Split Screen / Back
	//   _networkPanel    – Network: Host / Join / Back
	//   _joinPanel       – Join: IP input + Connect / Back
	//   _settingsPanel   – Settings: resolution + window mode + Apply / Back
	public partial class MainMenu : CanvasLayer
	{
		// ── Colours matching HUD style ───────────────────────────────────────
		private static readonly Color ColGreen     = new(0.20f, 1.00f, 0.40f);
		private static readonly Color ColDim       = new(0.50f, 0.50f, 0.50f);
		private static readonly Color ColText      = new(0.85f, 0.85f, 0.85f);
		private static readonly Color ColBg        = new(0.00f, 0.00f, 0.00f, 0.72f);
		private static readonly Color ColBtnHover  = new(0.20f, 1.00f, 0.40f, 0.12f);

		// ── Panels ───────────────────────────────────────────────────────────
		private Control _mainPanel    = null!;
		private Control _multiPanel   = null!;
		private Control _networkPanel = null!;
		private Control _joinPanel    = null!;
		private Control _settingsPanel= null!;

		private LineEdit _ipField = null!;

		// Settings state
		private static readonly Vector2I[] Resolutions =
		{
			new(1280, 720), new(1920, 1080), new(2560, 1440), new(3840, 2160),
		};
		private int _selectedRes = 0;
		private DisplayServer.WindowMode _windowMode = DisplayServer.WindowMode.Windowed;

		// ── Lifecycle ────────────────────────────────────────────────────────

		public override void _Ready()
		{
			Layer = 0;

			var root = new Control();
			root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			root.MouseFilter = Control.MouseFilterEnum.Ignore;
			AddChild(root);

			AddBackground(root);
			AddTitle(root);

			_mainPanel     = BuildMainPanel(root);
			_multiPanel    = BuildMultiPanel(root);
			_networkPanel  = BuildNetworkPanel(root);
			_joinPanel     = BuildJoinPanel(root);
			_settingsPanel = BuildSettingsPanel(root);

			ShowPanel(_mainPanel);
		}

		public override void _Input(InputEvent evt)
		{
			if (evt is InputEventKey key && key.Pressed && !key.Echo
				&& key.PhysicalKeycode == Key.Escape)
			{
				// Navigate back from sub-panels on Escape.
				if (_joinPanel.Visible)    { ShowPanel(_networkPanel); return; }
				if (_networkPanel.Visible) { ShowPanel(_multiPanel);   return; }
				if (_multiPanel.Visible)   { ShowPanel(_mainPanel);    return; }
				if (_settingsPanel.Visible){ ShowPanel(_mainPanel);    return; }
			}
		}

		// ── Background ───────────────────────────────────────────────────────

		private static void AddBackground(Control root)
		{
			var bg = new ColorRect
			{
				Color = new Color(0.02f, 0.02f, 0.04f, 1f),
			};
			bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			root.AddChild(bg);

			// Subtle scanline feel: a semi-transparent dark overlay panel
			var overlay = new ColorRect
			{
				Color = new Color(0f, 0f, 0f, 0.25f),
			};
			overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
			root.AddChild(overlay);
		}

		private static void AddTitle(Control root)
		{
			var title = new Label
			{
				Text                = "HOVER TANK",
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			title.AddThemeColorOverride("font_color", ColGreen);
			title.AddThemeFontSizeOverride("font_size", 52);
			// Anchored to top-centre
			title.AnchorLeft   = 0f; title.OffsetLeft   = 0f;
			title.AnchorTop    = 0f; title.OffsetTop    = 60f;
			title.AnchorRight  = 1f; title.OffsetRight  = 0f;
			title.AnchorBottom = 0f; title.OffsetBottom = 120f;
			root.AddChild(title);

			var sub = new Label
			{
				Text                = "BATTLEZONE EDITION",
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			sub.AddThemeColorOverride("font_color", ColDim);
			sub.AddThemeFontSizeOverride("font_size", 16);
			sub.AnchorLeft   = 0f; sub.OffsetLeft   = 0f;
			sub.AnchorTop    = 0f; sub.OffsetTop    = 120f;
			sub.AnchorRight  = 1f; sub.OffsetRight  = 0f;
			sub.AnchorBottom = 0f; sub.OffsetBottom = 150f;
			root.AddChild(sub);
		}

		// ── Panel helpers ────────────────────────────────────────────────────

		private void ShowPanel(Control panel)
		{
			_mainPanel.Visible     = panel == _mainPanel;
			_multiPanel.Visible    = panel == _multiPanel;
			_networkPanel.Visible  = panel == _networkPanel;
			_joinPanel.Visible     = panel == _joinPanel;
			_settingsPanel.Visible = panel == _settingsPanel;
		}

		// Creates a centred VBox panel with a styled background.
		private static VBoxContainer MakeCentredPanel(Control root, float width = 340f)
		{
			var panel = new PanelContainer();
			panel.AddThemeStyleboxOverride("panel", PanelStyle());
			panel.AnchorLeft   = 0.5f; panel.OffsetLeft   = -width / 2f;
			panel.AnchorTop    = 0.5f; panel.OffsetTop    = -10f;
			panel.AnchorRight  = 0.5f; panel.OffsetRight  =  width / 2f;
			panel.AnchorBottom = 0.5f; panel.OffsetBottom =  400f;
			panel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
			root.AddChild(panel);

			var vbox = new VBoxContainer();
			vbox.AddThemeConstantOverride("separation", 14);
			panel.AddChild(vbox);
			return vbox;
		}

		private static StyleBoxFlat PanelStyle() => new()
		{
			BgColor                 = ColBg,
			CornerRadiusTopLeft     = 8,
			CornerRadiusTopRight    = 8,
			CornerRadiusBottomLeft  = 8,
			CornerRadiusBottomRight = 8,
			ContentMarginLeft       = 36,
			ContentMarginRight      = 36,
			ContentMarginTop        = 28,
			ContentMarginBottom     = 28,
		};

		// Creates a menu button with hover highlight.
		private static Button MakeBtn(string text)
		{
			var btn = new Button { Text = text };
			btn.AddThemeColorOverride("font_color",         ColText);
			btn.AddThemeColorOverride("font_hover_color",   ColGreen);
			btn.AddThemeColorOverride("font_pressed_color", ColGreen);
			btn.AddThemeFontSizeOverride("font_size", 20);
			btn.AddThemeStyleboxOverride("normal",   TransparentBox());
			btn.AddThemeStyleboxOverride("hover",    HoverBox());
			btn.AddThemeStyleboxOverride("pressed",  HoverBox());
			btn.AddThemeStyleboxOverride("focus",    TransparentBox());
			btn.CustomMinimumSize = new Vector2(0f, 42f);
			btn.Alignment         = HorizontalAlignment.Center;
			return btn;
		}

		private static StyleBoxFlat TransparentBox() => new() { BgColor = Colors.Transparent };

		private static StyleBoxFlat HoverBox() => new()
		{
			BgColor                 = ColBtnHover,
			CornerRadiusTopLeft     = 4,
			CornerRadiusTopRight    = 4,
			CornerRadiusBottomLeft  = 4,
			CornerRadiusBottomRight = 4,
		};

		private static Label MakeSectionLabel(string text)
		{
			var lbl = new Label { Text = text, HorizontalAlignment = HorizontalAlignment.Center };
			lbl.AddThemeColorOverride("font_color", ColGreen);
			lbl.AddThemeFontSizeOverride("font_size", 14);
			return lbl;
		}

		private static HSeparator MakeSep()
		{
			var sep = new HSeparator();
			var style = new StyleBoxFlat { BgColor = new Color(0.25f, 0.25f, 0.25f) };
			sep.AddThemeStyleboxOverride("separator", style);
			return sep;
		}

		// ── Main panel ───────────────────────────────────────────────────────

		private Control BuildMainPanel(Control root)
		{
			var vbox = MakeCentredPanel(root);

			vbox.AddChild(MakeSectionLabel("MISSION SELECT"));
			vbox.AddChild(MakeSep());

			var btnSP = MakeBtn("SINGLE PLAYER");
			btnSP.Pressed += OnSinglePlayer;
			vbox.AddChild(btnSP);

			var btnMP = MakeBtn("MULTIPLAYER  >");
			btnMP.Pressed += () => ShowPanel(_multiPanel);
			vbox.AddChild(btnMP);

			vbox.AddChild(MakeSep());

			var btnSettings = MakeBtn("SETTINGS");
			btnSettings.Pressed += () => ShowPanel(_settingsPanel);
			vbox.AddChild(btnSettings);

			var btnQuit = MakeBtn("QUIT");
			btnQuit.Pressed += () => GetTree().Quit();
			vbox.AddChild(btnQuit);

			return (Control)vbox.GetParent();
		}

		// ── Multiplayer panel ─────────────────────────────────────────────────

		private Control BuildMultiPanel(Control root)
		{
			var vbox = MakeCentredPanel(root);

			vbox.AddChild(MakeSectionLabel("MULTIPLAYER"));
			vbox.AddChild(MakeSep());

			var btnNet = MakeBtn("NETWORK  >");
			btnNet.Pressed += () => ShowPanel(_networkPanel);
			vbox.AddChild(btnNet);

			var btnSS = MakeBtn("SPLIT SCREEN");
			btnSS.Pressed += OnSplitScreen;
			vbox.AddChild(btnSS);

			vbox.AddChild(MakeSep());

			var btnBack = MakeBtn("< BACK");
			btnBack.Pressed += () => ShowPanel(_mainPanel);
			vbox.AddChild(btnBack);

			return (Control)vbox.GetParent();
		}

		// ── Network panel ─────────────────────────────────────────────────────

		private Control BuildNetworkPanel(Control root)
		{
			var vbox = MakeCentredPanel(root);

			vbox.AddChild(MakeSectionLabel("NETWORK PLAY"));
			vbox.AddChild(MakeSep());

			var btnHost = MakeBtn("HOST GAME");
			btnHost.Pressed += OnHostGame;
			vbox.AddChild(btnHost);

			var btnJoin = MakeBtn("JOIN GAME  >");
			btnJoin.Pressed += () => ShowPanel(_joinPanel);
			vbox.AddChild(btnJoin);

			vbox.AddChild(MakeSep());

			var infoLbl = new Label
			{
				Text                = $"Default port: 7777",
				HorizontalAlignment = HorizontalAlignment.Center,
			};
			infoLbl.AddThemeColorOverride("font_color", ColDim);
			infoLbl.AddThemeFontSizeOverride("font_size", 13);
			vbox.AddChild(infoLbl);

			var btnBack = MakeBtn("< BACK");
			btnBack.Pressed += () => ShowPanel(_multiPanel);
			vbox.AddChild(btnBack);

			return (Control)vbox.GetParent();
		}

		// ── Join panel ────────────────────────────────────────────────────────

		private Control BuildJoinPanel(Control root)
		{
			var vbox = MakeCentredPanel(root);

			vbox.AddChild(MakeSectionLabel("JOIN GAME"));
			vbox.AddChild(MakeSep());

			var ipLabel = new Label { Text = "SERVER IP ADDRESS" };
			ipLabel.AddThemeColorOverride("font_color", ColDim);
			ipLabel.AddThemeFontSizeOverride("font_size", 13);
			vbox.AddChild(ipLabel);

			_ipField = new LineEdit
			{
				Text              = "127.0.0.1",
				PlaceholderText   = "e.g. 192.168.1.100",
				CustomMinimumSize = new Vector2(0f, 38f),
			};
			_ipField.AddThemeColorOverride("font_color",             ColText);
			_ipField.AddThemeColorOverride("font_placeholder_color", ColDim);
			_ipField.AddThemeFontSizeOverride("font_size", 18);
			StyleLineEdit(_ipField);
			vbox.AddChild(_ipField);

			vbox.AddChild(MakeSep());

			var btnConnect = MakeBtn("CONNECT");
			btnConnect.Pressed += OnJoinGame;
			vbox.AddChild(btnConnect);

			var btnBack = MakeBtn("< BACK");
			btnBack.Pressed += () => ShowPanel(_networkPanel);
			vbox.AddChild(btnBack);

			return (Control)vbox.GetParent();
		}

		private static void StyleLineEdit(LineEdit field)
		{
			var normal = new StyleBoxFlat
			{
				BgColor                 = new Color(0.08f, 0.08f, 0.10f, 0.9f),
				BorderColor             = new Color(0.25f, 0.25f, 0.25f),
				BorderWidthBottom       = 1,
				BorderWidthTop          = 1,
				BorderWidthLeft         = 1,
				BorderWidthRight        = 1,
				CornerRadiusTopLeft     = 4,
				CornerRadiusTopRight    = 4,
				CornerRadiusBottomLeft  = 4,
				CornerRadiusBottomRight = 4,
				ContentMarginLeft       = 10,
				ContentMarginRight      = 10,
				ContentMarginTop        = 6,
				ContentMarginBottom     = 6,
			};
			var focus = new StyleBoxFlat
			{
				BgColor                 = new Color(0.08f, 0.08f, 0.10f, 0.9f),
				BorderColor             = ColGreen,
				BorderWidthBottom       = 1,
				BorderWidthTop          = 1,
				BorderWidthLeft         = 1,
				BorderWidthRight        = 1,
				CornerRadiusTopLeft     = 4,
				CornerRadiusTopRight    = 4,
				CornerRadiusBottomLeft  = 4,
				CornerRadiusBottomRight = 4,
				ContentMarginLeft       = 10,
				ContentMarginRight      = 10,
				ContentMarginTop        = 6,
				ContentMarginBottom     = 6,
			};
			field.AddThemeStyleboxOverride("normal", normal);
			field.AddThemeStyleboxOverride("focus",  focus);
			field.AddThemeStyleboxOverride("read_only", normal);
		}

		// ── Settings panel ───────────────────────────────────────────────────

		private OptionButton _resOption   = null!;
		private OptionButton _modeOption  = null!;

		private Control BuildSettingsPanel(Control root)
		{
			var vbox = MakeCentredPanel(root, 380f);

			vbox.AddChild(MakeSectionLabel("SETTINGS"));
			vbox.AddChild(MakeSep());

			// Resolution
			var resLabel = new Label { Text = "RESOLUTION" };
			resLabel.AddThemeColorOverride("font_color", ColDim);
			resLabel.AddThemeFontSizeOverride("font_size", 13);
			vbox.AddChild(resLabel);

			_resOption = new OptionButton { CustomMinimumSize = new Vector2(0f, 38f) };
			_resOption.AddThemeFontSizeOverride("font_size", 16);
			foreach (var r in Resolutions)
				_resOption.AddItem($"{r.X} × {r.Y}");
			_resOption.Selected = _selectedRes;
			_resOption.ItemSelected += idx => _selectedRes = (int)idx;
			vbox.AddChild(_resOption);

			// Window mode
			var modeLabel = new Label { Text = "WINDOW MODE" };
			modeLabel.AddThemeColorOverride("font_color", ColDim);
			modeLabel.AddThemeFontSizeOverride("font_size", 13);
			vbox.AddChild(modeLabel);

			_modeOption = new OptionButton { CustomMinimumSize = new Vector2(0f, 38f) };
			_modeOption.AddThemeFontSizeOverride("font_size", 16);
			_modeOption.AddItem("Windowed");
			_modeOption.AddItem("Fullscreen");
			_modeOption.AddItem("Borderless");
			_modeOption.Selected = 0;
			_modeOption.ItemSelected += idx => _windowMode = idx switch
			{
				1 => DisplayServer.WindowMode.ExclusiveFullscreen,
				2 => DisplayServer.WindowMode.Fullscreen,
				_ => DisplayServer.WindowMode.Windowed,
			};
			vbox.AddChild(_modeOption);

			vbox.AddChild(MakeSep());

			var btnApply = MakeBtn("APPLY");
			btnApply.Pressed += OnApplySettings;
			vbox.AddChild(btnApply);

			var btnBack = MakeBtn("< BACK");
			btnBack.Pressed += () => ShowPanel(_mainPanel);
			vbox.AddChild(btnBack);

			return (Control)vbox.GetParent();
		}

		// ── Button callbacks ─────────────────────────────────────────────────

		private void OnSinglePlayer()
		{
			GameState.Instance.Mode = GameMode.SinglePlayer;
			GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
		}

		private void OnHostGame()
		{
			GameState.Instance.Mode = GameMode.NetworkHost;
			GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
		}

		private void OnJoinGame()
		{
			string ip = _ipField.Text.Trim();
			if (string.IsNullOrEmpty(ip)) ip = "127.0.0.1";
			GameState.Instance.Mode        = GameMode.NetworkJoin;
			GameState.Instance.JoinAddress = ip;
			GetTree().ChangeSceneToFile("res://scenes/Main.tscn");
		}

		private void OnSplitScreen()
		{
			GameState.Instance.Mode = GameMode.SplitScreen;
			GetTree().ChangeSceneToFile("res://scenes/SplitScreen.tscn");
		}

		private void OnApplySettings()
		{
			var res = Resolutions[_selectedRes];

			// Set size before mode: ExclusiveFullscreen uses the pre-set size to
			// pick a display mode; Borderless (Godot's Fullscreen) uses native res.
			if (_windowMode != DisplayServer.WindowMode.Fullscreen)
				DisplayServer.WindowSetSize(res);

			DisplayServer.WindowSetMode(_windowMode);

			if (_windowMode == DisplayServer.WindowMode.Windowed)
			{
				var screen = DisplayServer.ScreenGetSize();
				DisplayServer.WindowSetPosition((screen - res) / 2);
			}
		}
	}
}
