using Godot;

namespace Gamejam.Gameplay;

// A small carved niche on the wall behind a hidden reward, built by RiseOnSolved when OpenWall is on. When the
// reward is revealed, the niche's stone hatch sinks and slides up into the lintel, then a ledge carrying the reward
// slides out of the dark opening. Uses the wall's own material so it looks cut into it.
// It can also be placed in a level on its own (no Reward), facing Outward, as a closed niche that never opens, e.g.
// so the other era's room has the same niches.
public partial class WallOpening : Node3D
{
	private static readonly Vector2 OpeningSize = new(0.32f, 0.16f);
	private const float FrameDepth = 0.04f;
	private const float FrameWidth = 0.05f;
	private const float SillHeight = 0.04f;
	private const float HoleDepth = 0.18f;
	private const float HatchThickness = 0.012f;
	private const float LedgeWidth = 0.22f;
	private const float LedgeThickness = 0.05f;
	// Gap between the ledge's top and the reward's origin (the reward lies on the ledge).
	private const float RestGap = 0.015f;
	private const float SinkPart = 0.35f;

	public float OpenTime { get; set; } = 0.7f;
	// Placed on its own: the direction out of the wall, from this node's position (a point just out from the wall).
	[Export] public Vector3 Outward { get; set; } = Vector3.Back;

	// Set by RiseOnSolved before adding this to the tree.
	public Node3D Reward { get; set; }
	public Vector3 RewardEnd { get; set; }     // global position the reward ends at
	public Vector3 Travel { get; set; }        // global offset from the hidden start to RewardEnd
	[Export] public float LedgeLength { get; set; } = 0.42f;

	private MeshInstance3D _hatch;
	private MeshInstance3D _ledge;
	private Vector3 _hatchClosed;
	private Vector3 _ledgeOut;
	private float _ledgeTravel;

	public override void _Ready()
	{
		if (Reward == null)
		{
			RewardEnd = GlobalPosition;
			Travel = Outward.Normalized() * (LedgeLength + 0.03f);
		}
		// The wall is found with a raycast, which needs the level's bodies in the physics world first.
		Visible = false;
		SetPhysicsProcess(true);
	}

	public override void _PhysicsProcess(double delta)
	{
		SetPhysicsProcess(false);
		Build();
	}

	private void Build()
	{
		Vector3 outward = Travel;
		outward.Y = 0f;
		outward = outward.LengthSquared() > 0.0001f ? outward.Normalized() : Vector3.Back;

		var query = PhysicsRayQueryParameters3D.Create(RewardEnd, RewardEnd - outward * 1.5f, 1u);
		if (Reward is CollisionObject3D body)
			query.Exclude = new Godot.Collections.Array<Rid> { body.GetRid() };
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

		Vector3 surface = RewardEnd;
		Material wallMaterial = null;
		if (hit.Count > 0)
		{
			surface = hit["position"].AsVector3();
			if (hit["collider"].AsGodotObject() is Node collider && collider.GetParent() is MeshInstance3D mesh)
				wallMaterial = mesh.GetActiveMaterial(0);
		}
		else
			GD.PushWarning($"{Name}: no wall found behind {Reward?.Name}; the niche floats at the reward.");

		Vector3 right = Vector3.Up.Cross(outward).Normalized();
		// Centred on the reward's visible shape, which needn't be centred on its origin.
		Vector3 origin = new Vector3(surface.X, RewardEnd.Y, surface.Z) + right * right.Dot(MeshCentre() - RewardEnd);
		GlobalTransform = new Transform3D(new Basis(right, Vector3.Up, outward), origin);

		Material stone = StoneMaterial(wallMaterial);
		var dark = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0.02f, 0.015f, 0.01f),
		};

		float w = OpeningSize.X;
		float h = OpeningSize.Y;
		float ledgeTop = -RestGap;
		float bottom = ledgeTop - LedgeThickness - 0.015f;
		float top = bottom + h;

		// The frame stands proud of the wall. The lintel is as tall as the opening so the hatch can hide behind it.
		float outerW = w + 2f * FrameWidth;
		AddBox(stone, new Vector3(outerW, SillHeight, FrameDepth), new Vector3(0f, bottom - SillHeight / 2f, FrameDepth / 2f));
		AddBox(stone, new Vector3(outerW, h, FrameDepth), new Vector3(0f, top + h / 2f, FrameDepth / 2f));
		AddBox(stone, new Vector3(FrameWidth, h, FrameDepth), new Vector3(-(w + FrameWidth) / 2f, bottom + h / 2f, FrameDepth / 2f));
		AddBox(stone, new Vector3(FrameWidth, h, FrameDepth), new Vector3((w + FrameWidth) / 2f, bottom + h / 2f, FrameDepth / 2f));

		// The opening: a dark box sunk into the wall, its face just in front of the wall's.
		MeshInstance3D hole = AddBox(dark, new Vector3(w, h, HoleDepth), new Vector3(0f, bottom + h / 2f, 0.004f - HoleDepth / 2f));
		hole.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;

		_hatchClosed = new Vector3(0f, bottom + h / 2f, FrameDepth - HatchThickness / 2f);
		_hatch = AddBox(stone, new Vector3(w - 0.006f, h - 0.006f, HatchThickness), _hatchClosed);

		_ledgeOut = new Vector3(0f, ledgeTop - LedgeThickness / 2f, LedgeLength / 2f + 0.005f);
		_ledgeTravel = Travel.Length();
		_ledge = AddBox(stone, new Vector3(LedgeWidth, LedgeThickness, LedgeLength), _ledgeOut - new Vector3(0f, 0f, _ledgeTravel));
		_ledge.Visible = false;

		Visible = true;
	}

	// Sinks the hatch a little, slides it up behind the lintel, then calls then.
	public void Open(System.Action then)
	{
		if (_hatch == null)
		{
			then?.Invoke();
			return;
		}
		Vector3 sunk = _hatchClosed with { Z = 0.004f + HatchThickness / 2f + 0.002f };
		Tween tween = CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		tween.TweenProperty(_hatch, "position", sunk, OpenTime * SinkPart);
		tween.TweenProperty(_hatch, "position", sunk + Vector3.Up * OpeningSize.Y, OpenTime * (1f - SinkPart));
		tween.TweenCallback(Callable.From(() =>
		{
			_hatch.Visible = false;
			then?.Invoke();
		}));
	}

	// Slides the ledge out alongside the reward (same easing as RiseOnSolved's rise).
	public void SlideOut(float duration)
	{
		if (_ledge == null)
			return;
		_ledge.Visible = true;
		CreateTween().SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out)
			.TweenProperty(_ledge, "position", _ledgeOut, duration);
	}

	private Vector3 MeshCentre()
	{
		if (Reward == null)
			return RewardEnd;
		Aabb? bounds = null;
		foreach (Node node in Reward.FindChildren("*", "MeshInstance3D", true, false))
		{
			var mesh = (MeshInstance3D)node;
			Aabb box = mesh.GlobalTransform * mesh.GetAabb();
			bounds = bounds?.Merge(box) ?? box;
		}
		return bounds?.GetCenter() ?? RewardEnd;
	}

	private MeshInstance3D AddBox(Material material, Vector3 size, Vector3 position)
	{
		var box = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = size },
			MaterialOverride = material,
			Position = position,
		};
		AddChild(box);
		return box;
	}

	// The wall's material, mapped in world space so its texture keeps the wall's scale on these small boxes.
	private static Material StoneMaterial(Material wall)
	{
		if (wall is StandardMaterial3D standard)
		{
			var copy = (StandardMaterial3D)standard.Duplicate();
			copy.Uv1Triplanar = true;
			copy.Uv1WorldTriplanar = true;
			copy.Uv1Scale = Vector3.One * 0.5f;
			copy.Uv1Offset = Vector3.Zero;
			return copy;
		}
		return wall ?? new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.47f, 0.37f), Roughness = 0.9f };
	}
}
