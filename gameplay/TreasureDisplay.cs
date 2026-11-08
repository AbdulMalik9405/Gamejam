using Godot;

namespace Gamejam.Gameplay;

// A trinket from a model file (art/treasure.glb) as plain scenery, standing on this node's origin like a
// TreasurePickup: e.g. the past's shelf showing the order the present player has to put the trinkets in.
public partial class TreasureDisplay : Node3D
{
	[Export] public PackedScene Model { get; set; }
	[Export] public string MeshName { get; set; } = "";
	[Export] public Vector3 ModelRotationDegrees { get; set; } = Vector3.Zero;
	[Export] public float ModelScale { get; set; } = 1f;

	public override void _Ready()
	{
		Model ??= GD.Load<PackedScene>("res://art/treasure.glb");
		MeshInstance3D mesh = TreasurePickup.MakeMesh(Model, MeshName, ModelRotationDegrees, ModelScale);
		if (mesh == null)
		{
			GD.PushWarning($"{Name}: no mesh \"{MeshName}\" in {Model?.ResourcePath}.");
			return;
		}
		mesh.Name = "Mesh";
		AddChild(mesh);
	}
}
