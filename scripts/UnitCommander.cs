using Godot;
using System.Collections.Generic;

namespace HoverTank
{
    /// <summary>
    /// Attached as a child of the player's HoverTank in single-player mode.
    /// Handles crosshair-based ally selection and order dispatch.
    ///
    /// Controls:
    ///   - SPACE        — context-sensitive: select ally under crosshair, or
    ///                    (with units selected) issue attack/move to target
    ///                    under crosshair.
    ///   - Shift+SPACE  — add hovered ally to selection.
    ///   - Ctrl+SPACE   — toggle hovered ally in selection.
    ///   - G / H        — order selected units to Follow / Hold.
    ///   - F            — cycle formation (Wedge / Line / Column).
    ///   - ESC          — deselect all (falls through to pause menu when
    ///                    nothing is selected).
    ///
    /// Drives the HUD:
    ///   - Glowing terrain-preview ring that tracks the crosshair while units
    ///     are selected (green = move target, red = attack target).
    ///   - Row of unit-card icons along the top-left of the screen.
    ///   - Contextual action list showing available keys and the active order.
    /// </summary>
    public partial class UnitCommander : Node3D
    {
        // ── Formation ────────────────────────────────────────────────────────
        private enum FormationType { Wedge, Line, Column }

        // Spacing between formation slots (metres).
        private const float SlotSpacing = 8f;

        // ── State ────────────────────────────────────────────────────────────
        private readonly List<AllyAI> _selected      = new();
        private FormationType          _formation     = FormationType.Wedge;
        private AllyAI.AllyOrder       _groupOrder    = AllyAI.AllyOrder.Follow;

        // ── Scene refs ───────────────────────────────────────────────────────
        private HoverTank     _player  = null!;
        private FollowCamera  _camera  = null!;

        // ── Terrain ring marker ──────────────────────────────────────────────
        private MeshInstance3D _ring       = null!;
        private StandardMaterial3D _ringMat = null!;
        private float _ringPulsePhase;

        // ── Per-frame crosshair state (updated in _Process) ──────────────────
        private enum CrosshairHit { Terrain, Ally, Enemy }
        private CrosshairHit _crosshairHit = CrosshairHit.Terrain;
        private AllyAI?      _hoveredAlly;
        private HoverTank?   _hoveredEnemy;

        // ── HUD ──────────────────────────────────────────────────────────────
        private CanvasLayer   _layer      = null!;
        private HBoxContainer _cardRow    = null!;
        private VBoxContainer _actionBox  = null!;
        private Label         _orderLabel = null!;      // current order text

        // Labels that show the highlighted key hint in the action list.
        private readonly Label[] _actionLabels = new Label[5];

        // One live card per selected ally (order matches _selected).
        private readonly List<UnitCard> _cards = new();

        private sealed class UnitCard
        {
            public Control    Root       = null!;
            public ColorRect  HealthFill = null!;
            public ColorRect  StatusDot  = null!;
            public AllyAI     Ally       = null!;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        public override void _Ready()
        {
            _player  = GetParent<HoverTank>();
            _camera  = _player.AimCamera!;

            BuildRingMarker();
            BuildHUD();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Input — Escape intercept (deselect when selections exist)
        // ─────────────────────────────────────────────────────────────────────

        public override void _Input(InputEvent evt)
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured) return;
            if (evt is not InputEventKey key || !key.Pressed || key.Echo) return;

            // Escape: consume and deselect when there's an active selection so
            // GameSetup's _UnhandledInput pause handler doesn't also fire.
            if (evt.IsAction("unit_deselect_all") && _selected.Count > 0)
            {
                DeselectAll();
                GetViewport().SetInputAsHandled();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Process — every render frame
        // ─────────────────────────────────────────────────────────────────────

        public override void _Process(double delta)
        {
            UpdateCrosshairState();
            UpdateFormationSlots();
            ProcessSelectKey();
            ProcessOrderKeys();
            UpdateRingMarker((float)delta);
            RefreshHUD();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Crosshair raycast
        // ─────────────────────────────────────────────────────────────────────

        private void UpdateCrosshairState()
        {
            _hoveredAlly  = null;
            _hoveredEnemy = null;
            _crosshairHit = CrosshairHit.Terrain;

            if (_camera == null) return;

            var space  = GetWorld3D().DirectSpaceState;
            var origin = _camera.GlobalPosition;
            var end    = origin + (-_camera.GlobalBasis.Z) * 300f;
            var query  = PhysicsRayQueryParameters3D.Create(origin, end);
            query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };

            var hit = space.IntersectRay(query);
            if (hit.Count == 0) return;

            if (hit["collider"].AsGodotObject() is HoverTank t)
            {
                if (t.IsFriendlyAI && t.Health > 0f)
                {
                    _crosshairHit = CrosshairHit.Ally;
                    _hoveredAlly  = t.GetNodeOrNull<AllyAI>("AllyAI");
                }
                else if (t.IsEnemy && t.Health > 0f)
                {
                    _crosshairHit = CrosshairHit.Enemy;
                    _hoveredEnemy = t;
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Selection management
        // ─────────────────────────────────────────────────────────────────────

        private void SelectOnly(AllyAI ally)
        {
            foreach (var a in _selected) a.IsSelected = false;
            _selected.Clear();
            _cards.Clear();
            foreach (var child in _cardRow.GetChildren())
                child.QueueFree();

            _selected.Add(ally);
            ally.IsSelected = true;
            ally.CurrentOrder = _groupOrder;
            AddCard(ally);
        }

        private void AddToSelection(AllyAI ally)
        {
            if (_selected.Contains(ally)) return;
            _selected.Add(ally);
            ally.IsSelected = true;
            ally.CurrentOrder = _groupOrder;
            AddCard(ally);
        }

        private void ToggleSelection(AllyAI ally)
        {
            int idx = _selected.IndexOf(ally);
            if (idx >= 0)
            {
                ally.IsSelected = false;
                _selected.RemoveAt(idx);
                RemoveCard(idx);
            }
            else
            {
                AddToSelection(ally);
            }
        }

        // Remove dead allies from the selection each frame.
        private void PruneDeadAllies()
        {
            for (int i = _selected.Count - 1; i >= 0; i--)
            {
                var ally = _selected[i];
                if (!IsInstanceValid(ally) || ally.Tank == null || ally.Tank.Health <= 0f)
                {
                    if (IsInstanceValid(ally)) ally.IsSelected = false;
                    _selected.RemoveAt(i);
                    RemoveCard(i);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Order dispatch
        // ─────────────────────────────────────────────────────────────────────

        // Context-sensitive SPACE: select hovered ally (with modifier variants),
        // or issue attack/move to the crosshair target when units are selected.
        private void ProcessSelectKey()
        {
            if (Input.MouseMode != Input.MouseModeEnum.Captured) return;
            if (!Input.IsActionJustPressed("unit_select")) return;

            // Pointing at an ally → selection action.
            if (_hoveredAlly != null)
            {
                if (Input.IsKeyPressed(Key.Shift))
                    AddToSelection(_hoveredAlly);
                else if (Input.IsKeyPressed(Key.Ctrl))
                    ToggleSelection(_hoveredAlly);
                else
                    SelectOnly(_hoveredAlly);
                return;
            }

            // No ally under crosshair: issue a context-sensitive order if there
            // are units selected. Enemy under crosshair → attack; otherwise move.
            if (_selected.Count == 0) return;

            if (_hoveredEnemy != null)
                IssueAttackOrder();
            else
                IssueMoveOrder();
        }

        private void ProcessOrderKeys()
        {
            if (_selected.Count == 0) return;

            if (Input.IsActionJustPressed("unit_order_follow"))
                IssueOrder(AllyAI.AllyOrder.Follow);
            else if (Input.IsActionJustPressed("unit_order_hold"))
                IssueOrder(AllyAI.AllyOrder.Hold);
            else if (Input.IsActionJustPressed("unit_formation_cycle"))
                CycleFormation();
        }

        private void IssueOrder(AllyAI.AllyOrder order)
        {
            _groupOrder = order;
            foreach (var a in _selected)
                a.CurrentOrder = order;
        }

        private void IssueMoveOrder()
        {
            // Move all selected allies to the current aim point on terrain.
            Vector3 dest = _camera.AimTarget;
            _groupOrder = AllyAI.AllyOrder.MoveToWaypoint;
            foreach (var a in _selected)
            {
                a.WaypointPosition = dest;
                a.CurrentOrder     = AllyAI.AllyOrder.MoveToWaypoint;
            }
        }

        private void IssueAttackOrder()
        {
            if (_hoveredEnemy == null) return;
            _groupOrder = AllyAI.AllyOrder.AttackTarget;
            foreach (var a in _selected)
            {
                a.AttackTarget = _hoveredEnemy;
                a.CurrentOrder = AllyAI.AllyOrder.AttackTarget;
            }
        }

        private void CycleFormation()
        {
            _formation = _formation switch
            {
                FormationType.Wedge  => FormationType.Line,
                FormationType.Line   => FormationType.Column,
                _                   => FormationType.Wedge,
            };
        }

        private void DeselectAll()
        {
            foreach (var a in _selected) a.IsSelected = false;
            _selected.Clear();
            foreach (var child in _cardRow.GetChildren())
                child.QueueFree();
            _cards.Clear();
        }

        // ─────────────────────────────────────────────────────────────────────
        // Formation slots
        // ─────────────────────────────────────────────────────────────────────

        // Assign world-space formation slots to every ally in Follow order,
        // regardless of whether they're currently selected. Previously slots
        // were only refreshed for selected allies, so a follower that the
        // player deselected would freeze at the last slot world-position
        // instead of continuing to track the player.
        private void UpdateFormationSlots()
        {
            PruneDeadAllies();

            var followers = new List<AllyAI>();
            foreach (Node node in GetTree().GetNodesInGroup("hover_tanks"))
            {
                if (node is not HoverTank t) continue;
                if (!t.IsFriendlyAI || t.Health <= 0f) continue;
                var ai = t.GetNodeOrNull<AllyAI>("AllyAI");
                if (ai == null) continue;
                if (ai.CurrentOrder != AllyAI.AllyOrder.Follow) continue;
                followers.Add(ai);
            }
            if (followers.Count == 0) return;

            Vector3[] offsets = ComputeFormationOffsets(followers.Count);
            Vector3 playerFwd   = -_player.Basis.Z;
            Vector3 playerRight =  _player.Basis.X;

            for (int i = 0; i < followers.Count; i++)
            {
                Vector3 off   = offsets[i];
                // off.Z = depth behind player (positive = behind), off.X = lateral
                Vector3 world = _player.GlobalPosition
                              - playerFwd  * off.Z
                              + playerRight * off.X;
                followers[i].FormationSlot = world;
            }
        }

        // Returns local-space offsets (X = lateral, Z = depth behind player).
        private Vector3[] ComputeFormationOffsets(int count)
        {
            float D = SlotSpacing;
            var offsets = new Vector3[count];

            switch (_formation)
            {
                case FormationType.Line:
                    float span = (count - 1) * D * 0.5f;
                    for (int i = 0; i < count; i++)
                        offsets[i] = new Vector3(-span + i * D, 0f, D);
                    break;

                case FormationType.Column:
                    for (int i = 0; i < count; i++)
                        offsets[i] = new Vector3(0f, 0f, D * (i + 1));
                    break;

                default: // Wedge — alternating left/right, receding rows
                    for (int i = 0; i < count; i++)
                    {
                        int  row  = i / 2 + 1;
                        int  side = (i % 2 == 0) ? 1 : -1;
                        offsets[i] = new Vector3(side * D * row * 0.7f, 0f, D * row);
                    }
                    break;
            }
            return offsets;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Terrain ring marker
        // ─────────────────────────────────────────────────────────────────────

        private void BuildRingMarker()
        {
            // Build a circle of line-segments so it looks like a ring, not a disc.
            const int   Segments = 48;
            const float Radius   = 2.5f;

            var verts = new Vector3[Segments * 2];
            for (int i = 0; i < Segments; i++)
            {
                float a0 = Mathf.Tau * i       / Segments;
                float a1 = Mathf.Tau * (i + 1) / Segments;
                verts[i * 2]     = new Vector3(Mathf.Cos(a0) * Radius, 0f, Mathf.Sin(a0) * Radius);
                verts[i * 2 + 1] = new Vector3(Mathf.Cos(a1) * Radius, 0f, Mathf.Sin(a1) * Radius);
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts;

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);

            _ringMat = new StandardMaterial3D
            {
                ShadingMode              = StandardMaterial3D.ShadingModeEnum.Unshaded,
                EmissionEnabled          = true,
                Emission                 = new Color(0.1f, 1.0f, 0.3f),
                EmissionEnergyMultiplier = 2.5f,
            };
            mesh.SurfaceSetMaterial(0, _ringMat);

            _ring = new MeshInstance3D { Mesh = mesh, Visible = false };
            // Add to the scene root so the ring isn't parented to the tank
            // (avoids inheriting the tank's scale/rotation).
            GetTree().Root.CallDeferred("add_child", _ring);
        }

        private void UpdateRingMarker(float delta)
        {
            if (_selected.Count == 0)
            {
                _ring.Visible = false;
                return;
            }

            // Pulse scale between 0.85 and 1.15
            _ringPulsePhase += delta * 3f;
            float scale = 1f + 0.15f * Mathf.Sin(_ringPulsePhase);
            _ring.Scale = new Vector3(scale, 1f, scale);

            // Color: red when aiming at an enemy, green otherwise
            _ringMat.Emission = _crosshairHit == CrosshairHit.Enemy
                ? new Color(1.0f, 0.15f, 0.1f)
                : new Color(0.1f, 1.0f, 0.3f);

            // Position at aim target, slightly above terrain to avoid z-fighting.
            Vector3 pos = _camera.AimTarget;
            pos.Y += 0.12f;
            _ring.GlobalPosition = pos;
            _ring.Visible        = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // HUD construction
        // ─────────────────────────────────────────────────────────────────────

        private void BuildHUD()
        {
            _layer = new CanvasLayer { Layer = 5, Name = "UnitCommanderHUD" };
            AddChild(_layer);

            // ── Card row: top-left, flows right ──────────────────────────────
            _cardRow = new HBoxContainer
            {
                AnchorLeft   = 0f, AnchorRight  = 0f,
                AnchorTop    = 0f, AnchorBottom  = 0f,
                OffsetLeft   = 8f, OffsetTop     = 8f,
                OffsetRight  = 8f, OffsetBottom  = 8f,
            };
            _cardRow.AddThemeConstantOverride("separation", 4);
            _layer.AddChild(_cardRow);

            // ── Action list: below card row ───────────────────────────────────
            _actionBox = new VBoxContainer
            {
                AnchorLeft   = 0f, AnchorRight  = 0f,
                AnchorTop    = 0f, AnchorBottom  = 0f,
                OffsetLeft   = 8f, OffsetTop     = 76f,   // 8 + 56 card + 12 gap
                OffsetRight  = 8f, OffsetBottom  = 76f,
                Visible      = false,
            };
            _actionBox.AddThemeConstantOverride("separation", 2);
            _layer.AddChild(_actionBox);

            // Heading
            var heading = new Label { Text = "── ORDERS ──" };
            heading.AddThemeColorOverride("font_color",   new Color(0.55f, 0.55f, 0.55f));
            heading.AddThemeFontSizeOverride("font_size", 11);
            _actionBox.AddChild(heading);

            // Action rows: [KEY]  Description
            // Row 0 is refreshed every frame based on crosshair context.
            string[] lines = {
                "[SPACE] Move here",
                "[G]      Follow me",
                "[H]      Hold position",
                "[F]      Formation: Wedge",
                "[ESC]    Deselect all",
            };
            for (int i = 0; i < lines.Length; i++)
            {
                var lbl = new Label { Text = lines[i] };
                lbl.AddThemeFontSizeOverride("font_size", 13);
                lbl.AddThemeColorOverride("font_color", new Color(0.75f, 0.75f, 0.75f));
                _actionBox.AddChild(lbl);
                _actionLabels[i] = lbl;
            }
        }

        // ── Per-frame HUD refresh ─────────────────────────────────────────────

        private void RefreshHUD()
        {
            bool hasSelection = _selected.Count > 0;
            _actionBox.Visible = hasSelection;

            if (!hasSelection) return;

            // Follow / Hold highlight based on active group order.
            HighlightActiveOrder();

            // [SPACE] row reflects what pressing space right now would do.
            (string spaceText, Color spaceColor) = _crosshairHit switch
            {
                CrosshairHit.Enemy => ("[SPACE] Attack target", new Color(1.0f, 0.30f, 0.25f)),
                CrosshairHit.Ally  => ("[SPACE] Select ally",    new Color(0.30f, 0.85f, 1.0f)),
                _                  => ("[SPACE] Move here",      new Color(0.25f, 0.95f, 0.40f)),
            };
            _actionLabels[0].Text = spaceText;
            _actionLabels[0].AddThemeColorOverride("font_color", spaceColor);

            // Formation label.
            _actionLabels[3].Text = $"[F]      Formation: {_formation}";

            // Refresh card health / status
            for (int i = 0; i < _cards.Count && i < _selected.Count; i++)
                RefreshCard(_cards[i], _selected[i]);
        }

        private void HighlightActiveOrder()
        {
            var dim    = new Color(0.45f, 0.45f, 0.45f);
            var bright = new Color(1.0f,  0.90f, 0.30f);
            var neutral = new Color(0.75f, 0.75f, 0.75f);

            // Follow (1), Hold (2) dim/bright based on active order.
            _actionLabels[1].AddThemeColorOverride("font_color", dim);
            _actionLabels[2].AddThemeColorOverride("font_color", dim);

            int active = _groupOrder switch
            {
                AllyAI.AllyOrder.Follow => 1,
                AllyAI.AllyOrder.Hold   => 2,
                _                       => -1,
            };
            if (active >= 0)
                _actionLabels[active].AddThemeColorOverride("font_color", bright);

            // Formation + Deselect stay at neutral readable grey.
            _actionLabels[3].AddThemeColorOverride("font_color", neutral);
            _actionLabels[4].AddThemeColorOverride("font_color", neutral);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Unit cards
        // ─────────────────────────────────────────────────────────────────────

        private void AddCard(AllyAI ally)
        {
            // Outer panel
            var panel = new PanelContainer();
            var bg = new StyleBoxFlat
            {
                BgColor                = new Color(0f, 0f, 0f, 0.72f),
                CornerRadiusTopLeft    = 4, CornerRadiusTopRight    = 4,
                CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
                ContentMarginLeft = 4, ContentMarginRight  = 4,
                ContentMarginTop  = 4, ContentMarginBottom = 4,
            };
            panel.AddThemeStyleboxOverride("panel", bg);
            panel.CustomMinimumSize = new Vector2(56f, 56f);
            _cardRow.AddChild(panel);

            var vbox = new VBoxContainer();
            vbox.AddThemeConstantOverride("separation", 3);
            panel.AddChild(vbox);

            // Tank icon label (stylised text)
            var iconLbl = new Label
            {
                Text                = "[ ]",
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            iconLbl.AddThemeColorOverride("font_color",   new Color(0.2f, 1.0f, 0.45f));
            iconLbl.AddThemeFontSizeOverride("font_size", 16);
            vbox.AddChild(iconLbl);

            // Health bar track
            var track = new ColorRect
            {
                Color              = new Color(0.25f, 0.25f, 0.25f),
                CustomMinimumSize  = new Vector2(44f, 6f),
            };
            vbox.AddChild(track);

            // Health fill (child of track so it can be resized relative to it)
            var fill = new ColorRect
            {
                Color             = new Color(0.2f, 0.85f, 0.3f),
                CustomMinimumSize = new Vector2(44f, 6f),
                AnchorRight       = 1f,
            };
            track.AddChild(fill);

            // Status dot
            var dot = new ColorRect
            {
                Color             = StatusColor(AllyAI.AllyOrder.Follow),
                CustomMinimumSize = new Vector2(10f, 10f),
                AnchorLeft = 0.5f, AnchorRight = 0.5f,
            };
            vbox.AddChild(dot);

            var card = new UnitCard
            {
                Root       = panel,
                HealthFill = fill,
                StatusDot  = dot,
                Ally       = ally,
            };
            _cards.Add(card);
        }

        private void RemoveCard(int index)
        {
            if (index < 0 || index >= _cards.Count) return;
            _cards[index].Root.QueueFree();
            _cards.RemoveAt(index);
        }

        private static void RefreshCard(UnitCard card, AllyAI ally)
        {
            if (!IsInstanceValid(ally)) return;

            float hp = ally.Tank != null
                ? Mathf.Clamp(ally.Tank.Health / ally.Tank.MaxHealth, 0f, 1f)
                : 0f;

            // Scale the fill width within the 44 px track
            card.HealthFill.CustomMinimumSize = new Vector2(44f * hp, 6f);
            card.HealthFill.Color = hp > 0.5f
                ? new Color(0.2f, 0.85f, 0.3f)
                : hp > 0.25f
                    ? new Color(0.9f, 0.75f, 0.1f)
                    : new Color(0.9f, 0.2f, 0.15f);

            card.StatusDot.Color = StatusColor(ally.CurrentOrder);
        }

        private static Color StatusColor(AllyAI.AllyOrder order) => order switch
        {
            AllyAI.AllyOrder.Follow         => new Color(0.0f, 0.75f, 1.0f),
            AllyAI.AllyOrder.Hold           => new Color(1.0f, 0.65f, 0.0f),
            AllyAI.AllyOrder.MoveToWaypoint => new Color(1.0f, 1.0f, 0.15f),
            AllyAI.AllyOrder.AttackTarget   => new Color(1.0f, 0.2f, 0.15f),
            _                              => new Color(0.5f, 0.5f, 0.5f),
        };
    }
}
