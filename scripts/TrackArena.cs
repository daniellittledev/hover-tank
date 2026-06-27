using Godot;
using System;
using System.Collections.Generic;

namespace HoverTank
{
    /// <summary>
    /// Analytic height field for the TestDrive arena: a rounded infinity-symbol
    /// (∞) channel as the central feature, surrounded by a detailed, varied
    /// apron — craters, ramps/jumps, eroded ridges and rough edges — ringed by
    /// distant background mountains. Used by <see cref="TerrainGenerator"/> in
    /// TestDrive mode.
    ///
    /// This is a plain helper (no Node): it answers <see cref="SampleHeight"/>
    /// for any world (x, z). The channel is modelled as two overlapping circles
    /// (the ∞ loops) whose perimeters cross at the origin, so the player — which
    /// spawns at (0,0) — drops into the central crossing. Detail comes entirely
    /// from the function, so the renderer can sample it at any resolution (LOD)
    /// and the collision field samples it uniformly.
    /// </summary>
    public sealed class TrackArena
    {
        // ── Extents (metres) ────────────────────────────────────────────────
        // Render/visual grid spans ±RenderHalf (includes the background
        // mountains). Collision only needs to cover where the tank can reach,
        // so it spans ±CollisionHalf (just past the boundary). The tank is
        // contained by an invisible circle of BoundaryRadius.
        public const float RenderHalf     = 210f;
        public const float CollisionHalf  = 160f;
        public const float BoundaryRadius = 150f;

        // ── Infinity-symbol channel: two overlapping circles ────────────────
        // Loops are circles of radius CircleR centred at (±CircleC, 0). With
        // CircleC < CircleR the perimeters cross near the origin → the ∞ pinch.
        private const float CircleR = 56f;
        private const float CircleC = 48f;

        // Channel cross-section (relative to surrounding ground).
        private const float ChannelHalfWidth = 9f;     // flat groove half-width
        private const float WallWidth        = 7f;     // ramp from floor up to ground
        private const float ChannelFloor     = -4.5f;  // groove depth
        private const float BermHeight       = 2.8f;   // raised lip at the rim
        private const float BermWidth        = 5f;
        private static readonly float WallOuter = ChannelHalfWidth + WallWidth;

        // ── Background mountains (radius band, beyond the boundary) ──────────
        private const float MtnStart = 162f;   // rise begins here
        private const float MtnPeak  = 206f;   // full height by here
        private const float MtnMax   = 92f;

        // ── Noise sources ───────────────────────────────────────────────────
        private const float WarpAmp = 18f;     // domain-warp displacement
        private readonly FastNoiseLite _macro;   // broad undulation
        private readonly FastNoiseLite _ridge;   // ridged → erosion / mountain crags
        private readonly FastNoiseLite _micro;   // high-freq roughness
        private readonly FastNoiseLite _warp;    // domain warp offsets

        // ── Authored features ───────────────────────────────────────────────
        private readonly Crater[] _craters;
        private readonly Ramp[]   _ramps;

        private readonly struct Crater
        {
            public readonly float X, Z, Radius, Depth;
            public Crater(float x, float z, float r, float d) { X = x; Z = z; Radius = r; Depth = d; }
        }

        // A ramp/kicker: rises linearly along its facing direction to a lip,
        // then ends in a drop so a fast tank launches off it.
        private readonly struct Ramp
        {
            public readonly float X, Z, DirX, DirZ, Length, HalfWidth, Peak;
            public Ramp(float x, float z, float dirDeg, float length, float halfWidth, float peak)
            {
                X = x; Z = z;
                float a = Mathf.DegToRad(dirDeg);
                DirX = MathF.Cos(a); DirZ = MathF.Sin(a);
                Length = length; HalfWidth = halfWidth; Peak = peak;
            }
        }

        public TrackArena(int seed)
        {
            _macro = new FastNoiseLite { Seed = seed,     Frequency = 0.006f, FractalOctaves = 4, FractalGain = 0.45f };
            _ridge = new FastNoiseLite { Seed = seed + 3, Frequency = 0.012f, FractalOctaves = 5, FractalGain = 0.55f };
            _ridge.FractalType = FastNoiseLite.FractalTypeEnum.Ridged;
            _micro = new FastNoiseLite { Seed = seed + 9, Frequency = 0.10f,  FractalOctaves = 3, FractalGain = 0.5f };
            _warp  = new FastNoiseLite { Seed = seed + 17, Frequency = 0.013f };

            _craters = BuildCraters(seed);
            _ramps   = BuildRamps();
        }

        // Scatter craters across the apron and loop infields, keeping them off
        // the racing line (channel) and away from the spawn crossing.
        private Crater[] BuildCraters(int seed)
        {
            var rng = new Random(seed + 101);
            var list = new List<Crater>();
            int attempts = 0;
            while (list.Count < 22 && attempts++ < 400)
            {
                float ang = (float)(rng.NextDouble() * Math.Tau);
                float rad = 18f + (float)rng.NextDouble() * (BoundaryRadius - 24f);
                float cx = MathF.Cos(ang) * rad;
                float cz = MathF.Sin(ang) * rad;
                if (ChannelDistance(cx, cz) < 20f) continue;       // not on the track
                if (cx * cx + cz * cz < 26f * 26f) continue;       // not on spawn
                float cr = 6f + (float)rng.NextDouble() * 11f;
                float cd = 1.6f + (float)rng.NextDouble() * 2.6f;
                list.Add(new Crater(cx, cz, cr, cd));
            }
            return list.ToArray();
        }

        // A handful of hand-placed kickers along the loops for air time.
        private Ramp[] BuildRamps()
        {
            return new[]
            {
                // Outer apex of each loop (facing along the loop's travel).
                new Ramp( 100f,   0f,  90f, 15f, 8f, 4.0f),
                new Ramp(-100f,   0f, 270f, 15f, 8f, 4.0f),
                // Top and bottom straights between the loops.
                new Ramp(  46f,  46f,   0f, 13f, 7f, 3.2f),
                new Ramp( -46f, -46f, 180f, 13f, 7f, 3.2f),
            };
        }

        // World-space terrain height (metres) at (x, z).
        public float SampleHeight(float x, float z)
        {
            // Domain-warped coords for organic, non-grid-aligned noise shapes.
            float wx = x + _warp.GetNoise2D(x, z) * WarpAmp;
            float wz = z + _warp.GetNoise2D(x + 137.1f, z - 91.7f) * WarpAmp;

            // ── Base ground: broad undulation + eroded ridges + micro roughness
            float h = _macro.GetNoise2D(wx, wz) * 3.0f;
            h += (_ridge.GetNoise2D(wx, wz) * 0.5f + 0.5f) * 2.6f; // ridges sit above the valleys
            h += _micro.GetNoise2D(x, z) * 0.45f;

            // ── Infinity channel + berm ─────────────────────────────────────
            float dChan = ChannelDistance(x, z);
            if (dChan < WallOuter)
            {
                float k = Mathf.SmoothStep(ChannelHalfWidth, WallOuter, dChan); // 0 floor .. 1 ground
                h += Mathf.Lerp(ChannelFloor, 0f, k);
            }
            float bd   = (dChan - WallOuter) / BermWidth;
            float lip  = MathF.Exp(-bd * bd);          // peaks on the berm crest
            h += BermHeight * lip;
            h += _micro.GetNoise2D(x * 1.8f, z * 1.8f) * 0.9f * lip; // roughen the rim

            // ── Craters ─────────────────────────────────────────────────────
            foreach (var c in _craters)
            {
                float dx = x - c.X, dz = z - c.Z;
                float distSq = dx * dx + dz * dz;
                float rSq = c.Radius * c.Radius;
                if (distSq >= rSq) continue;
                float t   = MathF.Sqrt(distSq) / c.Radius;
                float bowl = c.Depth * (1f - t * t);
                float rim  = c.Depth * 0.3f * MathF.Exp(-20f * (t - 0.9f) * (t - 0.9f));
                h += rim - bowl;
            }

            // ── Ramps / kickers ─────────────────────────────────────────────
            foreach (var r in _ramps)
            {
                float dx = x - r.X, dz = z - r.Z;
                float u = dx * r.DirX + dz * r.DirZ;        // along facing
                float v = -dx * r.DirZ + dz * r.DirX;       // across
                if (u < 0f || u > r.Length || MathF.Abs(v) > r.HalfWidth) continue;
                float across = 1f - Mathf.SmoothStep(r.HalfWidth * 0.6f, r.HalfWidth, MathF.Abs(v));
                h += r.Peak * (u / r.Length) * across;      // linear rise to the lip, then a drop
            }

            // ── Background mountains (radius band) ──────────────────────────
            float rad = MathF.Sqrt(x * x + z * z);
            float mt = Mathf.SmoothStep(MtnStart, MtnPeak, rad);
            if (mt > 0f)
            {
                float crag = 0.4f + 0.6f * (_ridge.GetNoise2D(wx * 1.3f, wz * 1.3f) * 0.5f + 0.5f);
                h += mt * mt * MtnMax * crag;
            }

            return h;
        }

        // Distance from (x, z) to the nearest ∞-loop perimeter (the track
        // centerline). Fully analytic — distance to a circle is |‖p−c‖ − R|.
        public static float ChannelDistance(float x, float z)
        {
            float dl = MathF.Abs(MathF.Sqrt((x + CircleC) * (x + CircleC) + z * z) - CircleR);
            float dr = MathF.Abs(MathF.Sqrt((x - CircleC) * (x - CircleC) + z * z) - CircleR);
            return MathF.Min(dl, dr);
        }
    }
}
