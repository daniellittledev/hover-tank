using Godot;
using System.Collections.Generic;

namespace HoverTank
{
	/// <summary>
	/// Builds the Proto-Sabre hover-tank base platform at runtime from the build
	/// spec (docs): one 2D silhouette polygon (<see cref="Base"/>, a symmetric
	/// 10-gon = union of a core hex + a wider rear "fan" hex) stamped as four
	/// horizontal rings at increasing height and bridged into walls, then capped.
	/// No per-face modelling — the polygon is the single source of truth and its
	/// mirror symmetry is baked in, so the hull is always left/right symmetric.
	///
	/// Faceted hard-surface look: non-indexed triangles with per-face flat normals,
	/// double-sided material (winding never matters). A separate emissive quad on
	/// the rear face is the engine glow.
	///
	/// Axis note: the spec uses +Z = nose, but this project moves along -Z, so
	/// <see cref="ToWorld"/> reflects Z (nose → -Z) and applies a single SCALE.
	/// </summary>
	[Tool]
	public partial class TankMeshBuilder : MeshInstance3D
	{
		private const float Scale = 2.6f;   // normalised units → scene units
		private const float YOff  = 0.0f;   // vertical placement on the body
		private const float Cz    = 0.4236f; // silhouette centroid Z (X centroid = 0)

		// BASE silhouette, ordered right side nose→rear then mirrored up the left.
		// Stored as (X, Z); see spec §2.
		private static readonly Vector2[] Base =
		{
			new( 0.1517f, 1.0000f),  // 0 nose R
			new( 0.3833f, 0.5000f),  // 1 core R (widest)
			new( 0.3321f, 0.3890f),  // 2 notch R (concave)
			new( 0.4983f, 0.2292f),  // 3 fan R (widest)
			new( 0.2600f, 0.0000f),  // 4 rear R
			new(-0.2600f, 0.0000f),  // 5 rear L
			new(-0.4983f, 0.2292f),  // 6 fan L
			new(-0.3321f, 0.3890f),  // 7 notch L
			new(-0.3833f, 0.5000f),  // 8 core L
			new(-0.1517f, 1.0000f),  // 9 nose L
		};

		public override void _Ready()
		{
			Mesh = BuildHull();
		}

		// Spec-space (normalised, +Z forward) → scene-space (SCALE'd, -Z forward).
		// Forward is flipped by a 180° rotation about Y (negate X and Z about the
		// centroid) — a proper rotation, so triangle winding is preserved (a plain
		// Z-reflection would invert it and flip every normal). X-symmetry makes the
		// X negation invisible.
		private static Vector3 ToWorld(float x, float y, float z) =>
			new(-Scale * x, Scale * y + YOff, Scale * (Cz - z));

		// One BASE ring: scale about the centroid, lift to y, optionally shift in Z.
		private static Vector3[] Ring(float scale, float y, float centerZ = float.NaN)
		{
			float shiftZ = float.IsNaN(centerZ) ? 0f : centerZ - Cz;
			var r = new Vector3[Base.Length];
			for (int i = 0; i < Base.Length; i++)
			{
				float sx = scale * Base[i].X;                 // X centroid = 0
				float sz = Cz + scale * (Base[i].Y - Cz) + shiftZ;
				r[i] = ToWorld(sx, y, sz);
			}
			return r;
		}

		private static ArrayMesh BuildHull()
		{
			var verts = new List<Vector3>();
			var norms = new List<Vector3>();

			// Four layered rings (spec §3).
			Vector3[] bottom   = Ring(0.88f, -0.055f);            // belly (down + shrunk)
			Vector3[] baseRing = Ring(1.00f,  0.000f);            // reference plane
			Vector3[] rim      = Ring(1.00f,  0.085f);            // vertical rim band
			Vector3[] platform = Ring(0.42f,  0.210f, 0.230f);    // cannon mount deck

			Bridge(verts, norms, bottom, baseRing, vBias: -1.0f, radialW: 0.6f); // belly bevel (down+out)
			Bridge(verts, norms, baseRing, rim, vBias: 0f, radialW: 1.0f);      // rim band (radial wall)
			Bridge(verts, norms, rim, platform, vBias: 1.0f, radialW: 0f);      // deck ramp (up)
			Cap(verts, norms, bottom, vBias: -1.0f);   // underside (down)
			Cap(verts, norms, platform, vBias: 1.0f);  // platform top (up)

			var mesh = new ArrayMesh();

			var hull = new Godot.Collections.Array();
			hull.Resize((int)Mesh.ArrayType.Max);
			hull[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
			hull[(int)Mesh.ArrayType.Normal] = norms.ToArray();
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, hull);
			mesh.SurfaceSetMaterial(0, HullMaterial());

			// Engine-glow quad on the rear face of the rim band (spec §5).
			var glow = BuildGlowQuad();
			mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, glow);
			mesh.SurfaceSetMaterial(1, GlowMaterial());

			return mesh;
		}

		private static Godot.Collections.Array BuildGlowQuad()
		{
			const float gz = -0.004f;   // rear, inset just behind the hull
			const float y0 = 0.012f, y1 = 0.070f, hw = 0.190f;
			Vector3 a = ToWorld(-hw, y0, gz);
			Vector3 b = ToWorld( hw, y0, gz);
			Vector3 c = ToWorld( hw, y1, gz);
			Vector3 d = ToWorld(-hw, y1, gz);

			var v = new Vector3[] { a, b, c, a, c, d };
			var arr = new Godot.Collections.Array();
			arr.Resize((int)Mesh.ArrayType.Max);
			arr[(int)Mesh.ArrayType.Vertex] = v;
			return arr;
		}

		// Bridge two equal-count closed rings into a wall of quads (two tris each).
		// vBias/radialW describe which way "outward" points for this band (up/down
		// + away-from-axis), so each face is oriented front-out reliably.
		private static void Bridge(List<Vector3> v, List<Vector3> n, Vector3[] r0, Vector3[] r1,
			float vBias, float radialW)
		{
			int count = r0.Length;
			for (int k = 0; k < count; k++)
			{
				int k1 = (k + 1) % count;
				AddTri(v, n, r0[k], r0[k1], r1[k1], vBias, radialW);
				AddTri(v, n, r0[k], r1[k1], r1[k], vBias, radialW);
			}
		}

		// Fan-cap a ring to its centroid. vBias = ±1 (up cap / down cap).
		private static void Cap(List<Vector3> v, List<Vector3> n, Vector3[] r, float vBias)
		{
			Vector3 c = Vector3.Zero;
			foreach (var p in r) c += p;
			c /= r.Length;
			for (int k = 0; k < r.Length; k++)
			{
				Vector3 b0 = r[k], b1 = r[(k + 1) % r.Length];
				AddTri(v, n, c, b0, b1, vBias, 0f);
			}
		}

		// Append a triangle with a single flat normal. Builds an "outward hint" from
		// the face's radial direction (away from the hull's vertical axis at x=z=0)
		// plus a vertical bias, stores the outward normal for lighting, and emits the
		// winding so the outward side is the front face under single-sided culling.
		// Godot's front face is clockwise *as seen from outside* — i.e. the winding's
		// right-hand normal points inward — so we emit the order whose RH normal is
		// the inward (-outward) direction.
		private static void AddTri(List<Vector3> v, List<Vector3> n,
			Vector3 a, Vector3 b, Vector3 c, float vBias, float radialW)
		{
			Vector3 gn = (b - a).Cross(c - a);
			if (gn.LengthSquared() < 1e-12f) return;
			gn = gn.Normalized();

			Vector3 fc = (a + b + c) / 3f;
			Vector3 radial = new Vector3(fc.X, 0f, fc.Z);
			if (radial.LengthSquared() > 1e-6f) radial = radial.Normalized();
			Vector3 hint = radial * radialW + new Vector3(0f, vBias, 0f);

			Vector3 outN = gn.Dot(hint) >= 0f ? gn : -gn;   // outward, for lighting

			if (gn.Dot(outN) > 0f)            // (a,b,c) RH normal points outward → reverse
			{
				v.Add(a); v.Add(c); v.Add(b);
			}
			else
			{
				v.Add(a); v.Add(b); v.Add(c);
			}
			n.Add(outN); n.Add(outN); n.Add(outN);
		}

		// Matte dark hull. Double-sided so spec winding never produces black faces;
		// flat per-face normals give the faceted hard-surface read.
		// Light, shiny hull: metallic with moderate roughness so it picks up a soft,
		// blurred reflection of the sky (not a mirror). Single-sided — the hull
		// winding is oriented outward, so no double-sided crutch is needed. Flat
		// per-face normals keep the faceted hard-surface read, so each panel
		// reflects the sky as its own plane.
		private static StandardMaterial3D HullMaterial() => new()
		{
			AlbedoColor = new Color(0.78f, 0.82f, 0.90f),
			Metallic = 0.5f,
			Roughness = 0.30f,
		};

		// Unlit cyan engine bank.
		private static StandardMaterial3D GlowMaterial() => new()
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0.20f, 0.80f, 1.00f),
			EmissionEnabled = true,
			Emission = new Color(0.20f, 0.80f, 1.00f),
			EmissionEnergyMultiplier = 4.0f,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
		};
	}
}
