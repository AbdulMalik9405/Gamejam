using System.Collections.Generic;
using Godot;

namespace Gamejam.Gameplay;

// A Pickup that looks like one of the trinkets in a model file (art/treasure.glb: Trinket_Ankh, Trinket_Scarab...).
// The trinket is turned by ModelRotationDegrees, then set so the middle of its base is at this node's origin (so it
// stands on whatever slot or floor it's put on), and gets a box collider fitted to it.
public partial class TreasurePickup : Pickup
{
	[Export] public PackedScene Model { get; set; }
	// The trinket's mesh node in Model.
	[Export] public string MeshName { get; set; } = "";
	[Export] public Vector3 ModelRotationDegrees { get; set; } = Vector3.Zero;
	[Export] public float ModelScale { get; set; } = 1f;

	private static readonly Dictionary<PackedScene, Node> Loaded = new();

	public override void _Ready()
	{
		Model ??= GD.Load<PackedScene>("res://art/treasure.glb");
		MeshInstance3D mesh = MakeMesh(Model, MeshName, ModelRotationDegrees, ModelScale);
		if (mesh != null)
		{
			mesh.Name = "Mesh";
			AddChild(mesh);
			Aabb box = mesh.Transform * mesh.GetAabb();
			Vector3 size = box.Size.Max(Vector3.One * 0.03f);
			AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = box.GetCenter() });
		}
		else
		{
			GD.PushWarning($"{Name}: no mesh \"{MeshName}\" in {Model?.ResourcePath}.");
		}
		base._Ready();
	}

	// A copy of the named mesh (with its materials), turned and scaled, placed so the middle of its base is at the
	// origin. Also used by TreasureDisplay.
	public static MeshInstance3D MakeMesh(PackedScene model, string meshName, Vector3 rotationDegrees, float scale)
	{
		if (model == null || meshName == "")
			return null;
		if (!Loaded.TryGetValue(model, out Node root) || !GodotObject.IsInstanceValid(root))
		{
			root = model.Instantiate();
			Loaded[model] = root;
		}
		if (root.FindChild(meshName, true, false) is not MeshInstance3D source || source.Mesh == null)
			return null;

		var mesh = new MeshInstance3D { Mesh = source.Mesh };
		for (int i = 0; i < source.Mesh.GetSurfaceCount(); i++)
		{
			Material material = source.GetSurfaceOverrideMaterial(i) ?? source.Mesh.SurfaceGetMaterial(i);
			if (material != null)
				mesh.SetSurfaceOverrideMaterial(i, material);
		}
		var basis = Basis.FromEuler(rotationDegrees * (Mathf.Pi / 180f)).Scaled(Vector3.One * scale);
		Aabb turned = new Transform3D(basis, Vector3.Zero) * source.Mesh.GetAabb();
		Vector3 center = turned.GetCenter();
		mesh.Transform = new Transform3D(basis, new Vector3(-center.X, -turned.Position.Y, -center.Z));
		return mesh;
	}
}
