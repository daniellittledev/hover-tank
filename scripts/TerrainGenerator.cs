using Godot;
using System;
using System.Collections.Generic;
using GFileAccess = Godot.FileAccess;

namespace HoverTank
{
    /// <summary>
    /// Generates the world terrain entirely in C# at runtime.
    ///
    /// Two modes:
    ///   • Standard (default): a single finite cratered heightmap, edge-walled,
    ///     used by StandardWaves / multiplayer / split-screen. Heights are
    ///     generated from fractal noise; set <see cref="CustomMapPath"/> to
    ///     override with a packed float32 heightmap file.
    ///   • TestDrive: a finite "track arena" — mountains ringing the edge and a
    ///     large figure-8 channel as the central feature, rendered two-tier (a
    ///     fine central inset + a coarse outer ring) under a matte panel-grid
    ///     material. The height field lives in <see cref="TrackArena"/>. Selected
    ///     automatically when GameState.SinglePlayerMode == TestDrive.
    /// </summary>
    public partial class TerrainGenerator : Node3D
    {
        // ── Terrain dimensions ──────────────────────────────────────────────
        // Number of grid cells per side. Total vertices = (GridSize+1)^2.
        [Export] public int GridSize = 100;

        // World-space width and depth of each cell (metres).
        [Export] public float CellSize = 2.0f;

        // ── Heights ─────────────────────────────────────────────────────────
        // Multiplier applied to the normalised noise values (range 0..1).
        [Export] public float HeightScale = 12.0f;

        // Seed for the fractal noise used when no custom map is loaded.
        [Export] public int NoiseSeed = 42;

        // ── Craters ─────────────────────────────────────────────────────────
        [Export] public int CraterCount = 25;
        [Export] public float CraterRadiusMin = 4f;
        [Export] public float CraterRadiusMax = 18f;
        [Export] public float CraterDepth = 2.5f;

        // ── Optional custom heightmap ───────────────────────────────────────
        // Path to a packed little-endian float32 heightmap: exactly
        // (GridSize+1)² values, row-major (x varies fastest), in metres of
        // world height (not scaled by HeightScale). Leave empty to use
        // procedural noise. Intended for future hand-authored campaign maps.
        [Export] public string CustomMapPath = "";

        // Runtime state for the TestDrive track arena (see TrackArena.cs).
        private bool _trackMode;
        private TrackArena? _trackArena;
        private Material _trackMaterial = null!;

        // Finite-mode height grid, kept so HeightAt() can answer spawn queries
        // without a physics raycast (collision isn't flushed on spawn frame).
        private float[,]? _finiteHeights;
        private float _finiteOrigin;
        private int _finiteVerts;

        public override void _Ready()
        {
            // So other systems (e.g. NetworkManager spawn) can find us to probe
            // terrain height before dropping a tank in.
            AddToGroup("terrain");

            // TestDrive = finite figure-8 track arena with matte panel-grid surface.
            var gs = GameState.Instance;
            _trackMode = gs != null
                && gs.Mode == GameMode.SinglePlayer
                && gs.SinglePlayerMode == SinglePlayerMode.TestDrive;

            if (_trackMode)
                BuildTrackArena();
            else
                GenerateTerrain();
        }

        // World-space terrain height (metres) at (x, z), valid in both finite and
        // infinite modes the moment _Ready has run — no physics tick required.
        // Used to spawn the tank safely above the surface; falls back to 0 if the
        // finite grid hasn't been built or the point lies outside it.
        public float HeightAt(float x, float z)
        {
            if (_trackMode)
                return _trackArena!.SampleHeight(x, z);

            if (_finiteHeights == null) return 0f;

            // Nearest-vertex sample of the stored grid (good enough for spawn).
            int gx = Mathf.RoundToInt((x + _finiteOrigin) / CellSize);
            int gz = Mathf.RoundToInt((z + _finiteOrigin) / CellSize);
            if (gx < 0 || gz < 0 || gx >= _finiteVerts || gz >= _finiteVerts)
                return 0f;
            return _finiteHeights[gx, gz];
        }

        private void GenerateTerrain()
        {
            int verts = GridSize + 1;
            float worldSize = GridSize * CellSize;
            float origin = worldSize * 0.5f; // centre terrain on world origin

            // ── Step 1: build base height array ────────────────────────────
            float[,] heights = new float[verts, verts];

            if (!TryLoadCustomHeightmap(heights, verts))
                GenerateNoiseHeights(heights, verts);

            // Smooth the base terrain to reduce quad planarity deviation,
            // which suppresses the triangle-seam artefact. Done before crater
            // carving so crater rims remain crisp.
            SmoothHeights(heights, verts, 2);

            // ── Step 2: carve craters ───────────────────────────────────────
            CarveCraters(heights, verts, origin);

            // Keep the finished grid for HeightAt() spawn queries.
            _finiteHeights = heights;
            _finiteOrigin  = origin;
            _finiteVerts   = verts;

            // ── Step 3: build mesh ──────────────────────────────────────────
            var mesh = BuildMesh(heights, verts, origin);

            var meshInst = new MeshInstance3D { Mesh = mesh };
            meshInst.SetSurfaceOverrideMaterial(0, CreateStandardTerrainMaterial());
            AddChild(meshInst);

            // ── Step 4: physics collision via HeightMapShape3D ──────────────
            // HeightMapShape3D expects a flat Godot float[] of size W*D,
            // row-major (x varies fastest). Origin of the shape is its centre.
            var mapData = new float[verts * verts];
            for (int z = 0; z < verts; z++)
                for (int x = 0; x < verts; x++)
                    mapData[z * verts + x] = heights[x, z];

            var hmShape = new HeightMapShape3D();
            hmShape.MapWidth = verts;
            hmShape.MapDepth = verts;
            hmShape.MapData = mapData;

            var colShape = new CollisionShape3D { Shape = hmShape };

            // HeightMapShape3D spans [-MapWidth/2*CellSize .. +MapWidth/2*CellSize]
            // by default — we need to scale it to match our CellSize.
            colShape.Scale = new Vector3(CellSize, 1f, CellSize);

            var staticBody = new StaticBody3D();
            staticBody.AddChild(colShape);
            AddChild(staticBody);

            // ── Step 5: invisible edge barriers ────────────────────────────
            CreateEdgeBarriers(worldSize);
        }

        // ── Edge barriers ─────────────────────────────────────────────────
        // Four invisible collision walls around the terrain perimeter so the
        // tank cannot drive off the edge of the map.
        private void CreateEdgeBarriers(float worldSize)
            => CreateEdgeBarriers(worldSize, 20f, 20f * 0.5f - CraterDepth);

        // Explicit-height overload: the track arena needs tall walls (mountains
        // reach ~70 m and the jets can launch the tank high near the rim), so it
        // passes a taller wall and a higher centre than the crater-depth default.
        private void CreateEdgeBarriers(float worldSize, float wallHeight, float wallY)
        {
            float wallThickness = 2f;
            float half = worldSize * 0.5f;

            var walls = new (Vector3 pos, Vector3 size)[]
            {
                (new Vector3( half + wallThickness * 0.5f, wallY, 0f),
                 new Vector3(wallThickness, wallHeight, worldSize + wallThickness * 2f)),
                (new Vector3(-half - wallThickness * 0.5f, wallY, 0f),
                 new Vector3(wallThickness, wallHeight, worldSize + wallThickness * 2f)),
                (new Vector3(0f, wallY,  half + wallThickness * 0.5f),
                 new Vector3(worldSize + wallThickness * 2f, wallHeight, wallThickness)),
                (new Vector3(0f, wallY, -half - wallThickness * 0.5f),
                 new Vector3(worldSize + wallThickness * 2f, wallHeight, wallThickness)),
            };

            foreach (var (pos, size) in walls)
            {
                var body = new StaticBody3D();
                body.AddChild(new CollisionShape3D
                {
                    Shape = new BoxShape3D { Size = size }
                });
                body.Position = pos;
                AddChild(body);
            }
        }

        // ── Height source: custom binary float heightmap ───────────────────
        // Expected format: (verts × verts) little-endian float32 values,
        // row-major (x fastest), no header. Heights are stored in world
        // metres — HeightScale is NOT applied. Returns false if the file is
        // missing, unreadable, or the wrong size.
        private bool TryLoadCustomHeightmap(float[,] heights, int verts)
        {
            if (string.IsNullOrEmpty(CustomMapPath)) return false;
            if (!GFileAccess.FileExists(CustomMapPath)) return false;

            using var file = GFileAccess.Open(CustomMapPath, GFileAccess.ModeFlags.Read);
            if (file == null) return false;

            long expectedBytes = (long)verts * verts * sizeof(float);
            if ((long)file.GetLength() < expectedBytes)
            {
                GD.PushWarning($"CustomMapPath '{CustomMapPath}' is smaller than the required {expectedBytes} bytes for a {verts}×{verts} heightmap — falling back to procedural noise.");
                return false;
            }

            for (int z = 0; z < verts; z++)
                for (int x = 0; x < verts; x++)
                    heights[x, z] = file.GetFloat();

            return true;
        }

        private void GenerateNoiseHeights(float[,] heights, int verts)
        {
            var noise = new FastNoiseLite();
            noise.Seed = NoiseSeed;
            noise.Frequency = 0.010f;       // lower = broader, more gradual hills
            noise.FractalOctaves = 4;
            noise.FractalGain = 0.40f;      // less weight on high-frequency octaves
            noise.FractalLacunarity = 2.0f;

            for (int z = 0; z < verts; z++)
                for (int x = 0; x < verts; x++)
                    heights[x, z] = (noise.GetNoise2D(x, z) * 0.5f + 0.5f) * HeightScale;
        }

        // Box-blur the height field to smooth high-frequency noise before mesh
        // construction. Reduces the planarity deviation within each quad, which
        // visually suppresses triangle-seam artefacts under specular lighting.
        private static void SmoothHeights(float[,] heights, int verts, int passes)
        {
            var tmp = new float[verts, verts];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int z = 0; z < verts; z++)
                {
                    for (int x = 0; x < verts; x++)
                    {
                        float sum = 0f;
                        int count = 0;
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                int nx = x + dx, nz = z + dz;
                                if ((uint)nx < (uint)verts && (uint)nz < (uint)verts)
                                { sum += heights[nx, nz]; count++; }
                            }
                        }
                        tmp[x, z] = sum / count;
                    }
                }
                for (int z = 0; z < verts; z++)
                    for (int x = 0; x < verts; x++)
                        heights[x, z] = tmp[x, z];
            }
        }

        // ── Crater carving ──────────────────────────────────────────────────
        // Each crater is a smooth bowl with a slightly raised rim, modelled as:
        //   depression = depth * (1 - t²)       where t = dist/radius, t ∈ [0,1]
        //   rim        = depth * 0.3 * exp(-20*(t - 0.9)²)
        // The tank can drive into craters and the spring suspension will tilt it.
        private void CarveCraters(float[,] heights, int verts, float origin)
        {
            var rng = new Random(NoiseSeed + 1);

            // Pre-compute world X and Z coordinates for each grid index once,
            // rather than recomputing inside every crater × vertex iteration.
            var worldCoords = new float[verts];
            for (int i = 0; i < verts; i++)
                worldCoords[i] = i * CellSize - origin;

            for (int c = 0; c < CraterCount; c++)
            {
                float cx     = (float)(rng.NextDouble() * GridSize * CellSize) - origin;
                float cz     = (float)(rng.NextDouble() * GridSize * CellSize) - origin;
                float radius = CraterRadiusMin + (float)(rng.NextDouble() * (CraterRadiusMax - CraterRadiusMin));
                float radiusSq = radius * radius;

                for (int z = 0; z < verts; z++)
                {
                    float dzVal = worldCoords[z] - cz;
                    float dzSq  = dzVal * dzVal;

                    for (int x = 0; x < verts; x++)
                    {
                        float dxVal  = worldCoords[x] - cx;
                        float distSq = dxVal * dxVal + dzSq;

                        // Skip sqrt for vertices outside the crater radius.
                        if (distSq >= radiusSq) continue;

                        float dist = MathF.Sqrt(distSq);
                        float t    = dist / radius;
                        float bowl = CraterDepth * (1f - t * t);
                        float rim  = CraterDepth * 0.3f * MathF.Exp(-20f * (t - 0.9f) * (t - 0.9f));

                        heights[x, z] -= bowl;
                        heights[x, z] += rim;
                    }
                }
            }
        }

        // ── Mesh construction ───────────────────────────────────────────────
        private ArrayMesh BuildMesh(float[,] heights, int verts, float origin)
        {
            int vertCount  = verts * verts;
            int indexCount = GridSize * GridSize * 6;

            var positions = new Vector3[vertCount];
            var normals   = new Vector3[vertCount];
            var uvs       = new Vector2[vertCount];
            var indices   = new int[indexCount];

            // Positions and UVs
            for (int z = 0; z < verts; z++)
            {
                for (int x = 0; x < verts; x++)
                {
                    int i = z * verts + x;
                    float wx = x * CellSize - origin;
                    float wz = z * CellSize - origin;
                    positions[i] = new Vector3(wx, heights[x, z], wz);
                    uvs[i] = new Vector2((float)x / GridSize, (float)z / GridSize);
                }
            }

            // Indices — per-quad diagonal selection.
            // Choosing the diagonal whose endpoints differ least in height
            // creates the most planar triangle pair for that quad, which
            // breaks up the uniform diagonal-line artefact under specular light.
            int idx = 0;
            for (int z = 0; z < GridSize; z++)
            {
                for (int x = 0; x < GridSize; x++)
                {
                    int tl = z * verts + x;
                    int tr = tl + 1;
                    int bl = (z + 1) * verts + x;
                    int br = bl + 1;

                    if (MathF.Abs(heights[x, z] - heights[x + 1, z + 1]) <=
                        MathF.Abs(heights[x + 1, z] - heights[x, z + 1]))
                    {
                        // TL–BR diagonal
                        indices[idx++] = tl; indices[idx++] = tr; indices[idx++] = br;
                        indices[idx++] = tl; indices[idx++] = br; indices[idx++] = bl;
                    }
                    else
                    {
                        // TR–BL diagonal
                        indices[idx++] = tl; indices[idx++] = tr; indices[idx++] = bl;
                        indices[idx++] = tr; indices[idx++] = br; indices[idx++] = bl;
                    }
                }
            }

            // Normals via cross product of adjacent edges
            for (int z = 0; z < verts; z++)
            {
                int zRow     = z * verts;
                int zRowNext = Math.Min(z + 1, GridSize) * verts;
                int zRowPrev = Math.Max(z - 1, 0) * verts;

                for (int x = 0; x < verts; x++)
                {
                    // Sample neighbours, clamped to grid bounds
                    Vector3 dx = positions[zRow + Math.Min(x + 1, GridSize)]
                                - positions[zRow + Math.Max(x - 1, 0)];
                    Vector3 dz = positions[zRowNext + x]
                                - positions[zRowPrev + x];
                    normals[zRow + x] = dz.Cross(dx).Normalized();
                }
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.TexUV]  = uvs;
            arrays[(int)Mesh.ArrayType.Index]  = indices;

            var arrMesh = new ArrayMesh();
            arrMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return arrMesh;
        }

        // ═══════════════════════════════════════════════════════════════════
        // TestDrive track arena (figure-8 channel — see TrackArena.cs)
        // ═══════════════════════════════════════════════════════════════════

        // Builds the finite TestDrive arena. Detail comes entirely from the
        // analytic TrackArena height field, so render and collision just sample
        // it: the render is a grid of chunks, each a single mesh — fine over the
        // reachable disc, coarse for the distant backdrop mountains; collision
        // is one uniform heightfield over the reachable disc. The tank is kept
        // in by an invisible circular wall; mountains beyond it are backdrop.
        private void BuildTrackArena()
        {
            _trackArena    = new TrackArena(NoiseSeed);
            _trackMaterial = CreateDuneTerrainMaterial();

            BuildChunkedTerrain();
            BuildArenaCollision();
            CreateCircularBoundary(TrackArena.BoundaryRadius, height: 120f, segments: 64);
        }

        // ── Render: grid of single-resolution chunks ────────────────────────
        // No camera-distance LOD: the reachable disc is small enough (~165 m)
        // that uniform fine chunks are cheap (~60k verts total), and uniform
        // resolution means neighbouring chunks sample the analytic field at the
        // same shared-edge world positions — so edges match exactly, with no
        // cracks and no LOD cross-fade. (The old VisibilityRange fade dithered
        // overlapping LODs and made their mismatched skirts flicker.)
        private const float ChunkSize = 30f;     // world metres per chunk side
        private const float ChunkSkirt = 6f;     // downward skirt to hide the fine↔coarse seam
        private const float ReachableCell = 1.25f; // fine; matches collision sampling
        private const float FarCell       = 4.0f;  // coarse backdrop mountains

        private void BuildChunkedTerrain()
        {
            int n = Mathf.CeilToInt(2f * TrackArena.RenderHalf / ChunkSize); // 14
            float half = ChunkSize * 0.5f;

            for (int cz = 0; cz < n; cz++)
            {
                for (int cx = 0; cx < n; cx++)
                {
                    float centerX = -TrackArena.RenderHalf + (cx + 0.5f) * ChunkSize;
                    float centerZ = -TrackArena.RenderHalf + (cz + 0.5f) * ChunkSize;

                    // Chunks the tank can reach get the fine mesh; far backdrop
                    // chunks (mountains) only need the coarse one.
                    float dc = Mathf.Sqrt(centerX * centerX + centerZ * centerZ);
                    float cell = dc < TrackArena.BoundaryRadius + 15f ? ReachableCell : FarCell;

                    var inst = new MeshInstance3D
                    {
                        Name     = $"Chunk_{cx}_{cz}",
                        Mesh     = BuildChunkMesh(centerX, centerZ, half, cell),
                        Position = new Vector3(centerX, 0f, centerZ),
                    };
                    inst.SetSurfaceOverrideMaterial(0, _trackMaterial);
                    AddChild(inst);
                }
            }
        }

        // One chunk mesh in local space (centred on its chunk, [-half..half] on
        // X/Z). Heights and smooth normals are sampled directly from the
        // analytic field — normals use a fixed-epsilon central difference so
        // shading stays smooth (no visible facets) regardless of cell size. A
        // downward skirt around the perimeter hides the crack along the seam
        // between the fine reachable ring and the coarse backdrop chunks. (Same-
        // resolution neighbours share exact edges, so their coplanar, identically
        // shaded skirts overlap invisibly.)
        private ArrayMesh BuildChunkMesh(float centerX, float centerZ, float half, float cell)
        {
            int verts = Mathf.RoundToInt(2f * half / cell) + 1;

            var positions = new List<Vector3>(verts * verts);
            var normals   = new List<Vector3>(verts * verts);
            var uvs       = new List<Vector2>(verts * verts);

            for (int z = 0; z < verts; z++)
            {
                for (int x = 0; x < verts; x++)
                {
                    float lx = -half + x * cell;
                    float lz = -half + z * cell;
                    float wx = centerX + lx;
                    float wz = centerZ + lz;
                    positions.Add(new Vector3(lx, _trackArena!.SampleHeight(wx, wz), lz));
                    uvs.Add(new Vector2(wx, wz) * 0.1f); // unused by the panel-grid shader
                    normals.Add(AnalyticNormal(wx, wz));
                }
            }

            int Vid(int x, int z) => z * verts + x;
            var indices = new List<int>();
            for (int z = 0; z < verts - 1; z++)
            {
                for (int x = 0; x < verts - 1; x++)
                {
                    int tl = Vid(x, z), tr = Vid(x + 1, z), bl = Vid(x, z + 1), br = Vid(x + 1, z + 1);
                    if (MathF.Abs(positions[tl].Y - positions[br].Y) <=
                        MathF.Abs(positions[tr].Y - positions[bl].Y))
                    {
                        indices.Add(tl); indices.Add(tr); indices.Add(br);
                        indices.Add(tl); indices.Add(br); indices.Add(bl);
                    }
                    else
                    {
                        indices.Add(tl); indices.Add(tr); indices.Add(bl);
                        indices.Add(tr); indices.Add(br); indices.Add(bl);
                    }
                }
            }

            // Skirts along the four edges. Emitted double-sided (both windings)
            // so they fill the seam regardless of view direction.
            void AddSkirt(System.Func<int, int> edgeVid, int count)
            {
                int firstBottom = positions.Count;
                for (int k = 0; k < count; k++)
                {
                    int top = edgeVid(k);
                    var p = positions[top];
                    positions.Add(new Vector3(p.X, p.Y - ChunkSkirt, p.Z));
                    normals.Add(normals[top]);
                    uvs.Add(uvs[top]);
                }
                for (int k = 0; k < count - 1; k++)
                {
                    int t0 = edgeVid(k), t1 = edgeVid(k + 1);
                    int b0 = firstBottom + k, b1 = firstBottom + k + 1;
                    indices.Add(t0); indices.Add(t1); indices.Add(b1);
                    indices.Add(t0); indices.Add(b1); indices.Add(b0);
                    indices.Add(t0); indices.Add(b1); indices.Add(t1); // reverse winding
                    indices.Add(t0); indices.Add(b0); indices.Add(b1);
                }
            }
            AddSkirt(k => Vid(k, 0), verts);            // bottom edge
            AddSkirt(k => Vid(k, verts - 1), verts);    // top edge
            AddSkirt(k => Vid(0, k), verts);            // left edge
            AddSkirt(k => Vid(verts - 1, k), verts);    // right edge

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV]  = uvs.ToArray();
            arrays[(int)Mesh.ArrayType.Index]  = indices.ToArray();

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }

        // Smooth surface normal from the height field's gradient (fixed epsilon,
        // independent of mesh cell size → no faceting, consistent across LODs).
        private Vector3 AnalyticNormal(float wx, float wz)
        {
            const float e = 0.9f;
            float hL = _trackArena!.SampleHeight(wx - e, wz);
            float hR = _trackArena!.SampleHeight(wx + e, wz);
            float hD = _trackArena!.SampleHeight(wx, wz - e);
            float hU = _trackArena!.SampleHeight(wx, wz + e);
            return new Vector3(hL - hR, 2f * e, hD - hU).Normalized();
        }

        // ── Collision: one uniform heightfield over the reachable disc ──────
        // Collision needs no LOD — the tank only touches terrain near it — so a
        // single medium-fine HeightMapShape3D over ±CollisionHalf captures the
        // channel, craters and ramps as drivable geometry. Cheap: physics only
        // narrow-phases cells under the body.
        private void BuildArenaCollision()
        {
            const float cell = 1.25f;
            int verts = Mathf.RoundToInt(2f * TrackArena.CollisionHalf / cell) + 1;
            var mapData = new float[verts * verts];
            for (int z = 0; z < verts; z++)
                for (int x = 0; x < verts; x++)
                    mapData[z * verts + x] = _trackArena!.SampleHeight(
                        -TrackArena.CollisionHalf + x * cell,
                        -TrackArena.CollisionHalf + z * cell);

            var colShape = new CollisionShape3D
            {
                Shape = new HeightMapShape3D { MapWidth = verts, MapDepth = verts, MapData = mapData },
                Scale = new Vector3(cell, 1f, cell), // centred on world origin
            };
            var body = new StaticBody3D();
            body.AddChild(colShape);
            AddChild(body);
        }

        // ── Circular boundary ───────────────────────────────────────────────
        // A ring of tangent box walls approximating a cylinder, so the tank
        // can't drive (or jet) out of the arena. Replaces the square edge walls.
        private void CreateCircularBoundary(float radius, float height, int segments)
        {
            float segLen = (float)(Math.Tau * radius / segments) * 1.2f; // slight overlap
            var body = new StaticBody3D { Name = "Boundary" };
            for (int i = 0; i < segments; i++)
            {
                float ang = (float)(i * Math.Tau / segments);
                var col = new CollisionShape3D
                {
                    Shape    = new BoxShape3D { Size = new Vector3(2f, height, segLen) },
                    Position = new Vector3(MathF.Cos(ang) * radius, height * 0.5f - 20f, MathF.Sin(ang) * radius),
                    Rotation = new Vector3(0f, -ang, 0f),
                };
                body.AddChild(col);
            }
            AddChild(body);
        }

        // Standard (combat/MP) terrain material. Instead of one flat albedo, it
        // blends by slope and height — lighter dust on flats and crests, darker
        // rock on steep crater walls — varies roughness with slope, and adds a
        // faint large-scale mottle so the ground doesn't read as a single flat
        // tone. World normal/position come from the vertex stage. Asset-free.
        private static ShaderMaterial CreateStandardTerrainMaterial()
        {
            var shader = new Shader
            {
                Code = @"
shader_type spatial;

uniform vec3  valley_color : source_color = vec3(0.40, 0.37, 0.32);
uniform vec3  slope_color  : source_color = vec3(0.26, 0.24, 0.22);
uniform vec3  crest_color  : source_color = vec3(0.56, 0.53, 0.47);
uniform float height_low  = -3.0;
uniform float height_high = 12.0;

varying vec3 world_normal;
varying vec3 world_pos;

float hash(vec2 p) { return fract(sin(dot(p, vec2(41.3, 289.1))) * 43758.5453); }
float vnoise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = hash(i),            b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0)), d = hash(i + vec2(1.0, 1.0));
    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

void vertex() {
    world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
    world_pos    = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    float slope = 1.0 - clamp(world_normal.y, 0.0, 1.0); // 0 flat .. 1 vertical
    vec3  col   = mix(valley_color, slope_color, smoothstep(0.22, 0.6, slope));

    float hf = smoothstep(height_low, height_high, world_pos.y);
    col = mix(col, crest_color, hf * 0.35);

    // Faint large-scale mottle to break up flat tone.
    float n = vnoise(world_pos.xz * 0.08);
    col *= 0.92 + 0.16 * n;

    ALBEDO    = col;
    ROUGHNESS = mix(0.95, 0.78, slope);
    METALLIC  = 0.0;
}
",
            };
            return new ShaderMaterial { Shader = shader };
        }

        // Dune terrain ShaderMaterial for the TestDrive sandbox: a soft dark
        // blue-grey surface with a subtle world-space tile grid (a panelled-floor
        // feel, not glowing neon) and a faint cool rim on ridge silhouettes. No
        // emissive colour of its own — the teal-blue distance comes from the
        // scene's aerial fog and the warmth from the sunset sky. One material
        // shared across all chunks.
        //
        // world_pos comes from MODEL_MATRIX*VERTEX (chunk roots at y=0, heights in
        // world metres). NORMAL/VIEW are view-space, which is what fresnel needs.
        private ShaderMaterial CreateDuneTerrainMaterial()
        {
            var shader = new Shader
            {
                Code = @"
shader_type spatial;
render_mode cull_back;

uniform vec3  base_color : source_color = vec3(0.12, 0.15, 0.21); // dark blue-grey
uniform vec3  grid_color : source_color = vec3(0.20, 0.24, 0.31); // subtle lighter seams
uniform vec3  rim_color  : source_color = vec3(0.45, 0.62, 0.72); // faint cool crest rim
uniform float grid_period = 6.0;   // world metres between grid lines
uniform float grid_width  = 0.03;
uniform float rim_energy   = 0.25;

varying vec3 world_pos;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    // Subtle world-space panel grid (tiled-floor feel, stays put as you move).
    // Derivative-based antialiasing: line thickness is measured in pixels via
    // fwidth, so the lines stay ~1px and don't alias/moiré at distance or on
    // grazing mountain slopes. The grid also fades out where cells shrink below
    // a couple of pixels, instead of crawling into a shimmering haze.
    vec2  coord = world_pos.xz / grid_period;
    vec2  deriv = fwidth(coord);
    vec2  gdist = abs(fract(coord - 0.5) - 0.5) / max(deriv, vec2(1e-5));
    float line  = 1.0 - clamp(min(gdist.x, gdist.y) - grid_width, 0.0, 1.0);
    float fade  = 1.0 - smoothstep(0.35, 1.0, max(deriv.x, deriv.y));
    ALBEDO      = mix(base_color, grid_color, line * 0.7 * fade);
    ROUGHNESS   = 0.82;
    METALLIC    = 0.0;

    // Faint cool rim so ridge silhouettes catch a little light.
    float fres = pow(1.0 - clamp(dot(normalize(NORMAL), normalize(VIEW)), 0.0, 1.0), 4.0);
    EMISSION   = rim_color * fres * rim_energy;
}
",
            };
            return new ShaderMaterial { Shader = shader };
        }
    }
}
