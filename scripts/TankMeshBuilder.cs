using Godot;
using Godot.Collections;
using System.Collections.Generic;

namespace HoverTank
{
    /// <summary>
    /// Builds the hover tank hull at runtime as a long trapezoidal prism:
    /// narrow + low at the front (-Z), wide + tall at the back (+Z), flat bottom.
    /// Single dark charcoal surface (0). Front of the tank is -Z (matches
    /// movement direction and barrel orientation).
    /// </summary>
    [Tool]
    public partial class TankMeshBuilder : MeshInstance3D
    {
        public override void _Ready()
        {
            Mesh = BuildHullMesh();
        }

        private static void Tri(List<Vector3> v, List<Vector3> n, List<Vector2> u,
            Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 norm = (b - a).Cross(c - a).Normalized();
            v.Add(a); v.Add(b); v.Add(c);
            n.Add(norm); n.Add(norm); n.Add(norm);
            u.Add(Uv(a)); u.Add(Uv(b)); u.Add(Uv(c));
        }

        private static void Quad(List<Vector3> v, List<Vector3> n, List<Vector2> u,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            Tri(v, n, u, a, c, b);
            Tri(v, n, u, a, d, c);
        }

        private static Vector2 Uv(Vector3 p) =>
            new(p.X * 0.2f + 0.5f, p.Z * 0.15f + 0.5f);

        private static void AddSurface(ArrayMesh mesh,
            List<Vector3> v, List<Vector3> n, List<Vector2> u)
        {
            if (v.Count == 0) return;
            var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = v.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = n.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV]  = u.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }

        private static ArrayMesh BuildHullMesh()
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>();
            BuildTrapezoid(v, n, u);

            var mesh = new ArrayMesh();
            AddSurface(mesh, v, n, u);
            return mesh;
        }

        private static void BuildTrapezoid(List<Vector3> v, List<Vector3> n, List<Vector2> u)
        {
            // Long trapezoidal prism. -Z = forward.
            const float zFront = -1.70f, zBack = 1.60f;   // length along Z
            const float hwFront = 0.55f, hwBack = 1.20f;  // half-width (back wider)
            const float botY = -0.14f;                    // flat bottom
            const float topFront = 0.10f, topBack = 0.46f; // top higher at back

            // Front rib
            var FBL = new Vector3(-hwFront, botY,     zFront);
            var FBR = new Vector3( hwFront, botY,     zFront);
            var FTL = new Vector3(-hwFront, topFront, zFront);
            var FTR = new Vector3( hwFront, topFront, zFront);
            // Back rib
            var BBL = new Vector3(-hwBack, botY,    zBack);
            var BBR = new Vector3( hwBack, botY,    zBack);
            var BTL = new Vector3(-hwBack, topBack, zBack);
            var BTR = new Vector3( hwBack, topBack, zBack);

            Quad(v, n, u, FBL, FTL, FTR, FBR);  // front  (-Z)
            Quad(v, n, u, BBR, BTR, BTL, BBL);  // back   (+Z)
            Quad(v, n, u, FTL, BTL, BTR, FTR);  // top    (sloped)
            Quad(v, n, u, FBL, FBR, BBR, BBL);  // bottom (-Y)
            Quad(v, n, u, FTL, FBL, BBL, BTL);  // left   (-X)
            Quad(v, n, u, FTR, BTR, BBR, FBR);  // right  (+X)
        }
    }
}
