using Godot;
using System.Collections.Generic;

namespace HoverTank
{
    // Draws a circular Battlezone-style radar.
    // Call UpdateData() each frame to feed fresh positions; the control redraws itself.
    public partial class RadarDisplay : Control
    {
        // World-space radius (metres) that maps to the edge of the radar circle.
        [Export] public float RadarRange = 150f;

        // ── State fed by HUD each frame ──────────────────────────────────────
        private Vector3    _playerPos;
        private Basis      _playerBasis;
        private readonly List<Vector3> _enemies = new();

        // ── Colours ──────────────────────────────────────────────────────────
        private static readonly Color ColBg      = new(0f,    0f,    0f,    0.62f);
        private static readonly Color ColBorder  = new(0.20f, 0.85f, 0.30f, 0.90f);
        private static readonly Color ColRing    = new(0.20f, 0.85f, 0.30f, 0.30f);
        private static readonly Color ColCross   = new(0.20f, 0.85f, 0.30f, 0.18f);
        private static readonly Color ColPlayer  = new(0.20f, 1.00f, 0.40f, 1.00f);
        private static readonly Color ColEnemy   = new(1.00f, 0.25f, 0.25f, 1.00f);

        public void UpdateData(Vector3 playerPos, Basis playerBasis, IReadOnlyList<Vector3> enemyPositions)
        {
            _playerPos   = playerPos;
            _playerBasis = playerBasis;
            _enemies.Clear();
            for (int i = 0; i < enemyPositions.Count; i++)
                _enemies.Add(enemyPositions[i]);
            QueueRedraw();
        }

        public override void _Draw()
        {
            Vector2 c = Size / 2f;
            float   r = Mathf.Min(c.X, c.Y) - 2f;

            // Background fill
            DrawCircle(c, r, ColBg);

            // Concentric distance rings at 1/3 and 2/3 of max range
            DrawArc(c, r * 0.333f, 0f, Mathf.Tau, 64, ColRing, 1f);
            DrawArc(c, r * 0.667f, 0f, Mathf.Tau, 64, ColRing, 1f);

            // Cardinal crosshairs
            DrawLine(c - new Vector2(r, 0f), c + new Vector2(r, 0f), ColCross, 1f);
            DrawLine(c - new Vector2(0f, r), c + new Vector2(0f, r), ColCross, 1f);

            // Outer border
            DrawArc(c, r, 0f, Mathf.Tau, 64, ColBorder, 2f);

            // Forward notch — small tick at top of circle (tank forward = radar up)
            float notchLen = 5f;
            DrawLine(c + new Vector2(0f, -r), c + new Vector2(0f, -(r - notchLen)), ColBorder, 2f);

            // Player dot
            DrawCircle(c, 3.5f, ColPlayer);

            // Enemy blips
            float scale = r / RadarRange;
            foreach (var ep in _enemies)
            {
                Vector3 offset = ep - _playerPos;

                // Project world offset onto tank's local XZ axes.
                // localX > 0  →  right of tank  →  radar right  (+screen X)
                // localZ < 0  →  ahead of tank  →  radar up     (-screen Y)
                float localX = offset.X * _playerBasis.X.X + offset.Z * _playerBasis.X.Z;
                float localZ = offset.X * _playerBasis.Z.X + offset.Z * _playerBasis.Z.Z;

                float dist2D = Mathf.Sqrt(localX * localX + localZ * localZ);
                if (dist2D > RadarRange) continue;

                Vector2 blip = c + new Vector2(localX, localZ) * scale;

                // Clamp to just inside the border circle
                Vector2 fromCenter = blip - c;
                if (fromCenter.LengthSquared() > (r - 4f) * (r - 4f))
                    blip = c + fromCenter.Normalized() * (r - 4f);

                DrawCircle(blip, 3.5f, ColEnemy);
            }
        }
    }
}
