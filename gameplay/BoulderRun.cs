using Gamejam.Core;
using Godot;

namespace Gamejam.Gameplay;

// The last obstacle: boulders (art/level1old.glb's Ruin_Exit_Boulder-rigid) come down the exit ramp every so often.
// Each one tumbles in over the ramp's side wall near the top, lands on the ramp, rolls down it gathering speed and smashes
// at the bottom; a few seconds later the next one comes. It never sits at the top, so the way out stays open. Players
// dodge into the alcoves in the ramp's walls as it passes and run up between boulders. Anyone it hits is crushed: a
// death screen, then the game is lost for both (gameplay/GameEnding.cs), as in the spike trap.
// Its timing comes from the game clock, which both machines share (core/GameState.cs), so both players see the same
// boulder without anything being sent; each machine only checks whether its own player was hit.
public partial class BoulderRun : Node
{
	[Export] public Node3D Level { get; set; }
	// Where each boulder comes from (up beyond the ramp's south wall), where it lands on the ramp, and where the ramp
	// ends at the bottom (Level's space; the ramp runs down along +X, its middle at LaneZ).
	[Export] public Vector3 FallFrom { get; set; } = new(-26.9f, 11.5f, 19.4f);
	[Export] public float LandX { get; set; } = -26.6f;
	[Export] public float LaneZ { get; set; } = 16.3f;
	[Export] public float BottomX { get; set; } = -18.9f;
	[Export] public float Radius { get; set; } = 0.63f;
	// The cycle: nothing, a trickle of grit from where it'll come over (a warning), the fall in, the roll down.
	[Export] public float GoneTime { get; set; } = 2.6f;
	[Export] public float WarnTime { get; set; } = 0.8f;
	[Export] public float FallTime { get; set; } = 0.75f;
	[Export] public float RollTime { get; set; } = 2.3f;
	[Export] public string DeathTitle { get; set; } = "CRUSHED";
	[Export] public string DeathText { get; set; } = "The boulder rolled right over you.";
	[Export] public float LoseDelay { get; set; } = 2.5f;

	private const string BoulderName = "Ruin_Exit_Boulder-rigid";
	private const float PlayerRadius = 0.35f;
	// The ramp's rough line (Level's space), for where to look for its surface: y = RampY0 + (x - RampX0) * RampSlope.
	private const float RampX0 = -18.4f;
	private const float RampY0 = 1.6f;
	private const float RampSlope = -0.667f;

	private RigidBody3D _boulder;
	private Node3D _visual;
	private float _lastPhase = -1f;
	private CpuParticles3D _dust;
	private bool _dead;

	private float Cycle => GoneTime + WarnTime + FallTime + RollTime;

	public override void _Ready()
	{
		_boulder = Level?.GetNodeOrNull<RigidBody3D>(BoulderName);
		if (_boulder == null)
		{
			GD.PushWarning($"{Name}: no {BoulderName} under Level.");
			SetProcess(false);
			return;
		}
		// Moved by this script, not by physics, and never solid: it hits by this script's check.
		_boulder.Freeze = true;
		_boulder.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
		_boulder.CollisionLayer = 0;
		_boulder.CollisionMask = 0;
		_visual = _boulder.GetChildOrNull<Node3D>(0);
		_dust = MakeDust();
		Level.AddChild(_dust);
	}

	public override void _Process(double delta)
	{
		double now = GameState.Instance?.ElapsedSeconds ?? 0d;
		float phase = (float)(now % Cycle);
		float warnStart = GoneTime;
		float fallStart = warnStart + WarnTime;
		float rollStart = fallStart + FallTime;

		Vector3 land = new(LandX, SurfaceY(LandX) + Radius, LaneZ);
		Vector3 at = FallFrom;
		float roll = 0f, tumble = 0f;
		bool moving = false, visible = true;
		if (phase < fallStart)
			visible = false;
		else if (phase < rollStart)
		{
			// Over the wall and down onto the ramp: across steadily, dropping faster and faster, tumbling as it comes.
			float k = (phase - fallStart) / FallTime;
			at = new Vector3(Mathf.Lerp(FallFrom.X, land.X, k), Mathf.Lerp(FallFrom.Y, land.Y, k * k), Mathf.Lerp(FallFrom.Z, land.Z, k));
			tumble = k * 2.5f;
			moving = true;
		}
		else
		{
			// Down the ramp gathering speed, on its surface, turning as it rolls.
			float k = (phase - rollStart) / RollTime;
			float x = Mathf.Lerp(LandX, BottomX, k * (0.35f + 0.65f * k));
			at = new Vector3(x, SurfaceY(x) + Radius, LaneZ);
			roll = (x - LandX) / Radius;
			tumble = 2.5f;
			moving = true;
		}

		// A trickle of grit where it'll come over; a thud as it lands; a smash at the bottom.
		if (_lastPhase >= 0f)
		{
			if (_lastPhase < warnStart && phase >= warnStart)
				Burst(new Vector3(FallFrom.X, FallFrom.Y - 2.5f, LaneZ + 0.8f), "boulder_rumble", 12);
			if (_lastPhase < rollStart && phase >= rollStart)
				Burst(land + Vector3.Down * Radius, "boulder_land", 30);
			if (phase < _lastPhase)
				Burst(new Vector3(BottomX, SurfaceY(BottomX), LaneZ), "boulder_smash", 40);
		}
		_lastPhase = phase;

		_boulder.Position = at;
		_boulder.Rotation = new Vector3(tumble, 0f, -roll);
		if (_visual != null)
			_visual.Visible = visible;
		if (moving && visible)
			CheckHit();
	}

	// The ramp's surface height under x (Level's space): a short ray down the middle of the ramp from just above its
	// rough line, so it finds the floor and not the ramp's ceiling.
	private float SurfaceY(float x)
	{
		float line = RampY0 + (x - RampX0) * RampSlope;
		Vector3 from = Level.ToGlobal(new Vector3(x, line + 1.2f, LaneZ));
		var query = PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 2.6f, Layers.World);
		Godot.Collections.Dictionary hit = Level.GetWorld3D().DirectSpaceState.IntersectRay(query);
		return hit.Count > 0 ? Level.ToLocal(hit["position"].AsVector3()).Y : line;
	}

	// This machine's player, if the boulder's on them: crushed.
	private void CheckHit()
	{
		if (_dead || GameState.Instance == null)
			return;
		if (GameState.Instance.GetPlayerNode(Multiplayer.GetUniqueId()) is not Player player || player.InputLocked)
			return;
		Vector3 local = Level.ToLocal(player.GlobalPosition);
		Vector3 centre = _boulder.Position;
		// The player as an upright capsule from their feet to head height.
		float y = Mathf.Clamp(centre.Y, local.Y + PlayerRadius, local.Y + 1.8f - PlayerRadius);
		if (new Vector3(local.X, y, local.Z).DistanceTo(centre) > Radius + PlayerRadius)
			return;
		_dead = true;
		player.InputLocked = true;
		player.Velocity = Vector3.Zero;
		Events.Instance?.EmitSignal(Events.SignalName.PlaySfx, "boulder_hit", player.GlobalPosition);
		Crushed();
	}

	// Knocked flat, a red screen with the death lines, then the lose ending for both players.
	private async void Crushed()
	{
		Events.Instance?.EmitSignal(Events.SignalName.CameraShake, 0.8f);
		var hud = GetTree().CurrentScene?.GetNodeOrNull<CanvasLayer>("Hud");
		if (hud != null)
			hud.Visible = false;
		var overlay = new CanvasLayer { Name = "BoulderDeath", Layer = 20 };
		AddChild(overlay);
		var fade = new ColorRect { Color = new Color(0.5f, 0.02f, 0f, 0.6f), MouseFilter = Control.MouseFilterEnum.Ignore };
		fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(fade);
		var box = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center, Modulate = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Ignore };
		box.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(box);
		var font = new SystemFont { FontNames = new[] { "Cinzel", "Trajan Pro", "Georgia", "Times New Roman", "serif" } };
		var title = new Label { Text = DeathTitle, HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontOverride("font", font);
		title.AddThemeFontSizeOverride("font_size", 64);
		title.AddThemeColorOverride("font_color", new Color(0.85f, 0.15f, 0.1f));
		box.AddChild(title);
		var text = new Label { Text = DeathText, HorizontalAlignment = HorizontalAlignment.Center };
		text.AddThemeFontOverride("font", font);
		text.AddThemeFontSizeOverride("font_size", 26);
		text.AddThemeColorOverride("font_color", new Color(0.93f, 0.86f, 0.7f));
		box.AddChild(text);
		Tween show = CreateTween();
		show.TweenProperty(fade, "color", new Color(0.12f, 0f, 0f, 1f), 0.35f);
		show.TweenProperty(box, "modulate", Colors.White, 0.6f);

		await ToSignal(GetTree().CreateTimer(LoseDelay), SceneTreeTimer.SignalName.Timeout);
		GameEnding ending = null;
		foreach (Node node in GetTree().GetNodesInGroup("game_ending"))
			ending ??= node as GameEnding;
		if (ending == null)
			return;
		ending.RequestEnd(false);
		// The ending fades to black first; this screen goes once it's covered.
		await ToSignal(GetTree().CreateTimer(ending.FadeTime + 0.2f), SceneTreeTimer.SignalName.Timeout);
		overlay.QueueFree();
	}

	private void Burst(Vector3 localPosition, string sfx, int amount)
	{
		_dust.Position = localPosition;
		_dust.Amount = amount;
		_dust.Restart();
		Events.Instance?.EmitSignal(Events.SignalName.PlaySfx, sfx, Level.ToGlobal(localPosition));
	}

	private static CpuParticles3D MakeDust()
	{
		var dustColor = new Color(0.55f, 0.46f, 0.34f);
		return new CpuParticles3D
		{
			Name = "BoulderDust",
			Emitting = false,
			OneShot = true,
			Explosiveness = 0.9f,
			Amount = 40,
			Lifetime = 2f,
			Mesh = new QuadMesh { Size = new Vector2(0.45f, 0.45f), Material = Rubble.DustMaterial() },
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Ring,
			EmissionRingAxis = Vector3.Up,
			EmissionRingRadius = 0.7f,
			EmissionRingInnerRadius = 0.2f,
			EmissionRingHeight = 0.1f,
			Direction = Vector3.Up,
			Spread = 70f,
			InitialVelocityMin = 0.6f,
			InitialVelocityMax = 1.8f,
			Gravity = new Vector3(0f, -0.4f, 0f),
			DampingMin = 1.2f,
			DampingMax = 2f,
			ScaleAmountMin = 0.6f,
			ScaleAmountMax = 1.6f,
			ColorRamp = new Gradient
			{
				Offsets = new[] { 0f, 0.2f, 1f },
				Colors = new[] { dustColor with { A = 0f }, dustColor with { A = 0.35f }, dustColor with { A = 0f } },
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
	}
}
