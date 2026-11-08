using System.Collections.Generic;
using Gamejam.Core;
using Godot;
using Godot.Collections;

namespace Gamejam.Gameplay;

/// <summary>
/// The past character's death, which is how the present room's ceiling got its hole and rubble pile. When Trigger is
/// solved (the tile placed on the crypt's altar), after a moment both players see "Some time long ago...", then the
/// intact past room, where the Egyptian walks slowly round the sarcophagus, stopping to mourn. Then: he walks in as the ground
/// rumbles and dust trickles down, looks up, the ceiling above the spot that's open in the present breaks, and the
/// rubble buries him (he goes limp, a ragdoll). Then it cuts to the same room, where he's laid in the royal coffin on
/// the floor and its lid swings shut on him. Then "In the modern day...", and the explorer climbing the stairs into the
/// crypt. Then the crypt itself: the royal coffin there rattles and bursts
/// open, and he comes out as the mummy and runs at the explorer (seen through the explorer's eyes). Then both players
/// get their own views back, and only then does this count as solved on the present player's machine (for whatever
/// comes next). Nobody dies.
/// </summary>
public partial class CeilingCollapse : Puzzle
{
	[Export] public Puzzle Trigger { get; set; }
	// The past map's root (level1), its ceiling, and the present map's ceiling pieces, which leave the hole. Copies of
	// those pieces, in the past's materials, replace the past ceiling for good when the cutscene starts.
	[Export] public Node3D PastLevel { get; set; }
	[Export] public MeshInstance3D PastCeiling { get; set; }
	[Export] public Array<MeshInstance3D> BrokenCeiling { get; set; } = new();
	// The hole, the Egyptian's feet and the camera, in PastLevel's space.
	[Export] public Vector3 HoleMin { get; set; } = new(2.7f, 4.0f, -0.8f);
	[Export] public Vector3 HoleMax { get; set; } = new(4.3f, 4.4f, 0.8f);
	[Export] public Vector3 VictimPosition { get; set; } = new(3.5f, 0f, 0.25f);
	// Where he walks in from (PastLevel's space), arriving under the hole just before it gives.
	[Export] public Vector3 WalkFrom { get; set; } = new(3.6f, 0f, -1.4f);
	// Before that: the title, then a wide shot of the room as he walks round the sarcophagus (PastLevel's space).
	[Export] public string PreludeTitle { get; set; } = "Some time long ago...";
	[Export] public Vector3 PreludeCameraPosition { get; set; } = new(-0.4f, 2.3f, 3.7f);
	[Export] public Vector3 PreludeFocus { get; set; } = new(0f, 0.4f, -1.0f);
	[Export] public Vector3 CameraPosition { get; set; } = new(0.8f, 1.5f, 0.9f);
	[Export] public PackedScene VictimScene { get; set; }
	// The coffin he's laid in: the crypt's royal coffin (its mesh becomes the lid), lying on the floor at
	// CoffinPosition (PastLevel's space, turned by CoffinYawDegrees, head end towards +Z), and where the camera stands
	// in the coffin's own space.
	[Export] public MeshInstance3D RoyalCoffin { get; set; }
	[Export] public Vector3 CoffinPosition { get; set; } = new(-2.75f, 0f, 0f);
	[Export] public float CoffinYawDegrees { get; set; }
	[Export] public Vector3 CoffinCameraPosition { get; set; } = new(-1.1f, 1.45f, 0.55f);
	// The mummy standing in the present's royal coffin (a CharacterModel in the present map), which breaks out at the
	// end, and that shot's camera and where it steps down onto the crypt floor, in the mummy's parent's space.
	[Export] public CharacterModel Mummy { get; set; }
	[Export] public Vector3 MummyCameraPosition { get; set; } = new(1.1f, 3.0f, 17.2f);
	[Export] public Vector3 MummyCameraTarget { get; set; } = new(0f, 2.9f, 19.9f);
	[Export] public Vector3 MummyExit { get; set; } = new(0f, 1.6f, 19.0f);
	// The explorer's walk into the crypt, in the mummy's parent's space: from the main room, to the foot of the stairs,
	// up them (StairsTop) and on to the crypt's doorway, then to wherever the explorer really stands.
	[Export] public string ModernTitle { get; set; } = "In the modern day...";
	[Export] public Vector3 ModernWalkFrom { get; set; } = new(0f, 0f, 3.75f);
	[Export] public Vector3 StairsBottom { get; set; } = new(0f, 0f, 5.35f);
	[Export] public Vector3 StairsTop { get; set; } = new(0f, 1.6f, 9.1f);
	[Export] public Vector3 CryptDoorway { get; set; } = new(0f, 1.6f, 13.6f);
	// The trap room shot after the way back has closed (in the mummy's parent's space), and its line.
	[Export] public Vector3 TrapCameraPosition { get; set; } = new(-6.0f, 3.5f, 18.7f);
	[Export] public Vector3 TrapCameraTarget { get; set; } = new(-12.5f, 0.4f, 15.6f);
	[Export] public string TrapTitle { get; set; } = "Nowhere to run. You must now work together.";

	// Seconds from the cut to the past room.
	private const float FadeTime = 0.5f;
	// The prelude: a pause after the tile is placed, a fade to black, the title, then the room.
	private const float PreludeDelay = 1.5f;
	private const float TitleTime = 3.2f;
	private const float MournWalkSpeed = 1.15f;
	private const float MournPause = 1.8f;
	// He walks in (slowing as he hears the rumble), stops, and looks up as it breaks; the camera follows him and
	// tilts partway up to the ceiling with him.
	private const float WalkEnd = 1.85f;
	private const float LookUpStart = 1.3f;
	private const float LookUpEnd = 2.1f;
	private const float BreakTime = 2.3f;
	// He goes limp a moment before the slab reaches him: starting the ragdoll with a chunk already inside him would
	// fling him across the room.
	private const float FallTime = 2.7f;
	private const float RubbleEnd = 3.9f;
	private const float ShotEnd = 7.0f;
	// The coffin shot, from the cut: the lid starts to swing shut, takes this long, then the shot holds.
	private const float CoffinStart = ShotEnd + FadeTime;
	private const float LidDelay = 1.5f;
	private const float LidTime = 1.3f;
	private const float LidOpenDegrees = 105f;
	private const float CoffinEnd = CoffinStart + LidDelay + LidTime + 1.5f;
	// The mummy shot, from its cut: two rattles, the burst, stepping out, then the cut to the explorer's eyes for the
	// run, which ends RunStopShort in front of them.
	// "In the modern day...", then the explorer walking up the stairs into the crypt, ending where the explorer really
	// stands. The mummy shot follows once that's over (its start depends on how far the walk is).
	private const float ModernStart = CoffinEnd + FadeTime;
	private const float ModernWalkStart = ModernStart + TitleTime;
	private const float ModernWalkSpeed = 1.7f;
	private const float RattleOne = 0.7f;
	private const float RattleTwo = 1.4f;
	private const float BurstTime = 2.1f;
	private const float StepEnd = 2.9f;
	private const float PovCut = 3.1f;
	private const float RunSpeed = 3.3f;
	private const float RunStopShort = 1.3f;
	// The crypt's doorway, along the mummy's parent's Z: the run never goes past it.
	private const float CryptDoorZ = 13.4f;
	// The fright, from the mummy reaching the explorer: recoil, whirl round, run for the passage to the main room
	// (stopping at FleeStop, in the mummy's parent's space) while the wall there rises back up and slams shut.
	private const float RecoilEnd = 0.45f;
	private const float TurnEnd = 0.95f;
	private const float FleeEnd = 2.45f;
	private const float WallRise = 1.45f;
	private const float WallSlam = 2.0f;
	private const float ScaredEnd = 3.1f;
	private static readonly Vector3 FleeStop = new(0f, 1.6f, 11.0f);
	// Then the trap room, with the line telling them what's next.
	private const float TrapShotTime = 4.2f;
	// The trough is the royal coffin's outline measured at this many points along its length.
	private const int CoffinStations = 24;
	private const float CoffinWall = 0.035f;
	private const float CoffinWallHeight = 0.36f;
	// The lid is the royal coffin flattened to this much of its depth.
	private const float LidDepthScale = 0.45f;
	private const int PlugCells = 3;
	private const int RubbleCount = 40;
	private const int PebbleCount = 22;
	// After the main fall, loose bits keep dropping from the edges of the hole for a while.
	private const int AftershockCount = 18;
	private const float AftershockEnd = 6.5f;
	// Gravel that pours down with the big stones, and how many of the big ones throw up a puff of dust as they land.
	private const int GravelCount = 28;
	private const int MaxLandingPuffs = 12;

	private bool _playing;
	private bool _inPrelude;
	private float _preludeTime;
	private float _preludeShotEnd;
	private bool _preludeBuilt;
	private Label _title;
	private CharacterModel _mourner;
	private Camera3D _preludeCamera;
	// His walk round the room: where each leg starts and ends, how long it takes, and whether he stands and mourns.
	private readonly List<(Vector3 From, Vector3 To, float Time, bool Mourn)> _mournRoute = new();
	// Trigger was solved on this machine (the present player's), so the passage reward waits on this one.
	private bool _solvedHere;
	private float _time = -1f;
	private Player _localPlayer;
	private CanvasLayer _hud;
	private Camera3D _camera;
	private ColorRect _fade;
	private Node3D _victimPivot;
	private CharacterModel _victim;
	private Node3D _debris;
	private readonly List<RigidBody3D> _plug = new();
	private readonly List<(float Time, RigidBody3D Body)> _pending = new();
	private CpuParticles3D _trickle;
	private CpuParticles3D _burst;
	private Material _starsMaterial;
	private Material _stoneMaterial;
	private bool _broke, _fell, _cutBack, _finished, _inCoffin, _slammed;
	private Node3D _coffin;
	private Node3D _lidHinge;
	private CpuParticles3D _slamDust;
	private bool _inMummy, _coffinBroken, _atPov;
	private float _mummyEnd = -1f;
	// The explorer's fright and flight (from when the mummy reaches them), the wall slamming shut ahead of them, then
	// the trap room shot.
	private float _scaredStart = -1f;
	private Vector3 _fleeFrom;
	private Vector3 _fleeTo;
	private Basis _fleeLook;
	private Node3D _wall;
	private Vector3 _wallDown;
	private Vector3 _wallUp;
	private bool _wallSlammed;
	private float _trapStart = -1f;
	private Camera3D _trapCamera;
	private bool _modernBuilt;
	private float _modernEnd = float.MaxValue;
	private float _mummyShotStart = float.MaxValue;
	private Node3D _explorerWalker;
	private readonly List<Vector3> _modernPath = new();
	private float _modernLength;
	private Node3D _crypt;
	private Node3D _cryptDebris;
	private Transform3D _coffinRest;
	private Vector3 _mummyStart;
	private readonly List<Vector3> _mummyPath = new();
	private float _mummyPathLength;
	private Camera3D _explorerCamera;
	private readonly RandomNumberGenerator _rng = new();
	private Material[] _rockShades;
	private int _landingPuffs;

	public override void _Ready()
	{
		if (Trigger != null)
			Trigger.Solved += OnTriggerSolved;
		VictimScene ??= GD.Load<PackedScene>("res://art/egyptian.glb");
	}

	public override void _ExitTree()
	{
		if (Trigger != null && IsInstanceValid(Trigger))
			Trigger.Solved -= OnTriggerSolved;
		if (_playing && !_finished)
			GameState.Instance?.ResumeTimer();
		// Never leave a player stuck if this goes away early.
		if (_playing && !_cutBack && IsInstanceValid(_localPlayer))
			_localPlayer.InputLocked = false;
	}

	private void OnTriggerSolved()
	{
		_solvedHere = true;
		// Pickups are local, so only the present player's machine knows the keys are in. Tell everyone.
		Rpc(MethodName.Play);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	private void Play()
	{
		if (_playing || PastLevel == null)
			return;
		_playing = true;
		GameState.Instance?.PauseTimer();
		_rng.Seed = 1234;
		_localPlayer = GameState.Instance?.GetPlayerNode(Multiplayer.GetUniqueId()) as Player;
		if (_localPlayer != null)
			_localPlayer.InputLocked = true;
		_hud = GetTree().CurrentScene?.GetNodeOrNull<CanvasLayer>("Hud");
		BuildOverlay();
		_inPrelude = true;
		_preludeTime = 0f;
	}

	public override void _Process(double delta)
	{
		if (!_playing || _finished)
			return;
		float dt = (float)delta;
		if (_inPrelude)
		{
			Prelude(dt);
			return;
		}
		float before = _time;
		_time += dt;
		float t = _time;

		if (before < 0f && t >= 0f)
			CutToPast();
		if (t < 0f)
		{
			_fade.Color = new Color(0f, 0f, 0f, Mathf.Clamp(1f + t / FadeTime, 0f, 1f));
			return;
		}

		// Each shot fades in after its cut and out at its end.
		float fade;
		if (t < CoffinStart)
			fade = Mathf.Max(1f - Mathf.Clamp(t / FadeTime, 0f, 1f), Mathf.Clamp((t - ShotEnd) / FadeTime, 0f, 1f));
		else if (t < ModernStart)
			fade = Mathf.Max(1f - Mathf.Clamp((t - CoffinStart) / FadeTime, 0f, 1f), Mathf.Clamp((t - CoffinEnd) / FadeTime, 0f, 1f));
		else if (t < ModernWalkStart)
			fade = 1f;
		else if (t < _mummyShotStart)
			fade = Mathf.Max(1f - Mathf.Clamp((t - ModernWalkStart) / FadeTime, 0f, 1f), Mathf.Clamp((t - _modernEnd) / FadeTime, 0f, 1f));
		else if (_trapStart < 0f)
			fade = Mathf.Max(1f - Mathf.Clamp((t - _mummyShotStart) / FadeTime, 0f, 1f), _mummyEnd >= 0f ? Mathf.Clamp((t - _mummyEnd) / FadeTime, 0f, 1f) : 0f);
		else
			fade = Mathf.Max(1f - Mathf.Clamp((t - _trapStart) / FadeTime, 0f, 1f), Mathf.Clamp((t - _trapStart - TrapShotTime) / FadeTime, 0f, 1f));
		_fade.Color = new Color(0f, 0f, 0f, fade);

		if (!_inCoffin)
		{
			AnimateVictim(t, dt);
			AnimateCamera(t);
		}
		ReleaseRubble(t);
		if (!_inCoffin && t >= CoffinStart)
			CutToCoffin();
		if (_inCoffin && t < ModernStart)
			AnimateCoffin(t - CoffinStart);
		if (!_modernBuilt && t >= ModernStart)
			BuildModern();
		if (_modernBuilt && t < _mummyShotStart)
			AnimateModern(t, dt);
		if (!_inMummy && t >= _mummyShotStart)
			CutToMummy();
		if (_inMummy && !_cutBack && _trapStart < 0f)
			AnimateMummy(t, t - _mummyShotStart, dt);
		if (_trapStart < 0f && _mummyEnd >= 0f && t >= _mummyEnd + FadeTime)
			CutToTrapRoom(t);
		if (_trapStart >= 0f)
		{
			float title = t - _trapStart;
			_title.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp((title - 0.4f) / 0.8f, 0f, 1f) * (1f - Mathf.Clamp((title - TrapShotTime + 0.9f) / 0.8f, 0f, 1f)));
			_trapCamera.GlobalPosition = _crypt.ToGlobal(TrapCameraPosition + new Vector3(-0.5f, 0f, 0f) * Mathf.SmoothStep(0f, 1f, title / TrapShotTime));
			_trapCamera.LookAt(_crypt.ToGlobal(TrapCameraTarget), Vector3.Up);
		}

		if (!_broke && t >= BreakTime)
			Break();
		if (!_fell && t >= FallTime)
			Fall();

		if (!_cutBack && _trapStart >= 0f && t >= _trapStart + TrapShotTime + FadeTime)
			CutBack();
		if (_cutBack && t >= _trapStart + TrapShotTime + FadeTime * 2f)
			Finish();
	}

	private Vector3 Local(Vector3 p) => PastLevel.ToGlobal(p);

	// A cutscene walker's feet go on whatever floor is under them (up and down stairs, never over a drop), easing
	// between heights so a step up is a step rather than a jump. dt 0 snaps straight there.
	private Vector3 OnFloor(Vector3 at, Node3D walker, float dt)
	{
		var query = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * 1.2f, at + Vector3.Down * 2.5f, Layers.World);
		Godot.Collections.Dictionary hit = walker.GetWorld3D().DirectSpaceState.IntersectRay(query);
		float floor = hit.Count > 0 ? hit["position"].AsVector3().Y : at.Y;
		float y = dt <= 0f || !walker.IsInsideTree() ? floor : Mathf.Lerp(walker.GlobalPosition.Y, floor, 1f - Mathf.Exp(-14f * dt));
		return at with { Y = y };
	}

	// A beat after the tile goes down, fade to black, the title, then the intact room: he walks slowly round the
	// sarcophagus, stopping twice to mourn, and heads on towards where he'll stand when it falls.
	private void Prelude(float dt)
	{
		_preludeTime += dt;
		float p = _preludeTime;
		float black = PreludeDelay + FadeTime;
		float shot = black + TitleTime;
		if (p < black)
		{
			_fade.Color = new Color(0f, 0f, 0f, Mathf.Clamp((p - PreludeDelay) / FadeTime, 0f, 1f));
			return;
		}
		if (!_preludeBuilt)
			BuildPrelude();

		float title = p - black;
		_title.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(title / 0.8f, 0f, 1f) * (1f - Mathf.Clamp((title - TitleTime + 0.8f) / 0.8f, 0f, 1f)));
		if (p < shot)
		{
			_fade.Color = Colors.Black;
			return;
		}
		float w = p - shot;
		_fade.Color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp(w / 0.7f, 0f, 1f));
		AnimateMourner(w, dt);
		if (w >= _preludeShotEnd)
		{
			// On into the collapse: fade, and cut to him walking in under the ceiling.
			_inPrelude = false;
			_time = -FadeTime;
		}
	}

	private void BuildPrelude()
	{
		_preludeBuilt = true;
		if (_hud != null)
			_hud.Visible = false;
		HidePastPlayers();

		_preludeCamera = new Camera3D { Name = "PreludeCamera", Fov = 52f };
		AddChild(_preludeCamera);
		_preludeCamera.GlobalPosition = Local(PreludeCameraPosition);
		_preludeCamera.LookAt(Local(PreludeFocus), Vector3.Up);
		_preludeCamera.MakeCurrent();

		// Along the west side of the sunken floor (which runs x -1.75..1.75, z -2.75..2.75), a stop to mourn facing the
		// sarcophagus, round its north-west corner, along the north side, another stop, round the north-east corner, then
		// off towards the spot under the ceiling: always on the raised floor, and inside the corner pillars.
		var points = new (Vector3 At, bool Mourn)[]
		{
			(new Vector3(-2.25f, 0f, 0.6f), false), (new Vector3(-2.25f, 0f, -0.9f), true),
			(new Vector3(-2.25f, 0f, -3.25f), false), (new Vector3(0.9f, 0f, -3.4f), true),
			(new Vector3(2.3f, 0f, -3.25f), false), (new Vector3(2.3f, 0f, -1.3f), false), (WalkFrom, false),
		};
		_mournRoute.Clear();
		_preludeShotEnd = 0f;
		for (int i = 1; i < points.Length; i++)
		{
			float walk = points[i - 1].At.DistanceTo(points[i].At) / MournWalkSpeed;
			_mournRoute.Add((points[i - 1].At, points[i].At, walk, false));
			_preludeShotEnd += walk;
			if (points[i].Mourn)
			{
				_mournRoute.Add((points[i].At, points[i].At, MournPause, true));
				_preludeShotEnd += MournPause;
			}
		}

		var pivot = new Node3D { Name = "Mourner" };
		PastLevel.AddChild(pivot);
		pivot.GlobalPosition = Local(points[0].At);
		_mourner = new CharacterModel { Name = "Egyptian", Scene = VictimScene, Animate = true, WalkSpeed = 1.3f, StrideLength = 0.9f };
		pivot.AddChild(_mourner);
		AnimateMourner(0f, 0f);
	}

	private void AnimateMourner(float w, float dt)
	{
		if (_mourner == null)
			return;
		var pivot = (Node3D)_mourner.GetParent();
		float left = w;
		float mourn = 0f;
		Vector3 at = _mournRoute[^1].To;
		Vector3 facing = _mournRoute[^1].To - _mournRoute[^1].From;
		foreach ((Vector3 from, Vector3 to, float time, bool mourning) in _mournRoute)
		{
			if (left > time)
			{
				left -= time;
				continue;
			}
			float k = Mathf.Clamp(left / time, 0f, 1f);
			if (mourning)
			{
				at = from;
				// Turns to the sarcophagus, bows his head and folds his hands, then straightens to walk on.
				facing = -from;
				mourn = Mathf.SmoothStep(0f, 1f, Mathf.Clamp(k / 0.25f, 0f, 1f)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp((k - 0.8f) / 0.2f, 0f, 1f)));
			}
			else
			{
				// Easing in and out of each stop.
				at = from.Lerp(to, Mathf.SmoothStep(0f, 1f, k) * 0.35f + k * 0.65f);
				facing = to - from;
			}
			break;
		}
		pivot.GlobalPosition = OnFloor(Local(at), pivot, dt);
		facing.Y = 0f;
		if (facing.LengthSquared() > 0.0001f)
		{
			float yaw = Mathf.Atan2(-facing.X, -facing.Z);
			pivot.Rotation = new Vector3(0f, dt <= 0f ? yaw : Mathf.LerpAngle(pivot.Rotation.Y, yaw, 1f - Mathf.Exp(-4f * dt)), 0f);
		}
		_mourner.HeadPitchDegrees = -30f * mourn;
		_mourner.Reach = 0.22f * mourn;

		// A slow drift across the room, following him with the sarcophagus in the frame.
		Vector3 chest = pivot.GlobalPosition + Vector3.Up * 1.2f;
		_preludeCamera.GlobalPosition = Local(PreludeCameraPosition + new Vector3(Mathf.Lerp(-0.8f, 1.2f, Mathf.Clamp(w / _preludeShotEnd, 0f, 1f)), 0f, 0f));
		Vector3 aim = Local(PreludeFocus).Lerp(chest, 0.7f);
		Basis look = Basis.LookingAt(aim - _preludeCamera.GlobalPosition, Vector3.Up);
		_preludeCamera.GlobalBasis = dt <= 0f ? look : _preludeCamera.GlobalBasis.Orthonormalized().Slerp(look, 1f - Mathf.Exp(-2.5f * dt));
	}
	private Vector3 HoleCenter => (HoleMin + HoleMax) * 0.5f;

	private void BuildOverlay()
	{
		var overlay = new CanvasLayer { Name = "CollapseOverlay", Layer = 20 };
		AddChild(overlay);
		_fade = new ColorRect { Color = Colors.Transparent, MouseFilter = Control.MouseFilterEnum.Ignore };
		_fade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		overlay.AddChild(_fade);

		_title = new Label
		{
			Text = PreludeTitle,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Modulate = Colors.Transparent,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		_title.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		_title.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Cinzel", "Trajan Pro", "Georgia", "Times New Roman", "serif" } });
		_title.AddThemeFontSizeOverride("font_size", 46);
		_title.AddThemeColorOverride("font_color", new Color(0.93f, 0.86f, 0.7f));
		overlay.AddChild(_title);
	}

	private void CutToPast()
	{
		// The prelude's stand-in hands over to the collapse's own Egyptian, at the same spot.
		if (_mourner != null && IsInstanceValid(_mourner))
			_mourner.GetParent().QueueFree();
		if (_preludeCamera != null && IsInstanceValid(_preludeCamera))
			_preludeCamera.QueueFree();
		if (_hud != null)
			_hud.Visible = false;
		HidePastPlayers();
		BreakCeilingForGood();

		_camera = new Camera3D { Name = "CollapseCamera", Fov = 62f };
		AddChild(_camera);
		_camera.GlobalPosition = Local(CameraPosition);
		_camera.MakeCurrent();

		_debris = new Node3D { Name = "CollapseDebris" };
		PastLevel.AddChild(_debris);
		BuildVictim();
		BuildPlug();
		BuildRubble();
		BuildDust();
		AnimateCamera(0f);
	}

	// While the cutscene's Egyptian stands in for him, the real past player's body is hidden, here and on the other
	// machine. He gets it back at the end.
	private void HidePastPlayers() => SetPastPlayersShown(false);

	// The present: "In the modern day..." on black, then the explorer (a stand-in, carrying a torch; the real one is
	// hidden until the end) walking to the stairs, up into the crypt, and to where the real explorer stands.
	private void BuildModern()
	{
		_modernBuilt = true;
		_title.Text = ModernTitle;
		_crypt = Mummy?.GetParent() as Node3D;
		Player explorer = PresentPlayer();
		SetPresentPlayersShown(false);
		if (_crypt == null)
		{
			_modernEnd = ModernWalkStart;
			_mummyShotStart = _modernEnd + FadeTime;
			return;
		}

		_modernPath.Clear();
		_modernPath.Add(ModernWalkFrom);
		_modernPath.Add(StairsBottom);
		_modernPath.Add(StairsTop);
		_modernPath.Add(CryptDoorway);
		Vector3 stand = explorer != null ? _crypt.ToLocal(explorer.GlobalPosition) : CryptDoorway + new Vector3(0f, 0f, 1.2f);
		if (stand.Z > CryptDoorway.Z + 0.3f)
			_modernPath.Add(stand with { Y = CryptDoorway.Y });
		_modernLength = 0f;
		for (int i = 1; i < _modernPath.Count; i++)
			_modernLength += _modernPath[i - 1].DistanceTo(_modernPath[i]);
		_modernEnd = ModernWalkStart + _modernLength / ModernWalkSpeed + 0.4f;
		_mummyShotStart = _modernEnd + FadeTime;

		_explorerWalker = new Node3D { Name = "ExplorerWalker" };
		_crypt.AddChild(_explorerWalker);
		var body = new CharacterModel { Name = "Explorer", Scene = GD.Load<PackedScene>("res://art/explorer.glb"), Animate = true, WalkSpeed = 1.7f, StrideLength = 1.1f };
		_explorerWalker.AddChild(body);
		if (ResourceLoader.Exists("res://gameplay/Torch.tscn") && GD.Load<PackedScene>("res://gameplay/Torch.tscn").Instantiate() is Pickup torch)
		{
			torch.Freeze = true;
			torch.CreateSlotAtStart = false;
			torch.CollisionLayer = 0;
			torch.CollisionMask = 0;
			torch.Ready += () => torch.CollisionLayer = 0;
			_explorerWalker.AddChild(torch);
			body.HeldItem = torch;
		}
		AnimateModern(ModernWalkStart, 0f);
	}

	private void AnimateModern(float t, float dt)
	{
		float title = t - ModernStart;
		_title.Modulate = new Color(1f, 1f, 1f, Mathf.Clamp(title / 0.8f, 0f, 1f) * (1f - Mathf.Clamp((title - TitleTime + 0.8f) / 0.8f, 0f, 1f)));
		if (_explorerWalker == null || t < ModernWalkStart)
			return;

		// Along the path at a steady walk (the stairs as an even slope), turning smoothly at each corner.
		float distance = Mathf.Min((t - ModernWalkStart) * ModernWalkSpeed, _modernLength);
		Vector3 at = _modernPath[^1];
		Vector3 heading = _modernPath[^1] - _modernPath[^2];
		for (int i = 1; i < _modernPath.Count; i++)
		{
			float segment = _modernPath[i - 1].DistanceTo(_modernPath[i]);
			if (distance <= segment)
			{
				at = _modernPath[i - 1].Lerp(_modernPath[i], distance / Mathf.Max(segment, 0.001f));
				heading = _modernPath[i] - _modernPath[i - 1];
				break;
			}
			distance -= segment;
		}
		_explorerWalker.GlobalPosition = OnFloor(_crypt.ToGlobal(at), _explorerWalker, dt);
		heading.Y = 0f;
		if (heading.LengthSquared() > 0.0001f)
		{
			float yaw = Mathf.Atan2(-heading.X, -heading.Z);
			_explorerWalker.Rotation = new Vector3(0f, dt <= 0f ? yaw : Mathf.LerpAngle(_explorerWalker.Rotation.Y, yaw, 1f - Mathf.Exp(-5f * dt)), 0f);
		}

		// Following from behind and a little to the side, looking up the way he's going.
		Vector3 forward = -_explorerWalker.GlobalBasis.Z.Normalized();
		Vector3 side = _explorerWalker.GlobalBasis.X.Normalized();
		Vector3 eye = _explorerWalker.GlobalPosition - forward * 2.2f + Vector3.Up * 1.5f + side * 0.15f;
		Vector3 aim = _explorerWalker.GlobalPosition + Vector3.Up * 1.25f + forward * 1.6f;
		_camera.GlobalPosition = dt <= 0f ? eye : _camera.GlobalPosition.Lerp(eye, 1f - Mathf.Exp(-4f * dt));
		Basis look = Basis.LookingAt(aim - _camera.GlobalPosition, Vector3.Up);
		_camera.GlobalBasis = dt <= 0f ? look : _camera.GlobalBasis.Orthonormalized().Slerp(look, 1f - Mathf.Exp(-5f * dt));
		_camera.HOffset = 0f;
		_camera.VOffset = 0f;
	}

	// The real explorer is hidden while the cutscene's stand-in walks in and until the end, here and on the other
	// machine (with what they carry).
	private void SetPresentPlayersShown(bool shown)
	{
		if (GameState.Instance == null)
			return;
		foreach (KeyValuePair<long, PlayerInfo> entry in GameState.Instance.Players)
		{
			if (entry.Value.Era != Era.Present || GameState.Instance.GetPlayerNode(entry.Key) is not Player player)
				continue;
			player.Visible = shown;
		}
	}

	private void SetPastPlayersShown(bool shown)
	{
		if (GameState.Instance == null)
			return;
		foreach (KeyValuePair<long, PlayerInfo> entry in GameState.Instance.Players)
		{
			if (entry.Value.Era != Era.Past || GameState.Instance.GetPlayerNode(entry.Key) is not Player player)
				continue;
			player.Visible = shown;
			player.Inventory?.SelectedItem?.SetDeferred(Node3D.PropertyName.Visible, shown);
		}
	}

	// From here on the past player is the mummy: their character takes the cutscene mummy's place (and look), and the
	// cutscene's one goes.
	private void HandMummyToPastPlayer()
	{
		if (GameState.Instance == null || Mummy == null || !IsInstanceValid(Mummy))
			return;
		foreach (KeyValuePair<long, PlayerInfo> entry in GameState.Instance.Players)
		{
			if (entry.Value.Era != Era.Past || GameState.Instance.GetPlayerNode(entry.Key) is not Player player)
				continue;
			player.BecomeMummy(Mummy.Scene, Mummy.GlobalTransform.Orthonormalized(), Mummy.Scale.X);
			Mummy.QueueFree();
			return;
		}
	}

	// The explorer is where they ran to, by the sealed wall, turned back towards the crypt (and the trap room beyond).
	// Only their own machine moves them; the other follows by sync.
	private void PlaceExplorerAtWall()
	{
		Player explorer = PresentPlayer();
		if (explorer == null || !explorer.IsMultiplayerAuthority() || _crypt == null || _scaredStart < 0f)
			return;
		explorer.GlobalPosition = _crypt.ToGlobal(FleeStop with { X = _crypt.ToLocal(_fleeTo).X });
		explorer.Rotation = new Vector3(0f, _crypt.GlobalRotation.Y + Mathf.Pi, 0f);
		explorer.Velocity = Vector3.Zero;
		if (explorer.GetNodeOrNull<Camera3D>("Camera") is Camera3D camera)
			camera.Rotation = Vector3.Zero;
	}

	private Player PresentPlayer()
	{
		if (GameState.Instance == null)
			return null;
		foreach (KeyValuePair<long, PlayerInfo> entry in GameState.Instance.Players)
		{
			if (entry.Value.Era == Era.Present && GameState.Instance.GetPlayerNode(entry.Key) is Player player)
				return player;
		}
		return null;
	}

	// Swaps the whole past ceiling for copies of the present's pieces, which leave the hole.
	private void BreakCeilingForGood()
	{
		if (PastCeiling == null)
			return;
		for (int i = 0; i < PastCeiling.Mesh.GetSurfaceCount(); i++)
		{
			Material material = PastCeiling.GetActiveMaterial(i);
			string name = material?.ResourceName ?? "";
			if (name.Contains("Ceiling"))
				_starsMaterial = material;
			else if (name.Contains("Stone"))
				_stoneMaterial = material;
		}
		foreach (MeshInstance3D piece in BrokenCeiling)
		{
			if (piece?.Mesh == null)
				continue;
			var copy = new MeshInstance3D { Name = "Past" + piece.Name, Mesh = piece.Mesh, Transform = piece.Transform };
			for (int i = 0; i < piece.Mesh.GetSurfaceCount(); i++)
			{
				string name = piece.GetActiveMaterial(i)?.ResourceName ?? "";
				copy.SetSurfaceOverrideMaterial(i, name.Contains("Ceiling") ? _starsMaterial : _stoneMaterial);
			}
			PastLevel.AddChild(copy);
		}
		PastCeiling.Visible = false;
	}

	private void BuildVictim()
	{
		_victimPivot = new Node3D { Name = "Victim" };
		_debris.AddChild(_victimPivot);
		_victimPivot.GlobalPosition = Local(WalkFrom);
		Vector3 heading = Local(VictimPosition) - Local(WalkFrom);
		heading.Y = 0f;
		_victimPivot.LookAt(_victimPivot.GlobalPosition + heading, Vector3.Up);
		// Animate on: the walk cycle follows his movement, and he idles (breathing, shifting his weight) once he stops.
		// A slow, short-stepped walk.
		_victim = new CharacterModel { Name = "Egyptian", Scene = VictimScene, Animate = true, WalkSpeed = 1.3f, StrideLength = 0.9f };
		_victimPivot.AddChild(_victim);
	}

	// The slab that fills the hole until it breaks: a grid of chunks, starry underneath like the rest of the ceiling.
	private void BuildPlug()
	{
		Vector3 size = HoleMax - HoleMin;
		float cellX = size.X / PlugCells;
		float cellZ = size.Z / PlugCells;
		for (int x = 0; x < PlugCells; x++)
		{
			for (int z = 0; z < PlugCells; z++)
			{
				var center = new Vector3(HoleMin.X + cellX * (x + 0.5f), (HoleMin.Y + HoleMax.Y) * 0.5f, HoleMin.Z + cellZ * (z + 0.5f));
				var chunkSize = new Vector3(cellX - 0.006f, size.Y, cellZ - 0.006f);
				RigidBody3D chunk = MakeStone(chunkSize, center);
				chunk.Freeze = true;
				chunk.FreezeMode = RigidBody3D.FreezeModeEnum.Static;
				// The underside: the ceiling's stars, mapped the way the ceiling maps them (u = x / 2, v = z / 2 + 1 in
				// the level's space), so the slab looks like part of it until it falls.
				var stars = (_starsMaterial as BaseMaterial3D)?.Duplicate() as BaseMaterial3D;
				if (stars != null)
				{
					stars.Uv1Triplanar = true;
					stars.Uv1Scale = new Vector3(0.5f, 0.5f, -0.5f);
					stars.Uv1Offset = new Vector3(center.X * 0.5f, 0f, center.Z * 0.5f + 1f);
				}
				chunk.AddChild(new MeshInstance3D
				{
					Mesh = new PlaneMesh { Size = new Vector2(chunkSize.X, chunkSize.Z), Material = stars },
					Position = new Vector3(0f, -size.Y * 0.5f - 0.002f, 0f),
					RotationDegrees = new Vector3(180f, 0f, 0f),
				});
				_plug.Add(chunk);
			}
		}
	}

	// Pebbles that trickle down before the slab goes, then chipped stones of all sizes (a few big slabs, many middling
	// lumps) and a pour of gravel after it.
	private void BuildRubble()
	{
		_rockShades = Rubble.Shades(_stoneMaterial, _rng, 5);
		for (int i = 0; i < PebbleCount; i++)
		{
			float s = _rng.RandfRange(0.03f, 0.07f);
			Vector3 at = new(_rng.RandfRange(HoleMin.X + 0.2f, HoleMax.X - 0.2f), HoleMin.Y - 0.05f, _rng.RandfRange(HoleMin.Z + 0.2f, HoleMax.Z - 0.2f));
			_pending.Add((_rng.RandfRange(0.9f, BreakTime), MakeRock(new Vector3(s, s * 0.8f, s * 1.1f), at)));
		}
		for (int i = 0; i < RubbleCount; i++)
		{
			// Mostly middling lumps, the odd big slab.
			float s = i < 4 ? _rng.RandfRange(0.4f, 0.55f) : _rng.RandfRange(0.14f, 0.34f);
			Vector3 at = new(_rng.RandfRange(HoleMin.X, HoleMax.X), HoleMax.Y + _rng.RandfRange(0.1f, 1.2f), _rng.RandfRange(HoleMin.Z, HoleMax.Z));
			RigidBody3D rock = MakeRock(new Vector3(s, s * _rng.RandfRange(0.45f, 0.8f), s * _rng.RandfRange(0.7f, 1.2f)), at);
			if (s > 0.2f)
				PuffOnLanding(rock);
			_pending.Add((_rng.RandfRange(BreakTime + 0.05f, RubbleEnd), rock));
		}
		for (int i = 0; i < AftershockCount; i++)
		{
			float s = _rng.RandfRange(0.05f, 0.18f);
			// Round the rim of the hole, where the ceiling is still breaking away.
			bool alongX = _rng.Randf() < 0.5f;
			float x = alongX ? _rng.RandfRange(HoleMin.X, HoleMax.X) : (_rng.Randf() < 0.5f ? HoleMin.X + 0.1f : HoleMax.X - 0.1f);
			float z = alongX ? (_rng.Randf() < 0.5f ? HoleMin.Z + 0.1f : HoleMax.Z - 0.1f) : _rng.RandfRange(HoleMin.Z, HoleMax.Z);
			_pending.Add((_rng.RandfRange(RubbleEnd, AftershockEnd), MakeRock(new Vector3(s, s * 0.7f, s * 0.9f), new Vector3(x, HoleMin.Y - 0.05f, z))));
		}
		for (int i = 0; i < GravelCount; i++)
		{
			float s = _rng.RandfRange(0.035f, 0.09f);
			Vector3 at = new(_rng.RandfRange(HoleMin.X + 0.1f, HoleMax.X - 0.1f), HoleMax.Y + _rng.RandfRange(0f, 0.8f), _rng.RandfRange(HoleMin.Z + 0.1f, HoleMax.Z - 0.1f));
			_pending.Add((_rng.RandfRange(BreakTime + 0.1f, RubbleEnd + 0.5f), MakeRock(new Vector3(s, s * 0.7f, s), at)));
		}
	}

	// A chipped lump of the ceiling's stone (hidden and frozen until released), with a collider of its own shape.
	private RigidBody3D MakeRock(Vector3 size, Vector3 localCenter)
	{
		(ArrayMesh mesh, Vector3[] hull) = Rubble.Rock(size, _rng);
		var body = new RigidBody3D
		{
			CollisionLayer = Layers.World,
			CollisionMask = Layers.World,
			Mass = Mathf.Max(0.3f, size.X * size.Y * size.Z * 2000f),
			Freeze = true,
			FreezeMode = RigidBody3D.FreezeModeEnum.Static,
			Visible = false,
			// Stone grinds rather than slides.
			PhysicsMaterialOverride = new PhysicsMaterial { Friction = 0.9f, Bounce = 0.05f },
		};
		body.AddChild(new CollisionShape3D { Shape = new ConvexPolygonShape3D { Points = hull } });
		body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = _rockShades[_rng.RandiRange(0, _rockShades.Length - 1)] });
		_debris.AddChild(body);
		body.GlobalPosition = Local(localCenter);
		body.Rotation = new Vector3(_rng.RandfRange(-3f, 3f), _rng.RandfRange(-3f, 3f), _rng.RandfRange(-3f, 3f));
		return body;
	}

	// The first hard landing of a big stone kicks up a little dust.
	private void PuffOnLanding(RigidBody3D rock)
	{
		rock.ContactMonitor = true;
		rock.MaxContactsReported = 1;
		bool landed = false;
		rock.BodyEntered += _ =>
		{
			if (landed || rock.LinearVelocity.Length() < 1.5f || _landingPuffs >= MaxLandingPuffs || _burst == null)
				return;
			landed = true;
			_landingPuffs++;
			var puff = (CpuParticles3D)_burst.Duplicate();
			puff.Amount = 10;
			puff.EmissionRingRadius = 0.25f;
			puff.EmissionRingInnerRadius = 0.05f;
			puff.InitialVelocityMax = 0.9f;
			puff.ScaleAmountMax = 1.1f;
			_debris.AddChild(puff);
			puff.GlobalPosition = rock.GlobalPosition;
			puff.Emitting = true;
		};
	}

	// A block of the ceiling's stone at a point in PastLevel's space. Hidden ones wait, frozen, until released, and
	// start at a random angle so the rubble doesn't land in neat rows.
	private RigidBody3D MakeStone(Vector3 size, Vector3 localCenter, bool hidden = false)
	{
		var body = new RigidBody3D
		{
			CollisionLayer = Layers.World,
			CollisionMask = Layers.World,
			Mass = Mathf.Max(1f, size.X * size.Y * size.Z * 2400f),
			Freeze = hidden,
			FreezeMode = RigidBody3D.FreezeModeEnum.Static,
			Visible = !hidden,
		};
		body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
		body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size, Material = _stoneMaterial } });
		_debris.AddChild(body);
		body.GlobalPosition = Local(localCenter);
		if (hidden)
			body.Rotation = new Vector3(_rng.RandfRange(-0.4f, 0.4f), _rng.RandfRange(-3f, 3f), _rng.RandfRange(-0.4f, 0.4f));
		return body;
	}

	private void BuildDust()
	{
		// A soft round puff, faded at the edges.
		var puffShape = new Gradient
		{
			Offsets = new[] { 0f, 1f },
			Colors = new[] { Colors.White, new Color(1f, 1f, 1f, 0f) },
		};
		var puff = new GradientTexture2D
		{
			Gradient = puffShape,
			Fill = GradientTexture2D.FillEnum.Radial,
			FillFrom = new Vector2(0.5f, 0.5f),
			FillTo = new Vector2(0.5f, 0f),
			Width = 64,
			Height = 64,
		};
		var dust = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			// Plain billboards that keep the particle's scale: in Particles mode, particles waiting to be emitted (scale 0)
			// still showed as a solid black puff at the emitter.
			BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
			BillboardKeepScale = true,
			VertexColorUseAsAlbedo = true,
			AlbedoColor = Colors.White,
			AlbedoTexture = puff,
		};
		// Fades in, then out over each puff's life.
		var dustColor = new Color(0.42f, 0.37f, 0.3f);
		var fade = new Gradient
		{
			Offsets = new[] { 0f, 0.2f, 1f },
			Colors = new[] { dustColor with { A = 0f }, dustColor with { A = 0.28f }, dustColor with { A = 0f } },
		};

		Vector3 size = HoleMax - HoleMin;
		_trickle = new CpuParticles3D
		{
			Name = "DustTrickle",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Amount = 70,
			Lifetime = 2.6f,
			Mesh = new QuadMesh { Size = new Vector2(0.25f, 0.25f), Material = dust },
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
			EmissionBoxExtents = new Vector3(size.X * 0.5f, 0.02f, size.Z * 0.5f),
			Direction = Vector3.Down,
			Spread = 12f,
			InitialVelocityMin = 0.2f,
			InitialVelocityMax = 0.6f,
			Gravity = new Vector3(0f, -1.6f, 0f),
			ScaleAmountMin = 0.4f,
			ScaleAmountMax = 1.6f,
			ColorRamp = fade,
		};

		_burst = new CpuParticles3D
		{
			Name = "DustBurst",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			OneShot = true,
			Explosiveness = 0.85f,
			Amount = 50,
			Lifetime = 3.0f,
			Mesh = new QuadMesh { Size = new Vector2(0.5f, 0.5f), Material = dust },
			EmissionShape = CpuParticles3D.EmissionShapeEnum.Ring,
			EmissionRingAxis = Vector3.Up,
			EmissionRingRadius = 0.9f,
			EmissionRingInnerRadius = 0.5f,
			EmissionRingHeight = 0.1f,
			Direction = Vector3.Up,
			Spread = 70f,
			InitialVelocityMin = 0.5f,
			InitialVelocityMax = 1.6f,
			Gravity = new Vector3(0f, -0.25f, 0f),
			DampingMin = 1.2f,
			DampingMax = 2.0f,
			ScaleAmountMin = 0.6f,
			ScaleAmountMax = 1.8f,
			ColorRamp = fade,
		};
	}

	// Particles are only added when they start, where they should be.
	private void StartDust(CpuParticles3D dust, Vector3 localPosition)
	{
		_debris.AddChild(dust);
		dust.GlobalPosition = Local(localPosition);
	}

	private void AnimateVictim(float t, float dt)
	{
		if (_victim == null)
			return;
		// He walks in, slowing to a stop as the rumble grows, looks up, then throws his arms up as it gives.
		if (!_fell)
		{
			float k = Mathf.Clamp(t / WalkEnd, 0f, 1f);
			_victimPivot.GlobalPosition = OnFloor(Local(WalkFrom.Lerp(VictimPosition, 1f - (1f - k) * (1f - k))), _victimPivot, dt);
		}
		float look = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - LookUpStart) / (LookUpEnd - LookUpStart), 0f, 1f));
		_victim.HeadPitchDegrees = 40f * look * (_fell ? 0.3f : 1f);
		float shield = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - (BreakTime - 0.4f)) / 0.45f, 0f, 1f));
		_victim.Shield = _fell ? Mathf.Max(0f, shield * (1f - (t - FallTime) * 2f)) : shield;
	}

	private void AnimateCamera(float t)
	{
		// His chest as he walks in; once he's down, where he stood.
		Vector3 chest = (_victimPivot != null && !_fell ? _victimPivot.GlobalPosition : Local(VictimPosition)) + PastLevel.GlobalBasis * new Vector3(0f, 1.3f, 0f);
		Vector3 ceiling = Local(new Vector3(HoleCenter.X, HoleMin.Y, HoleCenter.Z));
		Vector3 ground = Local(VictimPosition + new Vector3(0f, 0.35f, -0.8f));
		// Up to the cracking ceiling, then follows the slab down onto him.
		float up = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - LookUpStart) / (LookUpEnd - LookUpStart), 0f, 1f));
		float down = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((t - BreakTime - 0.1f) / 0.7f, 0f, 1f));
		Vector3 target = chest.Lerp(ceiling, up * 0.42f).Lerp(ground, down);
		// A slow push-in.
		Vector3 from = Local(CameraPosition);
		Vector3 to = Local(CameraPosition.Lerp(VictimPosition + new Vector3(0f, 1.45f, 0f), 0.22f));
		_camera.GlobalPosition = from.Lerp(to, Mathf.SmoothStep(0f, 1f, t / ShotEnd));
		_camera.LookAt(target, Vector3.Up);

		float shake = t < BreakTime
			? Mathf.Lerp(0.004f, 0.02f, Mathf.Clamp(t / BreakTime, 0f, 1f))
			: Mathf.Lerp(0.07f, 0.0f, Mathf.Clamp((t - BreakTime) / 3.2f, 0f, 1f));
		_camera.HOffset = (_rng.Randf() - 0.5f) * 2f * shake;
		_camera.VOffset = (_rng.Randf() - 0.5f) * 2f * shake;
		// Starting a little below the ceiling keeps the puffs from cutting into it.
		if (_trickle != null && !_trickle.IsInsideTree() && t >= 0.4f)
			StartDust(_trickle, new Vector3(HoleCenter.X, HoleMin.Y - 0.25f, HoleCenter.Z));
	}

	private void ReleaseRubble(float t)
	{
		for (int i = _pending.Count - 1; i >= 0; i--)
		{
			if (t < _pending[i].Time)
				continue;
			RigidBody3D stone = _pending[i].Body;
			stone.Visible = true;
			stone.Freeze = false;
			stone.AngularVelocity = new Vector3(_rng.RandfRange(-4f, 4f), _rng.RandfRange(-4f, 4f), _rng.RandfRange(-4f, 4f));
			_pending.RemoveAt(i);
		}
	}

	private void Break()
	{
		_broke = true;
		// Each square of the slab cracks into a few chipped chunks as it gives (some still showing the painted stars
		// underneath), which fall with a bit of a tumble.
		var starShade = (_starsMaterial as BaseMaterial3D)?.Duplicate() as BaseMaterial3D;
		if (starShade != null)
		{
			starShade.Uv1Triplanar = true;
			starShade.Uv1Scale = Vector3.One * 1.2f;
		}
		Vector3 cell = HoleMax - HoleMin;
		cell = new Vector3(cell.X / PlugCells, cell.Y, cell.Z / PlugCells);
		foreach (RigidBody3D chunk in _plug)
		{
			Vector3 at = PastLevel.ToLocal(chunk.GlobalPosition);
			int pieces = _rng.RandiRange(2, 3);
			for (int i = 0; i < pieces; i++)
			{
				var size = new Vector3(cell.X * _rng.RandfRange(0.55f, 0.8f), cell.Y * _rng.RandfRange(0.8f, 1.1f), cell.Z * _rng.RandfRange(0.55f, 0.8f));
				Vector3 offset = new(_rng.RandfRange(-0.25f, 0.25f) * cell.X, 0f, _rng.RandfRange(-0.25f, 0.25f) * cell.Z);
				RigidBody3D piece = MakeRock(size, at + offset);
				if (starShade != null && i == 0)
					piece.GetChild<MeshInstance3D>(1).MaterialOverride = starShade;
				piece.Visible = true;
				piece.Freeze = false;
				piece.LinearVelocity = new Vector3(_rng.RandfRange(-0.5f, 0.5f), _rng.RandfRange(-1.5f, -0.4f), _rng.RandfRange(-0.5f, 0.5f));
				piece.AngularVelocity = new Vector3(_rng.RandfRange(-3f, 3f), _rng.RandfRange(-1.5f, 1.5f), _rng.RandfRange(-3f, 3f));
				if (i == 0)
					PuffOnLanding(piece);
			}
			chunk.QueueFree();
		}
		_plug.Clear();
		Events.Instance?.EmitSignal(Events.SignalName.PlaySfx, "ceiling_collapse", Local(HoleCenter));
	}

	// Struck down: he goes limp and is knocked down towards the middle of the hole, under where the rubble pile will be,
	// and it lands on him.
	private void Fall()
	{
		_fell = true;
		StartDust(_burst, VictimPosition + new Vector3(0f, 0.15f, -0.8f));
		Vector3 toHole = Local(new Vector3(HoleCenter.X, 0f, HoleCenter.Z)) - _victimPivot.GlobalPosition;
		toHole.Y = 0f;
		_victim.StartRagdoll(toHole.Normalized() * 12f + Vector3.Down * 10f);
	}

	// The same room, later: the royal coffin lies open on the floor with him inside, its lid standing up on one side.
	private void CutToCoffin()
	{
		_inCoffin = true;
		if (_trickle.IsInsideTree())
			_trickle.Emitting = false;
		_camera.HOffset = 0f;
		_camera.VOffset = 0f;

		_coffin = new Node3D { Name = "Coffin" };
		_debris.AddChild(_coffin);
		_coffin.GlobalTransform = new Transform3D(PastLevel.GlobalBasis * new Basis(Vector3.Up, Mathf.DegToRad(CoffinYawDegrees)), Local(CoffinPosition));

		Material wood = RoyalCoffin?.GetActiveMaterial(0) ?? _stoneMaterial;
		Material gold = RoyalCoffin != null && RoyalCoffin.Mesh.GetSurfaceCount() > 1 ? RoyalCoffin.GetActiveMaterial(1) : wood;
		Aabb box = RoyalCoffin?.GetAabb() ?? new Aabb(new Vector3(-0.33f, 0f, -0.2f), new Vector3(0.66f, 2.1f, 0.4f));
		float[] halfWidths = CoffinProfile(RoyalCoffin?.Mesh, box);
		float halfLength = box.Size.Y * 0.5f;
		float widest = 0f;
		foreach (float w in halfWidths)
			widest = Mathf.Max(widest, w);

		// The open coffin: a body-shaped trough following the royal coffin's outline, gold outside, wood inside.
		_coffin.AddChild(new MeshInstance3D { Name = "Base", Mesh = CoffinShell(halfWidths, halfLength, gold, wood) });

		// Him, on his back, feet towards -Z (the coffin's foot end), face up, arms at his sides.
		var body = new CharacterModel { Name = "Entombed", Scene = VictimScene, Animate = false, ArmDownDegrees = 46f, ElbowBendDegrees = 6f };
		_coffin.AddChild(body);
		body.Transform = new Transform3D(new Basis(Vector3.Right, Mathf.DegToRad(90f)), new Vector3(0f, 0.16f, -halfLength + 0.16f));
		WrapInBandages(-halfLength + 0.16f);

		// The lid is the royal coffin itself, flattened, so its outline is the trough's. It hinges on the trough's
		// widest +X edge and lies face up, head end towards +Z, its back on the walls when shut.
		_lidHinge = new Node3D { Name = "LidHinge", Position = new Vector3(widest, CoffinWallHeight, 0f) };
		_coffin.AddChild(_lidHinge);
		if (RoyalCoffin?.Mesh != null)
		{
			var lid = new MeshInstance3D { Name = "Lid", Mesh = RoyalCoffin.Mesh };
			for (int i = 0; i < RoyalCoffin.Mesh.GetSurfaceCount(); i++)
				lid.SetSurfaceOverrideMaterial(i, RoyalCoffin.GetActiveMaterial(i));
			var flat = new Basis(Vector3.Right, Mathf.DegToRad(90f)) * Basis.FromScale(new Vector3(1f, 1f, LidDepthScale));
			lid.Transform = new Transform3D(flat, new Vector3(-widest - box.GetCenter().X, box.End.Z * LidDepthScale, -box.GetCenter().Y));
			_lidHinge.AddChild(lid);
		}
		_lidHinge.Rotation = new Vector3(0f, 0f, -Mathf.DegToRad(LidOpenDegrees));

		_slamDust = (CpuParticles3D)_burst.Duplicate();
		_slamDust.EmissionRingRadius = 0.7f;
		_slamDust.EmissionRingInnerRadius = 0.3f;
		_slamDust.Amount = 30;
		AnimateCoffin(0f);
	}

	// Linen strips wound round him where he lies (feet at feetZ, head towards +Z, on his back): bands round the feet,
	// legs, body (arms and all), neck and head, each at a slightly different angle, two straps crossing his chest, and a
	// loose end trailing off the side.
	private void WrapInBandages(float feetZ)
	{
		var linen = new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.8f, 0.66f), Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
		if (ResourceLoader.Exists("res://art/mummy_TX_Bandage.png"))
		{
			linen.AlbedoTexture = GD.Load<Texture2D>("res://art/mummy_TX_Bandage.png");
			linen.Uv1Triplanar = true;
			linen.Uv1Scale = Vector3.One * 3f;
			linen.AlbedoColor = new Color(0.95f, 0.92f, 0.85f);
		}
		var rng = new RandomNumberGenerator { Seed = 4242 };
		var wraps = new Node3D { Name = "Bandages" };
		_coffin.AddChild(wraps);
		// Along the body from the feet: how far up it, and the half-width and half-height of the body there.
		var bands = new (float Along, float Width, float Height)[]
		{
			(0.06f, 0.11f, 0.08f), (0.16f, 0.13f, 0.08f), (0.3f, 0.14f, 0.09f), (0.44f, 0.15f, 0.1f), (0.58f, 0.17f, 0.11f),
			(0.72f, 0.19f, 0.12f), (0.86f, 0.21f, 0.13f), (0.98f, 0.24f, 0.14f), (1.1f, 0.26f, 0.14f), (1.22f, 0.27f, 0.14f),
			(1.34f, 0.27f, 0.14f), (1.45f, 0.24f, 0.13f), (1.55f, 0.08f, 0.07f), (1.65f, 0.11f, 0.11f), (1.75f, 0.11f, 0.11f),
		};
		const float centreY = 0.25f;
		foreach ((float along, float width, float height) in bands)
		{
			var band = new MeshInstance3D
			{
				Mesh = new CylinderMesh { TopRadius = 1f, BottomRadius = 1f, Height = rng.RandfRange(0.07f, 0.1f), CapTop = false, CapBottom = false, RadialSegments = 20, Rings = 1 },
				MaterialOverride = linen,
			};
			wraps.AddChild(band);
			// The cylinder's axis turned to run along the body, squashed to the body's shape, and tipped a little.
			var tip = new Basis(Vector3.Up, rng.RandfRange(-0.25f, 0.25f)) * new Basis(Vector3.Forward, rng.RandfRange(-0.15f, 0.15f));
			band.Transform = new Transform3D(tip * new Basis(Vector3.Right, Mathf.DegToRad(90f)) * Basis.FromScale(new Vector3(width + 0.012f, 1f, height + 0.012f)),
				new Vector3(rng.RandfRange(-0.01f, 0.01f), centreY, feetZ + along));
		}
		// Two straps crossing the chest, over the arms.
		foreach (float side in new[] { 1f, -1f })
		{
			var strap = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.07f, 0.006f, 0.62f) }, MaterialOverride = linen };
			wraps.AddChild(strap);
			strap.Transform = new Transform3D(new Basis(Vector3.Up, side * 0.55f), new Vector3(0f, centreY + 0.145f, feetZ + 1.18f));
		}
		// A loose end hanging over the side of the body.
		var loose = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.24f, 0.005f, 0.07f) }, MaterialOverride = linen };
		wraps.AddChild(loose);
		loose.Transform = new Transform3D(new Basis(Vector3.Back, 0.9f), new Vector3(0.26f, centreY - 0.02f, feetZ + 0.8f));
	}

	// How wide the coffin is (half, from its centre line) at evenly spaced points from its foot (mesh Y min) to its head.
	private static float[] CoffinProfile(Mesh mesh, Aabb box)
	{
		var half = new float[CoffinStations];
		if (mesh == null)
		{
			System.Array.Fill(half, box.Size.X * 0.5f);
			return half;
		}
		float step = box.Size.Y / (CoffinStations - 1);
		float centre = box.GetCenter().X;
		for (int s = 0; s < mesh.GetSurfaceCount(); s++)
		{
			foreach (Vector3 v in mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex].AsVector3Array())
			{
				int i = Mathf.Clamp(Mathf.RoundToInt((v.Y - box.Position.Y) / step), 0, CoffinStations - 1);
				half[i] = Mathf.Max(half[i], Mathf.Abs(v.X - centre));
			}
		}
		// Stations no vertex fell near take their neighbours' width.
		for (int i = 1; i < CoffinStations; i++)
			half[i] = half[i] > 0f ? half[i] : half[i - 1];
		for (int i = CoffinStations - 2; i >= 0; i--)
			half[i] = half[i] > 0f ? half[i] : half[i + 1];
		for (int i = 0; i < CoffinStations; i++)
			half[i] = Mathf.Max(half[i], CoffinWall * 3f);
		return half;
	}

	// An open trough following the outline (feet at -Z, head at +Z): outer wall and rim in gold, inner wall and floor
	// in wood.
	private static ArrayMesh CoffinShell(float[] halfWidths, float halfLength, Material outside, Material inside)
	{
		int n = halfWidths.Length;
		float h = CoffinWallHeight;
		float floor = 0.03f;
		// The outline loop: up the +X side from the foot, back down the -X side. Inner loop inset by the wall.
		var outer = new Vector3[n * 2];
		var inner = new Vector3[n * 2];
		for (int i = 0; i < n; i++)
		{
			float z = -halfLength + 2f * halfLength * i / (n - 1);
			float zIn = i == 0 ? z + CoffinWall : i == n - 1 ? z - CoffinWall : z;
			float w = halfWidths[i];
			outer[i] = new Vector3(w, 0f, z);
			outer[2 * n - 1 - i] = new Vector3(-w, 0f, z);
			inner[i] = new Vector3(w - CoffinWall, 0f, zIn);
			inner[2 * n - 1 - i] = new Vector3(-(w - CoffinWall), 0f, zIn);
		}

		var gold = new SurfaceTool();
		gold.Begin(Mesh.PrimitiveType.Triangles);
		var wood = new SurfaceTool();
		wood.Begin(Mesh.PrimitiveType.Triangles);
		Vector3 up = Vector3.Up * h;
		for (int i = 0; i < outer.Length; i++)
		{
			int j = (i + 1) % outer.Length;
			Vector3 along = outer[j] - outer[i];
			Vector3 outward = new Vector3(along.Z, 0f, -along.X).Normalized();
			if (outward.Dot(outer[i] + outer[j]) < 0f)
				outward = -outward;
			Quad(gold, outer[i], outer[j], outer[j] + up, outer[i] + up, outward);
			Quad(gold, outer[i] + up, outer[j] + up, inner[j] + up, inner[i] + up, Vector3.Up);
			Quad(wood, inner[i] + Vector3.Up * floor, inner[j] + Vector3.Up * floor, inner[j] + up, inner[i] + up, -outward);
		}
		// The floor, strip by strip across the inner outline.
		for (int i = 0; i < n - 1; i++)
		{
			Vector3 lift = Vector3.Up * floor;
			Quad(wood, inner[i] + lift, inner[i + 1] + lift, inner[2 * n - 2 - i] + lift, inner[2 * n - 1 - i] + lift, Vector3.Up);
		}

		var mesh = new ArrayMesh();
		gold.SetMaterial(outside);
		gold.Commit(mesh);
		wood.SetMaterial(inside);
		wood.Commit(mesh);
		return mesh;
	}

	// Two triangles facing `normal` (Godot's front faces wind clockwise, seen from the front).
	private static void Quad(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
	{
		Tri(tool, a, b, c, normal);
		Tri(tool, a, c, d, normal);
	}

	private static void Tri(SurfaceTool tool, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
	{
		if ((b - a).Cross(c - a).Dot(normal) > 0f)
			(b, c) = (c, b);
		foreach (Vector3 v in new[] { a, b, c })
		{
			tool.SetNormal(normal);
			tool.SetUV(new Vector2(v.X + v.Y, v.Z));
			tool.AddVertex(v);
		}
	}

	private void AnimateCoffin(float c)
	{
		// The lid falls shut faster and faster, lands with a thump and settles with a small bounce.
		float k = Mathf.Clamp((c - LidDelay) / LidTime, 0f, 1f);
		float angle = LidOpenDegrees * (1f - k * k);
		float after = c - LidDelay - LidTime;
		if (after > 0f)
			angle = 2.5f * Mathf.Abs(Mathf.Sin(after * 14f)) * Mathf.Exp(-after * 9f);
		_lidHinge.Rotation = new Vector3(0f, 0f, -Mathf.DegToRad(angle));
		if (!_slammed && k >= 1f)
		{
			_slammed = true;
			_debris.AddChild(_slamDust);
			_slamDust.GlobalPosition = _coffin.GlobalPosition + Vector3.Up * 0.1f;
			_slamDust.Emitting = true;
			Events.Instance?.EmitSignal(Events.SignalName.PlaySfx, "coffin_close", _coffin.GlobalPosition);
		}

		// Looking down into the coffin from beside it, drifting in slowly.
		Vector3 from = _coffin.ToGlobal(CoffinCameraPosition);
		Vector3 to = _coffin.ToGlobal(CoffinCameraPosition * 0.85f + new Vector3(0f, 0f, 0.05f));
		_camera.GlobalPosition = from.Lerp(to, Mathf.SmoothStep(0f, 1f, c / (CoffinEnd - CoffinStart)));
		_camera.LookAt(_coffin.ToGlobal(new Vector3(0f, 0.2f, 0.25f)), Vector3.Up);
		float shake = after > 0f ? Mathf.Lerp(0.035f, 0f, Mathf.Clamp(after / 0.8f, 0f, 1f)) : 0f;
		_camera.HOffset = (_rng.Randf() - 0.5f) * 2f * shake;
		_camera.VOffset = (_rng.Randf() - 0.5f) * 2f * shake;
	}

	// The present's crypt: the royal coffin there rattles, bursts open, and the mummy steps out.
	private void CutToMummy()
	{
		_inMummy = true;
		if (_explorerWalker != null && IsInstanceValid(_explorerWalker))
			_explorerWalker.QueueFree();
		_camera.HOffset = 0f;
		_camera.VOffset = 0f;
		_crypt = Mummy?.GetParent() as Node3D;
		if (_crypt == null)
		{
			// Nothing to show: end here.
			_mummyEnd = _time;
			return;
		}
		_cryptDebris = new Node3D { Name = "CoffinDebris" };
		_crypt.AddChild(_cryptDebris);
		if (RoyalCoffin != null)
			_coffinRest = RoyalCoffin.Transform;
		_mummyStart = Mummy.Position;
		_camera.GlobalPosition = _crypt.ToGlobal(MummyCameraPosition);
		_camera.LookAt(_crypt.ToGlobal(MummyCameraTarget), Vector3.Up);
		_explorerCamera = PresentPlayer()?.GetNodeOrNull<Camera3D>("Camera");
	}

	private void AnimateMummy(float t, float m, float dt)
	{
		if (_crypt == null)
			return;
		// Two rattles of the coffin, the second harder, then it bursts.
		float before = m - dt;
		float rattle = Pulse(m, RattleOne, 0.35f) * 1.5f + Pulse(m, RattleTwo, 0.45f) * 2.8f;
		if (RoyalCoffin != null && !_coffinBroken)
			RoyalCoffin.Transform = _coffinRest * new Transform3D(new Basis(Vector3.Back, Mathf.DegToRad(rattle * Mathf.Sin(m * 47f))), Vector3.Zero);
		if ((before < RattleOne && m >= RattleOne) || (before < RattleTwo && m >= RattleTwo))
			Events.Instance?.EmitSignal(Events.SignalName.PlaySfx, "coffin_rattle", Mummy.GlobalPosition);
		if (!_coffinBroken && m >= BurstTime)
			BurstCoffin();

		// Out of the coffin and down off the dais with a small hop; then the run.
		if (m >= BurstTime + 0.2f && m < PovCut)
		{
			float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp((m - BurstTime - 0.2f) / (StepEnd - BurstTime - 0.2f), 0f, 1f));
			Vector3 at = _mummyStart.Lerp(MummyExit, k);
			at.Y += Mathf.Sin(k * Mathf.Pi) * 0.1f;
			Mummy.Position = at;
		}
		if (!_atPov && m >= PovCut)
			CutToExplorer();
		if (_atPov)
			Run(t, m - PovCut, dt);
		else
		{
			float shake = Pulse(m, RattleOne, 0.35f) * 0.008f + Pulse(m, RattleTwo, 0.45f) * 0.015f
				+ (m >= BurstTime ? Mathf.Lerp(0.05f, 0f, Mathf.Clamp((m - BurstTime) / 0.9f, 0f, 1f)) : 0f);
			_camera.HOffset = (_rng.Randf() - 0.5f) * 2f * shake;
			_camera.VOffset = (_rng.Randf() - 0.5f) * 2f * shake;
		}
	}

	// 0 outside [start, start + length], rising and falling smoothly inside it.
	private static float Pulse(float t, float start, float length) =>
		t < start || t > start + length ? 0f : Mathf.Sin((t - start) / length * Mathf.Pi);

	// The front of the coffin is flung off, splinters fly, and what's left is a dark, empty shell.
	private void BurstCoffin()
	{
		_coffinBroken = true;
		Events.Instance?.EmitSignal(Events.SignalName.PlaySfx, "coffin_burst", Mummy.GlobalPosition);
		Mummy.Animate = true;
		Mummy.WalkSpeed = 3f;
		Mummy.StrideLength = 1.5f;
		Mummy.LegSwingDegrees = 38f;
		if (RoyalCoffin?.Mesh == null)
			return;
		RoyalCoffin.Transform = _coffinRest;
		RoyalCoffin.Visible = false;
		// The mummy, the lid and the splinters all pass where the coffin's collider was.
		foreach (Node child in RoyalCoffin.GetChildren())
		{
			if (child is CollisionObject3D body)
				body.CollisionLayer = 0;
		}

		Aabb box = RoyalCoffin.GetAabb();
		float back = box.End.Z;
		float front = box.Position.Z;
		Vector3 centre = box.GetCenter();
		Transform3D rest = RoyalCoffin.Transform;
		// The back half stays: the coffin squashed towards its back, with a dark hollow where its front was.
		MeshInstance3D shell = CopyCoffin("Shell");
		shell.Transform = rest * Scaled(new Vector3(0f, 0f, back), new Vector3(1f, 1f, 0.45f));
		_crypt.AddChild(shell);
		float shellFront = back - (back - front) * 0.45f;
		var hollow = new MeshInstance3D
		{
			Name = "Hollow",
			Mesh = RoyalCoffin.Mesh,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.03f, 0.02f, 0.015f), Roughness = 1f },
			Transform = rest * new Transform3D(Basis.Identity, new Vector3(centre.X, centre.Y, shellFront - 0.004f))
				* new Transform3D(Basis.FromScale(new Vector3(0.8f, 0.88f, 0.02f)), Vector3.Zero)
				* new Transform3D(Basis.Identity, -centre),
		};
		_crypt.AddChild(hollow);

		// The front, flung forward and toppling onto the floor.
		var lid = new RigidBody3D { Name = "BrokenLid", Mass = 40f, CollisionLayer = Layers.World, CollisionMask = Layers.World };
		MeshInstance3D lidMesh = CopyCoffin("Mesh");
		lidMesh.Transform = Scaled(new Vector3(0f, 0f, front), new Vector3(1f, 1f, 0.5f));
		lid.AddChild(lidMesh);
		lid.AddChild(new CollisionShape3D
		{
			Shape = new BoxShape3D { Size = new Vector3(box.Size.X * 0.85f, box.Size.Y * 0.95f, 0.16f) },
			Position = new Vector3(centre.X, centre.Y, front + 0.1f),
		});
		_cryptDebris.AddChild(lid);
		lid.Transform = rest;
		Vector3 forward = -lid.GlobalBasis.Z.Normalized();
		lid.LinearVelocity = forward * 2.6f + Vector3.Up * 1.2f;
		lid.AngularVelocity = lid.GlobalBasis.X.Normalized() * -2.2f + new Vector3(0f, _rng.RandfRange(-0.6f, 0.6f), 0f);

		// Splinters of wood and gold.
		Material wood = RoyalCoffin.GetActiveMaterial(0);
		Material gold = RoyalCoffin.Mesh.GetSurfaceCount() > 1 ? RoyalCoffin.GetActiveMaterial(1) : wood;
		for (int i = 0; i < 12; i++)
		{
			float size = _rng.RandfRange(0.05f, 0.13f);
			var shardSize = new Vector3(size, size * _rng.RandfRange(0.3f, 0.8f), size * _rng.RandfRange(0.15f, 0.4f));
			var shard = new RigidBody3D { Mass = 2f, CollisionLayer = Layers.World, CollisionMask = Layers.World };
			shard.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = shardSize } });
			shard.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = shardSize, Material = i % 3 == 0 ? gold : wood } });
			_cryptDebris.AddChild(shard);
			shard.Transform = rest * new Transform3D(Basis.Identity,
				new Vector3(_rng.RandfRange(box.Position.X * 0.7f, box.End.X * 0.7f), _rng.RandfRange(box.Position.Y + 0.4f, box.End.Y - 0.3f), front));
			shard.LinearVelocity = forward * _rng.RandfRange(2f, 4.5f) + new Vector3(_rng.RandfRange(-1.5f, 1.5f), _rng.RandfRange(0.5f, 2.5f), _rng.RandfRange(-1.5f, 1.5f));
			shard.AngularVelocity = new Vector3(_rng.RandfRange(-8f, 8f), _rng.RandfRange(-8f, 8f), _rng.RandfRange(-8f, 8f));
		}

		var dust = (CpuParticles3D)_burst.Duplicate();
		dust.EmissionShape = CpuParticles3D.EmissionShapeEnum.Box;
		dust.EmissionBoxExtents = new Vector3(box.Size.X * 0.4f, box.Size.Y * 0.4f, 0.05f);
		dust.Direction = forward;
		dust.Spread = 45f;
		_cryptDebris.AddChild(dust);
		dust.GlobalPosition = RoyalCoffin.ToGlobal(new Vector3(centre.X, centre.Y, front));
		dust.Emitting = true;
	}

	private MeshInstance3D CopyCoffin(string name)
	{
		var copy = new MeshInstance3D { Name = name, Mesh = RoyalCoffin.Mesh };
		for (int i = 0; i < RoyalCoffin.Mesh.GetSurfaceCount(); i++)
			copy.SetSurfaceOverrideMaterial(i, RoyalCoffin.GetActiveMaterial(i));
		return copy;
	}

	// Scales by scale about the point pivot.
	private static Transform3D Scaled(Vector3 pivot, Vector3 scale) =>
		new Transform3D(Basis.Identity, pivot) * new Transform3D(Basis.FromScale(scale), Vector3.Zero) * new Transform3D(Basis.Identity, -pivot);

	// Through the explorer's eyes: the mummy runs at them. Its route goes round the altar if that's in the way, and
	// stops a little short of them.
	private void CutToExplorer()
	{
		_atPov = true;
		_camera.HOffset = 0f;
		_camera.VOffset = 0f;
		Vector3 target = _explorerCamera != null ? _crypt.ToLocal(_explorerCamera.GlobalPosition) : MummyCameraPosition;
		if (_explorerCamera != null)
			_camera.GlobalTransform = _explorerCamera.GlobalTransform;
		target.Y = MummyExit.Y;

		// The run stays in the crypt: an explorer outside it is run at only as far as the doorway.
		if (target.Z < CryptDoorZ)
			target = new Vector3(0f, target.Y, CryptDoorZ - RunStopShort);

		_mummyPath.Clear();
		_mummyPath.Add(Mummy.Position with { Y = MummyExit.Y });
		// The altar (in the crypt's middle) stands between the coffin and anyone in front of it: go round its side.
		if (target.Z < MummyExit.Z - 2f && Mathf.Abs(target.X) < 1.6f)
			_mummyPath.Add(new Vector3(target.X >= 0f ? 0.98f : -0.98f, MummyExit.Y, 16.7f));
		Vector3 last = _mummyPath[^1];
		Vector3 toward = target - last;
		float reach = Mathf.Max(0.2f, toward.Length() - RunStopShort);
		_mummyPath.Add(last + toward.Normalized() * reach);
		_mummyPathLength = 0f;
		for (int i = 1; i < _mummyPath.Count; i++)
			_mummyPathLength += _mummyPath[i - 1].DistanceTo(_mummyPath[i]);
	}

	private void Run(float t, float r, float dt)
	{
		// Picks up speed over the first moment, then a steady run.
		const float pickUp = 0.25f;
		float distance = RunSpeed * (r - pickUp * (1f - Mathf.Exp(-r / pickUp)));
		Vector3 at = _mummyPath[^1];
		Vector3 heading = _mummyPath[^1] - _mummyPath[^2];
		float left = distance;
		for (int i = 1; i < _mummyPath.Count; i++)
		{
			float segment = _mummyPath[i - 1].DistanceTo(_mummyPath[i]);
			if (left <= segment)
			{
				at = _mummyPath[i - 1].Lerp(_mummyPath[i], left / Mathf.Max(segment, 0.001f));
				heading = _mummyPath[i] - _mummyPath[i - 1];
				break;
			}
			left -= segment;
		}
		if (_scaredStart >= 0f)
		{
			Scared(t, dt);
			return;
		}
		Mummy.Position = at;
		heading.Y = 0f;
		if (heading.LengthSquared() > 0.0001f)
		{
			float yaw = Mathf.Atan2(-heading.X, -heading.Z);
			Mummy.Rotation = new Vector3(0f, Mathf.LerpAngle(Mummy.Rotation.Y, yaw, 1f - Mathf.Exp(-12f * dt)), 0f);
		}
		if (distance >= _mummyPathLength)
			StartScared(t);

		// The explorer's eyes follow it in, with a jolt as it closes.
		Vector3 head = Mummy.GlobalPosition + Vector3.Up * 1.45f;
		Basis look = Basis.LookingAt(head - _camera.GlobalPosition, Vector3.Up);
		_camera.GlobalBasis = _camera.GlobalBasis.Orthonormalized().Slerp(look.Orthonormalized(), 1f - Mathf.Exp(-7f * dt));
		float near = 1f - Mathf.Clamp((Mummy.GlobalPosition.DistanceTo(_camera.GlobalPosition) - RunStopShort) / 3f, 0f, 1f);
		float shake = 0.006f + 0.02f * near * near;
		_camera.HOffset = (_rng.Randf() - 0.5f) * 2f * shake;
		_camera.VOffset = (_rng.Randf() - 0.5f) * 2f * shake;
	}

	// The mummy is on them: the explorer's fright begins. The passage wall that sank to let them into the crypt is
	// found, ready to come back up.
	private void StartScared(float t)
	{
		_scaredStart = t;
		_fleeFrom = _camera.GlobalPosition;
		_fleeTo = _crypt.ToGlobal(FleeStop with { X = _crypt.ToLocal(_fleeFrom).X * 0.3f }) + Vector3.Up * 1.55f;
		Vector3 away = _fleeTo - _fleeFrom;
		away.Y = 0f;
		_fleeLook = Basis.LookingAt(away.Normalized(), Vector3.Up);
		var opener = GetTree().CurrentScene?.FindChild("PassageOpener", true, false) as MoveOnSolved;
		_wall = opener?.Target;
		if (_wall != null)
		{
			_wallDown = _wall.Position;
			_wallUp = _wallDown - opener.Offset;
		}
	}

	private void Scared(float t, float dt)
	{
		float s = t - _scaredStart;
		Vector3 eye;
		Basis look;
		float shake;
		Basis atMummy = Basis.LookingAt(Mummy.GlobalPosition + Vector3.Up * 1.45f - _fleeFrom, Vector3.Up);
		if (s < RecoilEnd)
		{
			// A start: the head jerks back and up, and the explorer stumbles a step back.
			float k = Mathf.Sin(Mathf.Clamp(s / RecoilEnd, 0f, 1f) * Mathf.Pi * 0.5f);
			Vector3 back = (_fleeFrom - Mummy.GlobalPosition) with { Y = 0f };
			eye = _fleeFrom + back.Normalized() * 0.25f * k;
			look = atMummy * new Basis(Vector3.Right, Mathf.DegToRad(8f) * k);
			shake = 0.03f * (1f - k * 0.5f);
		}
		else if (s < TurnEnd)
		{
			// Whirls round towards the way out.
			float k = Mathf.SmoothStep(0f, 1f, (s - RecoilEnd) / (TurnEnd - RecoilEnd));
			Vector3 back = (_fleeFrom - Mummy.GlobalPosition) with { Y = 0f };
			eye = _fleeFrom + back.Normalized() * 0.25f;
			look = new Basis(atMummy.GetRotationQuaternion().Normalized().Slerp(_fleeLook.GetRotationQuaternion().Normalized(), k));
			shake = 0.015f;
		}
		else
		{
			// Runs for it, slowing to a stop as the wall slams up ahead.
			float k = Mathf.Clamp((s - TurnEnd) / (FleeEnd - TurnEnd), 0f, 1f);
			float eased = 1f - Mathf.Pow(1f - k, 2.2f);
			Vector3 start = _fleeFrom + ((_fleeFrom - Mummy.GlobalPosition) with { Y = 0f }).Normalized() * 0.25f;
			eye = start.Lerp(_fleeTo, eased);
			// Running bob, dying away as they stop.
			eye += Vector3.Up * Mathf.Sin(s * 17f) * 0.045f * (1f - k);
			look = _fleeLook * new Basis(Vector3.Forward, Mathf.Sin(s * 8.5f) * 0.035f * (1f - k));
			shake = 0.006f;
		}

		// The wall comes back up, slowly then fast, and slams into place.
		if (_wall != null && s >= WallRise)
		{
			float w = Mathf.Clamp((s - WallRise) / (WallSlam - WallRise), 0f, 1f);
			_wall.Position = _wallDown.Lerp(_wallUp, w * w);
			if (!_wallSlammed && w >= 1f)
			{
				_wallSlammed = true;
				Events.Instance?.EmitSignal(Events.SignalName.PlaySfx, "door_slam", _wall.GlobalPosition);
				var dust = (CpuParticles3D)_burst.Duplicate();
				dust.EmissionRingRadius = 0.9f;
				dust.EmissionRingInnerRadius = 0.2f;
				_crypt.AddChild(dust);
				dust.GlobalPosition = _crypt.ToGlobal(FleeStop with { Z = FleeStop.Z - 1.3f });
				dust.Emitting = true;
			}
		}
		if (_wallSlammed)
			shake = Mathf.Max(shake, Mathf.Lerp(0.06f, 0f, Mathf.Clamp((s - WallSlam) / 0.7f, 0f, 1f)));

		_camera.GlobalPosition = eye;
		_camera.GlobalBasis = look.Orthonormalized();
		_camera.HOffset = (_rng.Randf() - 0.5f) * 2f * shake;
		_camera.VOffset = (_rng.Randf() - 0.5f) * 2f * shake;

		// The mummy comes after them, stopping a little behind.
		Vector3 chase = _crypt.ToLocal(eye) with { Y = MummyExit.Y };
		Vector3 from = Mummy.Position;
		Vector3 toward = chase - from;
		toward.Y = 0f;
		if (toward.Length() > 1.6f && s > RecoilEnd)
		{
			Mummy.Position = from + toward.Normalized() * Mathf.Min(2.6f * dt, toward.Length() - 1.6f);
			float yaw = Mathf.Atan2(-toward.X, -toward.Z);
			Mummy.Rotation = new Vector3(0f, Mathf.LerpAngle(Mummy.Rotation.Y, yaw, 1f - Mathf.Exp(-10f * dt)), 0f);
		}

		if (_mummyEnd < 0f && s >= ScaredEnd)
			_mummyEnd = t;
	}

	// The trap room: the only way on now. Its line comes up over the shot.
	private void CutToTrapRoom(float t)
	{
		_trapStart = t;
		_camera.HOffset = 0f;
		_camera.VOffset = 0f;
		_trapCamera = new Camera3D { Name = "TrapCamera", Fov = 60f };
		AddChild(_trapCamera);
		_trapCamera.GlobalPosition = _crypt.ToGlobal(TrapCameraPosition);
		_trapCamera.LookAt(_crypt.ToGlobal(TrapCameraTarget), Vector3.Up);
		_trapCamera.MakeCurrent();
		_title.Text = TrapTitle;
		_title.Modulate = Colors.Transparent;
	}

	private void CutBack()
	{
		_cutBack = true;
		SetPastPlayersShown(true);
		SetPresentPlayersShown(true);
		HandMummyToPastPlayer();
		PlaceExplorerAtWall();
		_camera.HOffset = 0f;
		_camera.VOffset = 0f;
		Camera3D playerCamera = _localPlayer?.GetNodeOrNull<Camera3D>("Camera");
		playerCamera?.MakeCurrent();
		if (_hud != null && IsInstanceValid(_hud))
			_hud.Visible = true;
		if (_trickle.IsInsideTree())
			_trickle.Emitting = false;
	}

	private void Finish()
	{
		_finished = true;
		GameState.Instance?.ResumeTimer();
		_fade.Color = Colors.Transparent;
		if (IsInstanceValid(_localPlayer))
			_localPlayer.InputLocked = false;
		// The stones and splinters stay where they fell, but stop being simulated.
		foreach (Node node in _debris.FindChildren("*", "RigidBody3D", true, false))
			((RigidBody3D)node).Freeze = true;
		if (_cryptDebris != null)
		{
			foreach (Node node in _cryptDebris.FindChildren("*", "RigidBody3D", true, false))
				((RigidBody3D)node).Freeze = true;
		}
		if (_solvedHere)
		{
			IsSolved = true;
			EmitSignal(SignalName.Solved);
		}
	}
}
