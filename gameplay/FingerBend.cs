using System.Collections.Generic;
using Godot;
using Godot.Collections;

namespace Gamejam.Gameplay;

// Gives the explorer's fist (art/explorer_arm.glb, Arm_Hand) an "open" blend shape, so HeldItemView can open the
// fingers while a torch is in the air. The model has no finger bones, but each finger is built from separate parts
// curled around the grip's Y axis, so they can be uncurled in code: each finger part is bent straighter along its
// length, keeping its length, around the knuckle.
public static class FingerBend
{
	// Where the knuckles are, as an angle around the grip axis (in the X / -Z plane), and the finger's curl radius.
	private const float KnuckleAngle = -0.35f;
	private const float CurlRadius = 0.025f;
	// How far the shape opens the fingers (1 would be dead straight).
	private const float OpenAmount = 0.85f;
	// Finger rows (one per finger), by height along the grip.
	private static readonly float[] RowHeights = { 0.030f, 0.0097f, -0.0105f, -0.031f };
	// Pieces found on the current model: per finger 3 segments plus knuckle, 2 joint and fingertip spheres, except
	// the little finger, whose middle joints are fused to its segments (3 * 7 + 5).
	private const int ExpectedParts = 26;

	// Replaces hand's mesh with a copy that has the open shape as blend shape 0. False if the hand doesn't look like
	// the expected model (then nothing changes and the fingers just stay closed).
	public static bool AddOpenShape(MeshInstance3D hand)
	{
		if (hand?.Mesh is not ArrayMesh source || source.GetSurfaceCount() != 1 || source.GetBlendShapeCount() > 0)
			return false;

		Array arrays = source.SurfaceGetArrays(0);
		Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();
		if (vertices.Length == 0 || indices.Length == 0)
			return false;

		bool[] finger = FingerVertices(vertices, indices, out int parts);
		if (parts != ExpectedParts)
		{
			GD.PushWarning($"FingerBend: found {parts} finger parts on {hand.Name}, expected {ExpectedParts}; fingers won't open.");
			return false;
		}

		Vector3[] normals = arrays[(int)Mesh.ArrayType.Normal].AsVector3Array();
		float[] tangents = arrays[(int)Mesh.ArrayType.Tangent].AsFloat32Array();
		var openVertices = (Vector3[])vertices.Clone();
		var openNormals = (Vector3[])normals.Clone();
		var openTangents = (float[])tangents.Clone();
		for (int i = 0; i < vertices.Length; i++)
		{
			if (!finger[i])
				continue;
			openVertices[i] = Bend(vertices[i], out float turn);
			if (normals.Length > i)
				openNormals[i] = normals[i].Rotated(Vector3.Up, turn);
			if (tangents.Length >= (i + 1) * 4)
			{
				var t = new Vector3(tangents[i * 4], tangents[i * 4 + 1], tangents[i * 4 + 2]).Rotated(Vector3.Up, turn);
				openTangents[i * 4] = t.X;
				openTangents[i * 4 + 1] = t.Y;
				openTangents[i * 4 + 2] = t.Z;
			}
		}

		var blend = new Array();
		blend.Resize((int)Mesh.ArrayType.Max);
		blend[(int)Mesh.ArrayType.Vertex] = openVertices;
		if (normals.Length > 0)
			blend[(int)Mesh.ArrayType.Normal] = openNormals;
		if (tangents.Length > 0)
			blend[(int)Mesh.ArrayType.Tangent] = openTangents;

		var mesh = new ArrayMesh { BlendShapeMode = Mesh.BlendShapeMode.Normalized };
		mesh.AddBlendShape("open");
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, new Array<Array> { blend });
		mesh.SurfaceSetMaterial(0, source.SurfaceGetMaterial(0));
		hand.Mesh = mesh;
		return true;
	}

	// A bend deformer: the finger lies along a circle of CurlRadius from the knuckle; opening it puts it on a wider
	// circle through the same knuckle, keeping each point's distance along the finger and its depth below it.
	private static Vector3 Bend(Vector3 v, out float turn)
	{
		var p = new Vector2(v.X, -v.Z);
		float radius = p.Length();
		float along = Mathf.PosMod(p.Angle() - KnuckleAngle + Mathf.Pi / 2f, Mathf.Tau) - Mathf.Pi / 2f;
		float depth = CurlRadius - radius;

		float openRadius = CurlRadius / (1f - OpenAmount);
		Vector2 knuckleDir = Vector2.FromAngle(KnuckleAngle);
		Vector2 centre = knuckleDir * (CurlRadius - openRadius);
		float angle = along * CurlRadius / openRadius;
		Vector2 bent = centre + knuckleDir.Rotated(angle) * (openRadius - depth);

		turn = angle - along;
		return new Vector3(bent.X, v.Y, -bent.Y);
	}

	// Marks the vertices of the finger parts: the separate pieces (joined by shared triangles or shared positions)
	// that are small and lie in one of the finger rows. The thumb's pieces are bigger, sit between rows, or (for its
	// joint spheres) are larger than any finger joint.
	private static bool[] FingerVertices(Vector3[] vertices, int[] indices, out int parts)
	{
		var root = new int[vertices.Length];
		for (int i = 0; i < root.Length; i++)
			root[i] = i;
		int Find(int x)
		{
			while (root[x] != x)
				x = root[x] = root[root[x]];
			return x;
		}
		void Union(int a, int b) => root[Find(a)] = Find(b);

		for (int i = 0; i + 2 < indices.Length; i += 3)
		{
			Union(indices[i], indices[i + 1]);
			Union(indices[i], indices[i + 2]);
		}
		var byPosition = new System.Collections.Generic.Dictionary<Vector3, int>();
		for (int i = 0; i < vertices.Length; i++)
		{
			if (byPosition.TryGetValue(vertices[i], out int other))
				Union(i, other);
			else
				byPosition[vertices[i]] = i;
		}

		var bounds = new System.Collections.Generic.Dictionary<int, Aabb>();
		for (int i = 0; i < vertices.Length; i++)
		{
			int r = Find(i);
			bounds[r] = bounds.TryGetValue(r, out Aabb box) ? box.Expand(vertices[i]) : new Aabb(vertices[i], Vector3.Zero);
		}

		var isFinger = new HashSet<int>();
		foreach ((int r, Aabb box) in bounds)
		{
			Vector3 size = box.Size;
			Vector3 centre = box.GetCenter();
			bool small = size.Y < 0.025f && Mathf.Max(size.X, size.Z) < 0.05f;
			bool inRow = false;
			foreach (float row in RowHeights)
				inRow |= Mathf.Abs(centre.Y - row) < 0.004f;
			bool sphere = Mathf.Abs(size.X - size.Y) < 0.0005f && Mathf.Abs(size.Z - size.Y) < 0.0005f;
			bool thumbJoint = sphere && size.Y > 0.0185f && centre.X < -0.01f;
			if (small && inRow && !thumbJoint)
				isFinger.Add(r);
		}

		parts = isFinger.Count;
		var result = new bool[vertices.Length];
		for (int i = 0; i < vertices.Length; i++)
			result[i] = isFinger.Contains(Find(i));
		return result;
	}
}
