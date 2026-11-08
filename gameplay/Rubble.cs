using System.Collections.Generic;
using Godot;

namespace Gamejam.Gameplay;

// Broken stone, made in code: irregular chipped rocks (for falling debris and rubble heaps) and the dusty look that
// goes with them. Used by the ceiling collapse and the lose screen.
public static class Rubble
{
	// An irregular rock about `size` across: a low-poly ball squashed to size, its corners pushed in and out at random
	// and sliced flat on a side or two, so it reads as a chipped lump of stone. Flat shaded. Also returns its corners,
	// for a matching convex collider.
	public static (ArrayMesh Mesh, Vector3[] Hull) Rock(Vector3 size, RandomNumberGenerator rng)
	{
		var ball = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 7, Rings = 4 };
		Godot.Collections.Array arrays = ball.GetMeshArrays();
		Vector3[] vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
		int[] indices = arrays[(int)Mesh.ArrayType.Index].AsInt32Array();

		// Up to two flat breaks, each a plane cutting off part of the ball.
		var cuts = new List<(Vector3 Normal, float Depth)>();
		int cutCount = rng.RandiRange(1, 2);
		for (int i = 0; i < cutCount; i++)
		{
			var normal = new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f), rng.RandfRange(-1f, 1f)).Normalized();
			cuts.Add((normal, rng.RandfRange(0.22f, 0.38f)));
		}

		// The same corner appears more than once along the ball's seams, so each position is moved only once.
		var moved = new Dictionary<Vector3, Vector3>();
		var hull = new List<Vector3>();
		Vector3 Shape(Vector3 v)
		{
			if (moved.TryGetValue(v, out Vector3 done))
				return done;
			Vector3 p = v * rng.RandfRange(0.78f, 1.12f);
			foreach ((Vector3 normal, float depth) in cuts)
			{
				float over = p.Dot(normal) - depth;
				if (over > 0f)
					p -= normal * over;
			}
			p *= size;
			moved[v] = p;
			hull.Add(p);
			return p;
		}

		var tool = new SurfaceTool();
		tool.Begin(Mesh.PrimitiveType.Triangles);
		// Every face keeps its own normal, for hard chipped edges.
		tool.SetSmoothGroup(uint.MaxValue);
		foreach (int index in indices)
			tool.AddVertex(Shape(vertices[index]));
		tool.GenerateNormals();
		return (tool.Commit(), hull.ToArray());
	}

	// A few versions of the stone material, each a slightly different shade, with the texture wrapped round each rock
	// (triplanar) instead of stretched. Darker ones read as dust-covered.
	public static Material[] Shades(Material stone, RandomNumberGenerator rng, int count = 4)
	{
		var shades = new Material[count];
		for (int i = 0; i < count; i++)
		{
			float shade = Mathf.Lerp(0.62f, 1f, count == 1 ? 1f : i / (float)(count - 1));
			var warm = new Color(1f, rng.RandfRange(0.94f, 1f), rng.RandfRange(0.86f, 0.96f));
			if (stone is BaseMaterial3D source)
			{
				var copy = (BaseMaterial3D)source.Duplicate();
				copy.Uv1Triplanar = true;
				copy.Uv1Scale = Vector3.One * 1.6f;
				copy.AlbedoColor = source.AlbedoColor * warm * new Color(shade, shade, shade);
				copy.Roughness = 0.95f;
				copy.Metallic = 0f;
				shades[i] = copy;
			}
			else
				shades[i] = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.47f, 0.37f) * shade, Roughness = 0.95f };
		}
		return shades;
	}

	// A still (no physics) rock, for heaps and scattered debris.
	public static MeshInstance3D StillRock(Vector3 size, Material material, RandomNumberGenerator rng)
	{
		return new MeshInstance3D
		{
			Mesh = Rock(size, rng).Mesh,
			MaterialOverride = material,
			Rotation = new Vector3(rng.RandfRange(-0.5f, 0.5f), rng.RandfRange(-3.1f, 3.1f), rng.RandfRange(-0.5f, 0.5f)),
		};
	}

	// A soft puff texture for dust particles.
	public static StandardMaterial3D DustMaterial()
	{
		var puff = new GradientTexture2D
		{
			Gradient = new Gradient { Offsets = new[] { 0f, 1f }, Colors = new[] { Colors.White, new Color(1f, 1f, 1f, 0f) } },
			Fill = GradientTexture2D.FillEnum.Radial,
			FillFrom = new Vector2(0.5f, 0.5f),
			FillTo = new Vector2(0.5f, 0f),
			Width = 64,
			Height = 64,
		};
		return new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
			BillboardKeepScale = true,
			VertexColorUseAsAlbedo = true,
			AlbedoTexture = puff,
		};
	}
}
