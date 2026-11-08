using Godot;

namespace Gamejam.Gameplay;

// Shows a copy of another mesh, moved by Offset, e.g. a prop that's only in one era's level model, copied into the
// other era's room 200 m away so both rooms look the same.
public partial class MirrorMesh : MeshInstance3D
{
	[Export] public MeshInstance3D Source { get; set; }
	[Export] public Vector3 Offset { get; set; } = new(-200f, 0f, 0f);

	public override void _Ready()
	{
		if (Source?.Mesh == null)
		{
			GD.PushWarning($"{Name}: set Source to the mesh to copy.");
			return;
		}
		Mesh = Source.Mesh;
		for (int i = 0; i < Source.Mesh.GetSurfaceCount(); i++)
			SetSurfaceOverrideMaterial(i, Source.GetActiveMaterial(i));
		Transform3D t = Source.GlobalTransform;
		GlobalTransform = t with { Origin = t.Origin + Offset };
	}
}
