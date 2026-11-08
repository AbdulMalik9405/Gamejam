using System.Collections.Generic;
using Godot;

namespace Gamejam.Gameplay;

// A lit hand torch, built at runtime from art/torch.glb so it follows the art.
// The present player starts with one in their first hotbar slot (net/Session.cs).
public partial class Torch : Pickup
{
	[Export] public string SourceScene { get; set; } = "res://art/torch.glb";
	// The node whose child meshes and light make up the torch (the file's preview floor is left out).
	[Export] public string SourceNode { get; set; } = "Torch";
	[Export] public float LightEnergy { get; set; } = 1.0f;
	[Export] public float LightRange { get; set; } = 8f;
	// Right in front of the camera a bright flame glows pure white and metal reflects nothing but black,
	// so the hand copy can tone both down.
	[Export] public float FlameEmissionScale { get; set; } = 0.6f;
	[Export] public float MaxMetallic { get; set; } = 0.3f;
	// Size relative to the model in the file. Keep Torch.tscn's collision shape and Grip in step when changing it.
	[Export] public float ModelScale { get; set; } = 0.8f;
	// Put on a wall (Player, with the torch out, looking at a wall): how far the top leans out, and how far the
	// stick stands off the wall.
	[Export] public float MountTiltDegrees { get; set; } = 20f;
	[Export] public float MountStandOff { get; set; } = 0.07f;

	public bool IsMounted { get; private set; }

	private sealed class Model
	{
		public readonly List<(Mesh Mesh, Transform3D Transform)> Parts = new();
		public Vector3 LightPosition = new(0f, 0.38f, 0f);
		public Color LightColor = new(1f, 0.77f, 0.51f);
	}

	// Loading the source scene is slow, so each one is read once and shared by every torch.
	private static readonly Dictionary<string, Model> Models = new();

	private OmniLight3D _light;
	private Node3D _bracket;
	private float _time;

	public override void _Ready()
	{
		BuildModel();
		base._Ready();
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		if (IsHeld)
			IsMounted = false;
		_bracket.Visible = IsMounted;
		_light.LightEnergy = LightEnergy * (1f + 0.07f * Mathf.Sin(_time * 13f) + 0.05f * Mathf.Sin(_time * 29f + 1.3f));
	}

	private void BuildModel()
	{
		Model model = GetModel(SourceScene, SourceNode);
		Transform3D scale = Transform3D.Identity.Scaled(Vector3.One * ModelScale);
		foreach ((Mesh mesh, Transform3D transform) in model.Parts)
		{
			// Right in front of the camera, so a shadow would cover half the screen.
			AddChild(new MeshInstance3D
			{
				Mesh = mesh,
				Transform = scale * transform,
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				MaterialOverride = HandMaterial(mesh),
			});
		}

		_light = new OmniLight3D
		{
			Name = "Light",
			Position = model.LightPosition * ModelScale,
			LightColor = model.LightColor,
			LightEnergy = LightEnergy,
			OmniRange = LightRange,
			OmniAttenuation = 1.5f,
			ShadowEnabled = false,
		};
		AddChild(_light);
		BuildBracket();
	}

	// Fixes the torch to a wall at point (on the wall, facing out along normal), held at its grip by an iron bracket,
	// leaning out a little. It stays lit and can be picked up again.
	public void MountOnWall(Vector3 point, Vector3 normal)
	{
		normal = normal.Normalized();
		float tilt = Mathf.DegToRad(MountTiltDegrees);
		Vector3 up = (Vector3.Up * Mathf.Cos(tilt) + normal * Mathf.Sin(tilt)).Normalized();
		Vector3 back = (normal - up * normal.Dot(up)).Normalized();
		var basis = new Basis(up.Cross(back), up, back);
		float gripHeight = GetNodeOrNull<Node3D>("Grip")?.Position.Y ?? 0.3f;
		PlaceAt(new Transform3D(basis, point + normal * MountStandOff - up * gripHeight));
		IsMounted = true;
	}

	// A band around the stick at the grip and an arm back to the wall; only shown while mounted.
	private void BuildBracket()
	{
		float gripHeight = GetNodeOrNull<Node3D>("Grip")?.Position.Y ?? 0.3f;
		var iron = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.1f, 0.09f), Metallic = 0.6f, Roughness = 0.5f };
		_bracket = new Node3D { Name = "Bracket", Position = new Vector3(0f, gripHeight, 0f), Visible = false };
		AddChild(_bracket);
		_bracket.AddChild(new MeshInstance3D
		{
			Mesh = new TorusMesh { InnerRadius = 0.026f, OuterRadius = 0.038f },
			MaterialOverride = iron,
		});
		// The wall is behind the stick (local -Z), further along it than MountStandOff because the torch leans.
		float wall = MountStandOff / Mathf.Cos(Mathf.DegToRad(MountTiltDegrees));
		const float band = 0.03f;
		_bracket.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.022f, 0.022f, wall - band) },
			MaterialOverride = iron,
			Position = new Vector3(0f, 0f, -(band + wall) / 2f),
		});
		_bracket.AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.09f, 0.012f) },
			MaterialOverride = iron,
			Position = new Vector3(0f, 0f, -wall + 0.006f),
		});
	}

	private Material HandMaterial(Mesh mesh)
	{
		if (mesh.GetSurfaceCount() == 0 || mesh.SurfaceGetMaterial(0) is not BaseMaterial3D source)
			return null;
		var material = (BaseMaterial3D)source.Duplicate();
		material.Metallic = Mathf.Min(material.Metallic, MaxMetallic);
		if (material.EmissionEnabled)
			material.EmissionEnergyMultiplier *= FlameEmissionScale;
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
