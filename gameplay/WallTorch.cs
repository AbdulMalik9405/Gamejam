using Godot;

namespace Gamejam.Gameplay;

// An unlit wall torch the player can light by holding their hand Torch and pressing interact.
// Attach to a torch's root node (its origin is the foot of the stick); the flame and light are copied
// from the intact map's torch so both eras look the same when lit.
public partial class WallTorch : Node3D
{
	[Export] public string SourceScene { get; set; } = "res://art/level1.glb";
	[Export] public string SourceFlame { get; set; } = "Torch_1_flame";
	[Export] public string SourceLight { get; set; } = "TorchLight_1";
	// Same values as the intact map's torch lights in main.tscn.
	[Export] public float LightEnergy { get; set; } = 0.75f;
	[Export] public float LightRange { get; set; } = 10f;
	[Export] public float LightAttenuation { get; set; } = 1.5f;
	// Moves the copied flame and light from the intact torch's cup up onto this torch's head.
	// The default suits the ruined map's cloth-head torches (head top 0.66 m up the stick).
	[Export] public Vector3 FlameOffset { get; set; } = new Vector3(0f, 0.49f, 0f);
	// The flame points straight up even when the torch leans in its sconce.
	[Export] public bool KeepFlameUpright { get; set; } = true;
	[Export] public Vector3 HitBoxSize { get; set; } = new Vector3(0.3f, 0.8f, 0.3f);
	[Export] public Vector3 HitBoxOffset { get; set; } = new Vector3(0f, 0.4f, 0f);

	[Signal] public delegate void LitChangedEventHandler(WallTorch torch, bool lit);

	public bool IsLit { get; private set; }
	// Set once the puzzle is solved, so the torches stay lit.
	public bool Locked { get; set; }

	private const string LightPrompt = "Light torch";
	private const string NeedFlamePrompt = "Hold your torch to light this";

	private sealed class Model
	{
		public Mesh Flame;
		public Transform3D FlameTransform = new(Basis.Identity, new Vector3(0f, 0.24f, 0f));
		public Vector3 LightPosition = new(0f, 0.38f, 0f);
		public Color LightColor = new(1f, 0.77f, 0.51f);
	}

	// Loading the source scene is slow, so it's read once and shared by every wall torch.
	private static Model _model;

	private MeshInstance3D _flame;
	private OmniLight3D _light;
	private Interactable _target;
	private float _time;

	public override void _Ready()
	{
		Model model = GetModel();
		_flame = new MeshInstance3D
		{
			Name = "Flame",
			Mesh = model.Flame,
			Transform = model.FlameTransform.Translated(FlameOffset),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Visible = false,
		};
		AddChild(_flame);
		if (KeepFlameUpright)
			_flame.GlobalRotation = Vector3.Zero;

		_light = new OmniLight3D
		{
			Name = "Light",
			Position = model.LightPosition + FlameOffset,
			LightColor = model.LightColor,
			LightEnergy = LightEnergy,
			OmniRange = LightRange,
			OmniAttenuation = LightAttenuation,
			Visible = false,
		};
		AddChild(_light);

		_target = new Interactable { Name = "LightTarget", Id = Name + "_light", PromptFor = PromptFor };
		_target.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = HitBoxSize },
			Position = HitBoxOffset,
		});
		AddChild(_target);
		_target.Interacted += OnInteracted;

		// Random start so lit torches don't flicker in sync.
		_time = (float)GD.RandRange(0.0, 10.0);
		SetProcess(false);
	}

	public override void _ExitTree()
	{
		if (_target != null)
			_target.Interacted -= OnInteracted;
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		_light.LightEnergy = LightEnergy * (1f + 0.07f * Mathf.Sin(_time * 13f) + 0.05f * Mathf.Sin(_time * 29f + 1.3f));
	}

	public void Light() => SetLit(true);

	public void Extinguish()
	{
		if (!Locked)
			SetLit(false);
	}

	private void SetLit(bool lit)
	{
		if (lit == IsLit)
			return;
		IsLit = lit;
		_flame.Visible = lit;
		_light.Visible = lit;
		SetProcess(lit);
		EmitSignal(SignalName.LitChanged, this, lit);
	}

	private string PromptFor(Player player)
	{
		if (IsLit)
			return "";
		return player.Inventory.SelectedItem is Torch ? LightPrompt : NeedFlamePrompt;
	}

	private void OnInteracted(Player player)
	{
		if (!IsLit && player.Inventory.SelectedItem is Torch)
			Light();
	}

	private Model GetModel()
	{
		if (_model != null)
			return _model;

		_model = new Model();
		Node root = GD.Load<PackedScene>(SourceScene)?.Instantiate();
		if (root?.FindChild(SourceFlame, true, false) is MeshInstance3D flame)
		{
			_model.Flame = flame.Mesh;
			_model.FlameTransform = flame.Transform;
		}
		if (root?.FindChild(SourceLight, true, false) is OmniLight3D light)
		{
			_model.LightPosition = light.Position;
			_model.LightColor = light.LightColor;
		}
		root?.Free();

		if (_model.Flame == null)
		{
			GD.PushWarning($"WallTorch: no '{SourceFlame}' in {SourceScene}. Using a plain flame.");
			_model.Flame = new SphereMesh
			{
				Radius = 0.06f,
				Height = 0.16f,
				Material = new StandardMaterial3D { EmissionEnabled = true, Emission = _model.LightColor, AlbedoColor = _model.LightColor },
			};
		}
		return _model;
	}
}
