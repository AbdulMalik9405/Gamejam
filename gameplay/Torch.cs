using System.Collections.Generic;
using Godot;

namespace Gamejam.Gameplay;

// A lit hand torch, copied at runtime from the past map's wall torch (art/level1.glb) so it follows the art.
// The present player starts with one in their first hotbar slot (net/Session.cs).
public partial class Torch : Pickup
{
	[Export] public string SourceScene { get; set; } = "res://art/level1.glb";
	// The wall torch to copy. Its wall bracket is left out.
	[Export] public string SourceNode { get; set; } = "Torch_1";
	[Export] public float LightEnergy { get; set; } = 1.0f;
	[Export] public float LightRange { get; set; } = 8f;
	// The wall torch's flame and metal are made to be seen from across the room. Up close the flame glows
	// pure white and the metal reflects nothing but black, so the hand copy tones both down.
	[Export] public float FlameEmission { get; set; } = 3f;
	[Export] public float MaxMetallic { get; set; } = 0.3f;

	private sealed class Model
	{
		public readonly List<(Mesh Mesh, Transform3D Transform)> Parts = new();
		public Vector3 LightPosition = new(0f, 0.38f, 0f);
		public Color LightColor = new(1f, 0.77f, 0.51f);
	}

	// Loading the source scene is slow, so each one is read once and shared by every torch.
	private static readonly Dictionary<string, Model> Models = new();

	private OmniLight3D _light;
	private float _time;

	public override void _Ready()
	{
		BuildModel();
		base._Ready();
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		_light.LightEnergy = LightEnergy * (1f + 0.07f * Mathf.Sin(_time * 13f) + 0.05f * Mathf.Sin(_time * 29f + 1.3f));
	}

	private void BuildModel()
	{
		Model model = GetModel(SourceScene, SourceNode);
		foreach ((Mesh mesh, Transform3D transform) in model.Parts)
		{
			// Right in front of the camera, so a shadow would cover half the screen.
			AddChild(new MeshInstance3D
			{
				Mesh = mesh,
				Transform = transform,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				MaterialOverride = HandMaterial(mesh),
			});
		}

		_light = new OmniLight3D
		{
			Name = "Light",
			Position = model.LightPosition,
			LightColor = model.LightColor,
			LightEnergy = LightEnergy,
			OmniRange = LightRange,
			OmniAttenuation = 1.5f,
			ShadowEnabled = false,
		};
		AddChild(_light);
	}

	private Material HandMaterial(Mesh mesh)
	{
		if (mesh.GetSurfaceCount() == 0 || mesh.SurfaceGetMaterial(0) is not BaseMaterial3D source)
			return null;
		var material = (BaseMaterial3D)source.Duplicate();
		material.Metallic = Mathf.Min(material.Metallic, MaxMetallic);
		if (material.EmissionEnabled)
			material.EmissionEnergyMultiplier = Mathf.Min(material.EmissionEnergyMultiplier, FlameEmission);
		return material;
	}

	private static Model GetModel(string scenePath, string nodeName)
	{
		string key = scenePath + ":" + nodeName;
		if (Models.TryGetValue(key, out Model cached))
			return cached;

		var model = new Model();
		Models[key] = model;
		Node root = GD.Load<PackedScene>(scenePath)?.Instantiate();
		Node source = root?.FindChild(nodeName, true, false);
		if (source != null)
		{
			foreach (Node child in source.GetChildren())
			{
				if (child is MeshInstance3D mesh && mesh.Mesh != null && !mesh.Name.ToString().Contains("bracket"))
					model.Parts.Add((mesh.Mesh, mesh.Transform));
				else if (child is OmniLight3D light)
				{
					model.LightPosition = light.Position;
					model.LightColor = light.LightColor;
				}
			}
		}
		root?.Free();

		if (model.Parts.Count == 0)
		{
			GD.PushWarning($"Torch: no '{nodeName}' in {scenePath}. Using a plain stick.");
			model.Parts.Add((new CylinderMesh { TopRadius = 0.04f, BottomRadius = 0.04f, Height = 0.6f }, Transform3D.Identity));
		}
		return model;
	}
}
