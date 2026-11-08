using Godot;

namespace Gamejam.Gameplay;

// Turns a mesh that's part of a level model (e.g. the seal on the sarcophagus lid in art/level1old.glb) into a
// Pickup: it takes the mesh's place and looks, gets a box collider fitted to it, and the original is hidden. Put
// this RigidBody3D anywhere in the level and point Source at the mesh.
public partial class ArtPickup : Pickup
{
	[Export] public MeshInstance3D Source { get; set; }
	// Where the hand holds it and at what angle, relative to the mesh (see HeldItemView's Grip). Like carrying a
	// tray by its near edge by default.
	[Export] public Vector3 GripPosition { get; set; } = Vector3.Zero;
	[Export] public Vector3 GripRotationDegrees { get; set; } = new(-30f, 0f, 0f);

	public override void _Ready()
	{
		if (Source?.Mesh != null)
			TakeOver(Source);
		else
			GD.PushWarning($"{Name}: set Source to the level mesh this pickup replaces.");
		base._Ready();
	}

	private void TakeOver(MeshInstance3D source)
	{
		GlobalTransform = source.GlobalTransform;
		var mesh = new MeshInstance3D { Name = "Mesh", Mesh = source.Mesh };
		for (int i = 0; i < source.Mesh.GetSurfaceCount(); i++)
			mesh.SetSurfaceOverrideMaterial(i, source.GetActiveMaterial(i));
		AddChild(mesh);

		Aabb box = source.GetAabb();
		Vector3 size = box.Size.Max(Vector3.One * 0.02f);
		AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = box.GetCenter() });
		AddChild(new Marker3D
		{
			Name = "Grip",
			Position = GripPosition,
			RotationDegrees = GripRotationDegrees,
		});

		source.Visible = false;
		foreach (Node child in source.GetChildren())
		{
			if (child is CollisionObject3D body)
				body.CollisionLayer = 0;
		}
	}
}
