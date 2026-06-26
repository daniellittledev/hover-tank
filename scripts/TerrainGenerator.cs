using Godot;
using System;
using System.Collections.Generic;
using GFileAccess = Godot.FileAccess;

namespace HoverTank
{
    /// <summary>
    /// Generates moon-like cratered terrain entirely in C# at runtime.
    ///
    /// Two modes:
    ///   • Standard (default): a single finite cratered heightmap, edge-walled,
    ///     used by StandardWaves / multiplayer / split-screen. Heights are
    ///     generated from fractal noise; set <see cref="CustomMapPath"/> to
    ///     override with a packed float32 heightmap file.
    ///   • TestDrive: infinite chunk-streamed terrain — gentle rolling dunes
    ///     under a matte panel-grid material. Selected automatically when
    ///     GameState.SinglePlayerMode == TestDrive.
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

        // ── TestDrive infinite-chunk streaming ──────────────────────────────
        // Cells per chunk side. Chunk world size = ChunkCells * CellSize.
        [Export] public int ChunkCells = 32;
        // Number of chunks of radius to keep loaded around the player.
        // Total loaded chunks = (2*ChunkLoadRadius+1)^2. Radius 4 keeps a wide
        // vista of swells visible before the aerial-perspective fog closes in.
        [Export] public int ChunkLoadRadius = 4;
        // Amplitude of TestDrive rolling swells (metres). Large + low-frequency
        // (see SetupInfiniteTerrain) gives the big ocean-swell dunes of the
        // reference, which the hover springs ride and the jets launch off.
        [Export] public float InfiniteHillScale = 40f;
        // World height (metres) at which terrain crests begin to glow teal, and
        // the height of full glow. Drives the emission ramp in the dream shader.
        [Export] public float CrestGlowLow  = 26f;
        [Export] public float CrestGlowHigh = 36f;

        // Runtime state for infinite mode.
        private bool _infiniteMode;
        private FastNoiseLite _hillNoise = null!;
        private Material _infiniteMaterial = null!;
        private readonly Dictionary<(int, int), Node3D> _chunks = new();
        private (int, int) _lastCenterChunk = (int.MinValue, int.MinValue);
        private Node3D? _player;

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

            // TestDrive = infinite procedural sandbox with matte panel-grid surface.
            var gs = GameState.Instance;
            _infiniteMode = gs != null
                && gs.Mode == GameMode.SinglePlayer
                && gs.SinglePlayerMode == SinglePlayerMode.TestDrive;

            if (_infiniteMode)
                SetupInfiniteTerrain();
            else
                GenerateTerrain();
        }

        // World-space terrain height (metres) at (x, z), valid in both finite and
        // infinite modes the moment _Ready has run — no physics tick required.
        // Used to spawn the tank safely above the surface; falls back to 0 if the
        // finite grid hasn't been built or the point lies outside it.
        public float HeightAt(float x, float z)
        {
            if (_infiniteMode)
                return SampleHeight(x, z);

            if (_finiteHeights == null) return 0f;

            // Nearest-vertex sample of the stored grid (good enough for spawn).
            int gx = Mathf.RoundToInt((x + _finiteOrigin) / CellSize);
            int gz = Mathf.RoundToInt((z + _finiteOrigin) / CellSize);
            if (gx < 0 || gz < 0 || gx >= _finiteVerts || gz >= _finiteVerts)
                return 0f;
            return _finiteHeights[gx, gz];
        }

        public override void _Process(double delta)
        {
            if (!_infiniteMode) return;
            UpdateStreaming();
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
        {
            float wallHeight = 20f;
            float wallThickness = 2f;
            float half = worldSize * 0.5f;
            float wallY = wallHeight * 0.5f - CraterDepth;

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
        // TestDrive infinite terrain
        // ═══════════════════════════════════════════════════════════════════

        // Noise sources are world-space so adjacent chunks share boundary values
        // exactly — no seam stitching required. Normals are also sampled from
        // world-space SampleHeight so lighting is seamless across chunks.
        private void SetupInfiniteTerrain()
        {
            _hillNoise = new FastNoiseLite
            {
                Seed              = NoiseSeed,
                Frequency         = 0.006f,   // long wavelength → big ocean-swell dunes
                FractalOctaves    = 4,        // a little medium-scale variation on the swells
                FractalGain       = 0.42f,    // keep high-frequency octaves gentle (no rubble)
                FractalLacunarity = 2.0f,
            };

            _infiniteMaterial = CreateDuneTerrainMaterial();

            // Pre-build a block of chunks around the origin so the player tank
            // (spawned after the terrain's _Ready) has solid ground under it on
            // frame 0. Streaming takes over from the next _Process tick.
            for (int dz = -ChunkLoadRadius; dz <= ChunkLoadRadius; dz++)
                for (int dx = -ChunkLoadRadius; dx <= ChunkLoadRadius; dx++)
                    _chunks[(dx, dz)] = BuildChunk(dx, dz);
            _lastCenterChunk = (0, 0);
        }

        // Symmetric fractal-noise rolling dunes at world (x, z).
        private float SampleHeight(float wx, float wz)
        {
            return _hillNoise.GetNoise2D(wx, wz) * InfiniteHillScale;
        }

        // Player movement → chunk rebalance. Only runs when the player crosses
        // a chunk boundary, so per-frame cost is a cheap floor/compare.
        private void UpdateStreaming()
        {
            if (_player == null || !GodotObject.IsInstanceValid(_player))
                _player = FindPlayerTank();
            if (_player == null) return;

            float chunkWorld = ChunkCells * CellSize;
            Vector3 pos = _player.GlobalPosition;
            int cx = Mathf.FloorToInt(pos.X / chunkWorld);
            int cz = Mathf.FloorToInt(pos.Z / chunkWorld);

            if ((cx, cz) == _lastCenterChunk && _chunks.Count > 0) return;
            _lastCenterChunk = (cx, cz);

            // Free out-of-range chunks.
            var toFree = new List<(int, int)>();
            foreach (var key in _chunks.Keys)
            {
                if (Math.Abs(key.Item1 - cx) > ChunkLoadRadius ||
                    Math.Abs(key.Item2 - cz) > ChunkLoadRadius)
                    toFree.Add(key);
            }
            foreach (var key in toFree)
            {
                _chunks[key].QueueFree();
                _chunks.Remove(key);
            }

            // Build any missing in-range chunks.
            for (int dz = -ChunkLoadRadius; dz <= ChunkLoadRadius; dz++)
                for (int dx = -ChunkLoadRadius; dx <= ChunkLoadRadius; dx++)
                {
                    var key = (cx + dx, cz + dz);
                    if (!_chunks.ContainsKey(key))
                        _chunks[key] = BuildChunk(key.Item1, key.Item2);
                }
        }

        private Node3D? FindPlayerTank()
        {
            foreach (Node node in GetTree().GetNodesInGroup("hover_tanks"))
            {
                if (node is HoverTank tank && !tank.IsEnemy && !tank.IsFriendlyAI)
                    return tank;
            }
            return null;
        }

        // One chunk = ChunkCells × ChunkCells quads of terrain + matching
        // HeightMapShape3D collision, parented under a Node3D at the chunk's
        // world-space origin.
        private Node3D BuildChunk(int cx, int cz)
        {
            int verts = ChunkCells + 1;
            float chunkWorld = ChunkCells * CellSize;
            float baseX = cx * chunkWorld;
            float baseZ = cz * chunkWorld;

            // Sample heights for this chunk's vertex grid.
            var heights = new float[verts, verts];
            for (int z = 0; z < verts; z++)
                for (int x = 0; x < verts; x++)
                    heights[x, z] = SampleHeight(baseX + x * CellSize, baseZ + z * CellSize);

            // Build mesh with seamless normals sampled across chunk boundaries.
            var mesh = BuildChunkMesh(heights, verts, baseX, baseZ);

            var meshInst = new MeshInstance3D { Mesh = mesh };
            meshInst.SetSurfaceOverrideMaterial(0, _infiniteMaterial);

            // HeightMapShape3D collision. Shape is centred on its origin, so we
            // offset the CollisionShape3D to the chunk centre.
            var mapData = new float[verts * verts];
            for (int z = 0; z < verts; z++)
                for (int x = 0; x < verts; x++)
                    mapData[z * verts + x] = heights[x, z];

            var hmShape = new HeightMapShape3D
            {
                MapWidth  = verts,
                MapDepth  = verts,
                MapData   = mapData,
            };
            var colShape = new CollisionShape3D
            {
                Shape    = hmShape,
                Scale    = new Vector3(CellSize, 1f, CellSize),
                Position = new Vector3(chunkWorld * 0.5f, 0f, chunkWorld * 0.5f),
            };
            var body = new StaticBody3D();
            body.AddChild(colShape);

            var root = new Node3D
            {
                Name     = $"Chunk_{cx}_{cz}",
                Position = new Vector3(baseX, 0f, baseZ),
            };
            root.AddChild(meshInst);
            root.AddChild(body);
            AddChild(root);
            return root;
        }

        // Chunk mesh in local space ([0..chunkWorld] on X/Z). UVs are in cell
        // units (1 UV per cell) so the grid texture tiles exactly once per
        // terrain cell regardless of chunk size. Normals use SampleHeight on
        // world-space neighbours so lighting is seamless across chunks.
        private ArrayMesh BuildChunkMesh(float[,] heights, int verts, float baseX, float baseZ)
        {
            int gridSize   = verts - 1;
            int vertCount  = verts * verts;
            int indexCount = gridSize * gridSize * 6;

            var positions = new Vector3[vertCount];
            var normals   = new Vector3[vertCount];
            var uvs       = new Vector2[vertCount];
            var indices   = new int[indexCount];

            for (int z = 0; z < verts; z++)
            {
                for (int x = 0; x < verts; x++)
                {
                    int i = z * verts + x;
                    float lx = x * CellSize;
                    float lz = z * CellSize;
                    positions[i] = new Vector3(lx, heights[x, z], lz);
                    // 2 tiles per cell — denser panel grid matching the reference.
                    // Use world coords so grid aligns seamlessly across chunk boundaries.
                    uvs[i] = new Vector2((baseX + lx) / (CellSize * 0.5f), (baseZ + lz) / (CellSize * 0.5f));

                    // Wide central-difference normal (±2 cells). Larger stencil
                    // averages out high-frequency noise, giving smoother normals
                    // that reduce the triangle-seam artefact under specular light.
                    float wx = baseX + lx;
                    float wz = baseZ + lz;
                    float hL = SampleHeight(wx - 2f * CellSize, wz);
                    float hR = SampleHeight(wx + 2f * CellSize, wz);
                    float hD = SampleHeight(wx, wz - 2f * CellSize);
                    float hU = SampleHeight(wx, wz + 2f * CellSize);
                    normals[i] = new Vector3(hL - hR, 4f * CellSize, hD - hU).Normalized();
                }
            }

            int idx = 0;
            for (int z = 0; z < gridSize; z++)
            {
                for (int x = 0; x < gridSize; x++)
                {
                    int tl = z * verts + x;
                    int tr = tl + 1;
                    int bl = (z + 1) * verts + x;
                    int br = bl + 1;

                    if (MathF.Abs(heights[x, z] - heights[x + 1, z + 1]) <=
                        MathF.Abs(heights[x + 1, z] - heights[x, z + 1]))
                    {
                        indices[idx++] = tl; indices[idx++] = tr; indices[idx++] = br;
                        indices[idx++] = tl; indices[idx++] = br; indices[idx++] = bl;
                    }
                    else
                    {
                        indices[idx++] = tl; indices[idx++] = tr; indices[idx++] = bl;
                        indices[idx++] = tr; indices[idx++] = br; indices[idx++] = bl;
                    }
                }
            }

            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = positions;
            arrays[(int)Mesh.ArrayType.Normal] = normals;
            arrays[(int)Mesh.ArrayType.TexUV]  = uvs;
            arrays[(int)Mesh.ArrayType.Index]  = indices;

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
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
    vec2  gw   = abs(fract(world_pos.xz / grid_period) - 0.5);
    float grid = smoothstep(0.5 - grid_width, 0.5, max(gw.x, gw.y));
    ALBEDO     = mix(base_color, grid_color, grid * 0.7);
    ROUGHNESS  = 0.82;
    METALLIC   = 0.0;

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
