using Godot;
using Godot.Collections;
using System.Collections.Generic;

namespace HoverTank
{
    /// <summary>
    /// Builds a low-profile wedge hull ArrayMesh at runtime and assigns it to
    /// this MeshInstance3D.  Front of the tank is -Z (matches movement direction
    /// and barrel orientation).
    /// </summary>
    [Tool]
    public partial class TankMeshBuilder : MeshInstance3D
    {
        public override void _Ready()
        {
            Mesh = BuildHullMesh();
        }

        private static ArrayMesh BuildHullMesh()
        {
            // Hull silhouette in the XZ plane, listed counter-clockwise from
            // above.  Front (nose) points toward -Z.
            var profile = new Vector2[]
            {
                new( 0.00f, -1.50f), // nose tip
                new( 0.62f, -0.55f), // front-right shoulder
                new( 0.92f,  0.75f), // rear-right flare
                new( 0.42f,  1.44f), // rear-right corner
                new(-0.42f,  1.44f), // rear-left corner
                new(-0.92f,  0.75f), // rear-left flare
                new(-0.62f, -0.55f), // front-left shoulder
            };

            const float top    =  0.15f;
            const float bottom = -0.13f;

            var verts  = new List<Vector3>();
            var norms  = new List<Vector3>();
            var uvs    = new List<Vector2>();

            int n = profile.Length;

            // ── Top face — fan from centre ──────────────────────────────────
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                verts.Add(new Vector3(0f, top, 0f));
                verts.Add(new Vector3(profile[i].X, top, profile[i].Y));
                verts.Add(new Vector3(profile[j].X, top, profile[j].Y));
                for (int k = 0; k < 3; k++) norms.Add(Vector3.Up);
                uvs.Add(new Vector2(0.5f, 0.5f));
                uvs.Add(new Vector2(profile[i].X * 0.5f + 0.5f, profile[i].Y * 0.33f + 0.5f));
                uvs.Add(new Vector2(profile[j].X * 0.5f + 0.5f, profile[j].Y * 0.33f + 0.5f));
            }

            // ── Bottom face — reversed winding ──────────────────────────────
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                verts.Add(new Vector3(0f, bottom, 0f));
                verts.Add(new Vector3(profile[j].X, bottom, profile[j].Y));
                verts.Add(new Vector3(profile[i].X, bottom, profile[i].Y));
                for (int k = 0; k < 3; k++) norms.Add(Vector3.Down);
                uvs.Add(new Vector2(0.5f, 0.5f));
                uvs.Add(new Vector2(profile[j].X * 0.5f + 0.5f, profile[j].Y * 0.33f + 0.5f));
                uvs.Add(new Vector2(profile[i].X * 0.5f + 0.5f, profile[i].Y * 0.33f + 0.5f));
            }

            // ── Side faces — one quad per edge ─────────────────────────────
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                var a = new Vector3(profile[i].X, top,    profile[i].Y);
                var b = new Vector3(profile[j].X, top,    profile[j].Y);
                var c = new Vector3(profile[j].X, bottom, profile[j].Y);
                var d = new Vector3(profile[i].X, bottom, profile[i].Y);

                Vector3 faceNorm = (b - a).Cross(d - a).Normalized();
                // Ensure outward: compare with the horizontal midpoint direction
                var outward = new Vector3((a.X + b.X) * 0.5f, 0f, (a.Z + b.Z) * 0.5f);
                if (faceNorm.Dot(outward) < 0f) faceNorm = -faceNorm;

                verts.Add(a); verts.Add(b); verts.Add(c);
                verts.Add(a); verts.Add(c); verts.Add(d);
                for (int k = 0; k < 6; k++) norms.Add(faceNorm);
                uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0, 0));
            }

            var arrays = new Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
            arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
            arrays[(int)Mesh.ArrayType.TexUV]  = uvs.ToArray();

            var mesh = new ArrayMesh();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            return mesh;
        }
    }
}
