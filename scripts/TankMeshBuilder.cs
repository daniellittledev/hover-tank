using Godot;
using Godot.Collections;
using System.Collections.Generic;

namespace HoverTank
{
    /// <summary>
    /// Builds an angular stealth-fighter style hover tank hull at runtime.
    /// Two mesh surfaces: 0 = dark charcoal body, 1 = orange accent panels.
    /// Front of the tank is -Z (matches movement direction and barrel orientation).
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
            Tri(v, n, u, a, b, c);
            Tri(v, n, u, a, c, d);
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
            // Surface 0: dark charcoal body
            var dV = new List<Vector3>(); var dN = new List<Vector3>(); var dU = new List<Vector2>();
            // Surface 1: orange accent panels
            var oV = new List<Vector3>(); var oN = new List<Vector3>(); var oU = new List<Vector2>();

            BuildHull(dV, dN, dU, oV, oN, oU);
            BuildCockpit(dV, dN, dU);

            var mesh = new ArrayMesh();
            AddSurface(mesh, dV, dN, dU);
            AddSurface(mesh, oV, oN, oU);
            return mesh;
        }

        // Shoulder position as fraction of half-width from centre.
        private const float ShoulderFrac = 0.38f;

        private static void BuildHull(
            List<Vector3> dV, List<Vector3> dN, List<Vector2> dU,
            List<Vector3> oV, List<Vector3> oN, List<Vector2> oU)
        {
            // Cross-section ribs along Z axis (-Z = forward).
            //   z: position,  hw: half-width,  ey: edge height,
            //   ry: ridge height,  by: bottom height
            float[] z  = { -1.75f, -1.20f, -0.50f,  0.00f,  0.30f,  0.80f,  1.25f,  1.60f };
            float[] hw = {  0.00f,  0.40f,  0.80f,  1.20f,  1.55f,  1.35f,  0.70f,  0.45f };
            float[] ey = {  0.08f,  0.04f,  0.02f,  0.02f,  0.00f,  0.02f,  0.04f,  0.06f };
            float[] ry = {  0.08f,  0.16f,  0.20f,  0.20f,  0.20f,  0.20f,  0.18f,  0.14f };
            float[] by = { -0.06f, -0.14f, -0.14f, -0.14f, -0.14f, -0.14f, -0.14f, -0.14f };

            int count = z.Length;

            for (int i = 0; i < count - 1; i++)
            {
                int j = i + 1;

                // Orange outer panels on wing sections (skip nose)
                bool accent = hw[i] >= 0.001f;

                if (hw[i] < 0.001f)
                {
                    // ── Nose: rib i is a single point ───────────────────────
                    var tip  = new Vector3(0, ry[i], z[i]);
                    var tipB = new Vector3(0, by[i], z[i]);

                    float sw1 = hw[j] * ShoulderFrac;
                    float sy1 = Mathf.Lerp(ry[j], ey[j], 0.1f);

                    var le1 = new Vector3(-hw[j], ey[j], z[j]);
                    var ls1 = new Vector3(-sw1,   sy1,   z[j]);
                    var c1  = new Vector3(0,      ry[j], z[j]);
                    var rs1 = new Vector3(sw1,    sy1,   z[j]);
                    var re1 = new Vector3(hw[j],  ey[j], z[j]);
                    var lb1 = new Vector3(-hw[j], by[j], z[j]);
                    var rb1 = new Vector3(hw[j],  by[j], z[j]);

                    // Top: 4 triangles (outer = dark for nose)
                    Tri(dV, dN, dU, tip, le1, ls1);  // left outer
                    Tri(dV, dN, dU, tip, ls1, c1);   // left inner
                    Tri(dV, dN, dU, tip, c1,  rs1);  // right inner
                    Tri(dV, dN, dU, tip, rs1, re1);  // right outer

                    // Sides
                    Quad(dV, dN, dU, tip, tipB, lb1, le1);  // left
                    Quad(dV, dN, dU, tip, re1, rb1, tipB);  // right

                    // Bottom
                    Tri(dV, dN, dU, tipB, rb1, lb1);
                }
                else
                {
                    // ── Regular section ─────────────────────────────────────
                    float sw0 = hw[i] * ShoulderFrac;
                    float sy0 = Mathf.Lerp(ry[i], ey[i], 0.1f);
                    float sw1 = hw[j] * ShoulderFrac;
                    float sy1 = Mathf.Lerp(ry[j], ey[j], 0.1f);

                    var le0 = new Vector3(-hw[i], ey[i], z[i]);
                    var ls0 = new Vector3(-sw0,   sy0,   z[i]);
                    var c0  = new Vector3(0,      ry[i], z[i]);
                    var rs0 = new Vector3(sw0,    sy0,   z[i]);
                    var re0 = new Vector3(hw[i],  ey[i], z[i]);
                    var lb0 = new Vector3(-hw[i], by[i], z[i]);
                    var rb0 = new Vector3(hw[i],  by[i], z[i]);

                    var le1 = new Vector3(-hw[j], ey[j], z[j]);
                    var ls1 = new Vector3(-sw1,   sy1,   z[j]);
                    var c1  = new Vector3(0,      ry[j], z[j]);
                    var rs1 = new Vector3(sw1,    sy1,   z[j]);
                    var re1 = new Vector3(hw[j],  ey[j], z[j]);
                    var lb1 = new Vector3(-hw[j], by[j], z[j]);
                    var rb1 = new Vector3(hw[j],  by[j], z[j]);

                    // Top — outer panels (orange in accent zone)
                    var aV = accent ? oV : dV;
                    var aN = accent ? oN : dN;
                    var aU = accent ? oU : dU;
                    Quad(aV, aN, aU, ls0, le0, le1, ls1);  // left outer
                    Quad(aV, aN, aU, re0, rs0, rs1, re1);  // right outer

                    // Top — inner panels (always dark)
                    Quad(dV, dN, dU, c0, ls0, ls1, c1);    // left inner
                    Quad(dV, dN, dU, rs0, c0, c1, rs1);    // right inner

                    // Sides
                    Quad(dV, dN, dU, le0, lb0, lb1, le1);  // left
                    Quad(dV, dN, dU, re0, re1, rb1, rb0);  // right

                    // Bottom
                    Quad(dV, dN, dU, lb0, rb0, rb1, lb1);
                }
            }

            // ── Rear cap ────────────────────────────────────────────────────
            int last = count - 1;
            if (hw[last] > 0.001f)
            {
                var rc  = new Vector3(0,         ry[last], z[last]);
                var rle = new Vector3(-hw[last], ey[last], z[last]);
                var rre = new Vector3(hw[last],  ey[last], z[last]);
                var rlb = new Vector3(-hw[last], by[last], z[last]);
                var rrb = new Vector3(hw[last],  by[last], z[last]);

                Tri(dV, dN, dU, rc, rle, rre);           // upper
                Quad(dV, dN, dU, rle, rlb, rrb, rre);    // lower
            }
        }

        private static void BuildCockpit(List<Vector3> v, List<Vector3> n, List<Vector2> u)
        {
            const float baseY = 0.20f;

            // Front cross-section (z = -0.15)
            var fl = new Vector3(-0.22f, baseY, -0.15f);
            var ft = new Vector3( 0f,    0.34f, -0.15f);
            var fr = new Vector3( 0.22f, baseY, -0.15f);

            // Peak cross-section (z = 0.30)
            var ml = new Vector3(-0.32f, baseY, 0.30f);
            var mt = new Vector3( 0f,    0.44f, 0.30f);
            var mr = new Vector3( 0.32f, baseY, 0.30f);

            // Rear cross-section (z = 0.85)
            var rl = new Vector3(-0.25f, baseY, 0.85f);
            var rt = new Vector3( 0f,    0.38f, 0.85f);
            var rr = new Vector3( 0.25f, baseY, 0.85f);

            // Front cap
            Tri(v, n, u, fl, ft, fr);

            // Left roof: front → peak
            Quad(v, n, u, ft, fl, ml, mt);
            // Right roof: front → peak
            Quad(v, n, u, fr, ft, mt, mr);

            // Left roof: peak → rear
            Quad(v, n, u, mt, ml, rl, rt);
            // Right roof: peak → rear
            Quad(v, n, u, mr, mt, rt, rr);

            // Rear cap
            Tri(v, n, u, rr, rt, rl);
        }
    }
}
