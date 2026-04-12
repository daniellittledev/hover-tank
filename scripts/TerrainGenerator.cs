using Godot;
using System;

namespace HoverTank
{
    /// <summary>
    /// Generates moon-like cratered terrain entirely in C# at runtime.
    ///
    /// Strategy:
    ///   1. Load terrain/heightmap.png as a base height field. If the file is
    ///      missing, fall back to FastNoiseLite procedural noise so the project
    ///      works out-of-the-box without any assets.
    ///   2. Carve bowl-shaped craters with raised rims procedurally on top.
    ///   3. Build an ArrayMesh for visual rendering.
    ///   4. Build a HeightMapShape3D for efficient physics collision.
    ///
    /// Replace terrain/heightmap.png with any 8-bit grayscale PNG to customise
    /// the base terrain shape. The crater count and depth are tunable via
    /// exported properties.
    /// </summary>
    public partial class TerrainGenerator : Node3D
    {
        // ── Terrain dimensions ──────────────────────────────────────────────
        // Number of grid cells per side. Total vertices = (GridSize+1)^2.
        [Export] public int GridSize = 100;

        // World-space width and depth of each cell (metres).
        [Export] public float CellSize = 2.0f;

        // ── Heights ─────────────────────────────────────────────────────────
        // Multiplier applied to the normalised heightmap/noise values.
        [Export] public float HeightScale = 4.0f;

        // Noise seed — only used when the heightmap PNG is not found.
        [Export] public int NoiseSeed = 42;

        // ── Craters ─────────────────────────────────────────────────────────
        [Export] public int CraterCount = 25;
        [Export] public float CraterRadiusMin = 4f;
        [Export] public float CraterRadiusMax = 18f;
        [Export] public float CraterDepth = 2.5f;

        // ── Heightmap file ──────────────────────────────────────────────────
        [Export] public string HeightmapPath = "res://terrain/heightmap.png";

        public override void _Ready()
        {
            GenerateTerrain();
        }

        private void GenerateTerrain()
        {
            int verts = GridSize + 1;
            float worldSize = GridSize * CellSize;
            float origin = worldSize * 0.5f; // centre terrain on world origin

            // ── Step 1: build base height array ────────────────────────────
            float[,] heights = new float[verts, verts];

            var img = TryLoadHeightmapImage();
            if (img != null)
                SampleImageHeights(img, heights, verts);
            else
                GenerateNoiseHeights(heights, verts);

            // ── Step 2: carve craters ───────────────────────────────────────
            CarveCraters(heights, verts, origin);

            // ── Step 3: build mesh ──────────────────────────────────────────
            var mesh = BuildMesh(heights, verts, origin);

            var meshInst = new MeshInstance3D { Mesh = mesh };
            var mat = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.44f, 0.41f, 0.37f),
                Roughness = 0.95f,
                Metallic = 0.0f,
            };
            meshInst.SetSurfaceOverrideMaterial(0, mat);
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

        // ── Height source: PNG image ────────────────────────────────────────
        private Image? TryLoadHeightmapImage()
        {
            if (!FileAccess.FileExists(HeightmapPath)) return null;

            var img = Image.LoadFromFile(HeightmapPath);
            if (img == null || img.IsEmpty()) return null;

            img.Convert(Image.Format.L8); // ensure 8-bit grayscale
            return img;
        }

        private void SampleImageHeights(Image img, float[,] heights, int verts)
        {
            int imgW = img.GetWidth();
            int imgH = img.GetHeight();

            for (int z = 0; z < verts; z++)
            {
                for (int x = 0; x < verts; x++)
                {
                    // Bilinear sample from image to grid
                    float u = (float)x / (verts - 1) * (imgW - 1);
                    float v = (float)z / (verts - 1) * (imgH - 1);
                    int ix = (int)u;
                    int iz = (int)v;
                    float fu = u - ix;
                    float fv = v - iz;

                    int ix1 = Math.Min(ix + 1, imgW - 1);
                    int iz1 = Math.Min(iz + 1, imgH - 1);

                    float h00 = img.GetPixel(ix,  iz ).R;
                    float h10 = img.GetPixel(ix1, iz ).R;
                    float h01 = img.GetPixel(ix,  iz1).R;
                    float h11 = img.GetPixel(ix1, iz1).R;

                    float h = h00 * (1 - fu) * (1 - fv)
                            + h10 * fu       * (1 - fv)
                            + h01 * (1 - fu) * fv
                            + h11 * fu       * fv;

                    heights[x, z] = h * HeightScale;
                }
            }
        }

        private void GenerateNoiseHeights(float[,] heights, int verts)
        {
            var noise = new FastNoiseLite();
            noise.Seed = NoiseSeed;
            noise.Frequency = 0.025f;
            noise.FractalOctaves = 4;
            noise.FractalGain = 0.5f;
            noise.FractalLacunarity = 2.0f;

            for (int z = 0; z < verts; z++)
                for (int x = 0; x < verts; x++)
                    heights[x, z] = (noise.GetNoise2D(x, z) * 0.5f + 0.5f) * HeightScale;
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

            // Indices (two triangles per quad)
            int idx = 0;
            for (int z = 0; z < GridSize; z++)
            {
                for (int x = 0; x < GridSize; x++)
                {
                    int tl = z * verts + x;
                    int tr = tl + 1;
                    int bl = (z + 1) * verts + x;
                    int br = bl + 1;

                    indices[idx++] = tl; indices[idx++] = tr; indices[idx++] = bl;
                    indices[idx++] = tr; indices[idx++] = br; indices[idx++] = bl;
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
                    normals[zRow + x] = dx.Cross(dz).Normalized();
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
    }
}
