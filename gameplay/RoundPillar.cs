using Godot;
using Godot.Collections;

namespace Gamejam.Gameplay;

// Turns square pillars from a level model into round ones: each source mesh (a box standing along its own Y, like
// art/level1.glb's Pillar_1) is hidden, its collider switched off, and a cylinder of the same height and width put in
// its place, with the pillar's painted material wrapped round it (the paint repeats once per side the box had) and its
// stone on the ends. Works for broken and fallen pieces too, since each keeps its own transform.
public partial class RoundPillar : Node
{
	[Export] public Array<MeshInstance3D> Sources { get; set; } = new();
	// How much of the box's width the cylinder fills (1 = touching the box's sides).
	[Export] public float WidthFill { get; set; } = 0.96f;
	[Export] public int Segments { get; set; } = 28;

	public override void _Ready()
	{
		foreach (MeshInstance3D source in Sources)
		{
			if (source?.Mesh != null)
				Replace(source);
		}
	}

	private void Replace(MeshInstance3D source)
	{
		Aabb box = source.GetAabb();
		float radius = Mathf.Min(box.Size.X, box.Size.Z) * 0.5f * WidthFill;
		float bottom = box.Position.Y;
		float top = box.End.Y;
		Vector3 centre = box.GetCenter();
		int surfaces = source.Mesh.GetSurfaceCount();
		Material ends = source.GetActiveMaterial(0);
		Material sides = surfaces > 1 ? source.GetActiveMaterial(1) : ends;

		var round = new MeshInstance3D { Name = source.Name + "_Round", Mesh = Cylinder(radius, bottom, top, centre, sides, ends) };
		source.GetParent().AddChild(round);
		round.Transform = source.Transform;

		var body = new StaticBody3D { Name = "StaticBody3D", CollisionLayer = Core.Layers.World, CollisionMask = 0 };
		body.AddChild(new CollisionShape3D
		{
			Shape = new CylinderShape3D { Radius = radius, Height = top - bottom },
			Position = new Vector3(centre.X, (bottom + top) * 0.5f, centre.Z),
		});
		round.AddChild(body);

		source.Visible = false;
		foreach (Node child in source.GetChildren())
		{
			if (child is CollisionObject3D collider)
			{
				collider.CollisionLayer = 0;
				collider.CollisionMask = 0;
			}
		}
	}

	// A cylinder along Y: the side mapped like the box's faces (u four times round, v top to bottom), and flat caps.
	private ArrayMesh Cylinder(float radius, float bottom, float top, Vector3 centre, Material sides, Material ends)
	{
		var side = new SurfaceTool();
		side.Begin(Mesh.PrimitiveType.Triangles);
		for (int i = 0; i < Segments; i++)
		{
			float a0 = Mathf.Tau * i / Segments;
			float a1 = Mathf.Tau * (i + 1) / Segments;
			Vector3 d0 = new(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
			Vector3 d1 = new(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
			float u0 = 4f * i / Segments;
			float u1 = 4f * (i + 1) / Segments;
			Vector3 b0 = new Vector3(centre.X, bottom, centre.Z) + d0 * radius;
			Vector3 b1 = new Vector3(centre.X, bottom, centre.Z) + d1 * radius;
			Vector3 t0 = new Vector3(centre.X, top, centre.Z) + d0 * radius;
			Vector3 t1 = new Vector3(centre.X, top, centre.Z) + d1 * radius;
			// Outward faces wind clockwise seen from outside.
			AddVertex(side, t0, d0, new Vector2(u0, 0f));
			AddVertex(side, b1, d1, new Vector2(u1, 1f));
			AddVertex(side, b0, d0, new Vector2(u0, 1f));
			AddVertex(side, t0, d0, new Vector2(u0, 0f));
			AddVertex(side, t1, d1, new Vector2(u1, 0f));
			AddVertex(side, b1, d1, new Vector2(u1, 1f));
		}
		side.SetMaterial(sides);

		var cap = new SurfaceTool();
		cap.Begin(Mesh.PrimitiveType.Triangles);
		foreach ((float y, Vector3 normal) in new[] { (top, Vector3.Up), (bottom, Vector3.Down) })
		{
			var middle = new Vector3(centre.X, y, centre.Z);
			for (int i = 0; i < Segments; i++)
			{
				float a0 = Mathf.Tau * i / Segments;
				float a1 = Mathf.Tau * (i + 1) / Segments;
				Vector3 p0 = middle + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
				Vector3 p1 = middle + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
				Vector2 Uv(Vector3 p) => new(0.5f + (p.X - middle.X) / (radius * 2f), 0.5f + (p.Z - middle.Z) / (radius * 2f));
				if (normal.Y > 0f)
				{
					AddVertex(cap, middle, normal, Uv(middle));
					AddVertex(cap, p0, normal, Uv(p0));
					AddVertex(cap, p1, normal, Uv(p1));
				}
				else
				{
					AddVertex(cap, middle, normal, Uv(middle));
					AddVertex(cap, p1, normal, Uv(p1));
					AddVertex(cap, p0, normal, Uv(p0));
				}
			}
		}
		cap.SetMaterial(ends);

		var mesh = side.Commit();
		cap.Commit(mesh);
		return mesh;
	}

	private static void AddVertex(SurfaceTool tool, Vector3 position, Vector3 normal, Vector2 uv)
	{
		tool.SetNormal(normal);
		tool.SetUV(uv);
		tool.AddVertex(position);
	}
}
