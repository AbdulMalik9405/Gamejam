using Gamejam.Core;
using Godot;

namespace Gamejam.Gameplay;

/// <summary>
/// One of the rigged characters (art/explorer.glb, art/egyptian.glb, art/mummy.glb), posed in code since they come
/// without animations: arms lowered from the modelled A-pose, a simple walk (legs and arms swing) while it moves, and
/// a head that can look up. Standing still it idles: it breathes, shifts its weight and looks about a little. A
/// cutscene can turn it into a ragdoll (StartRagdoll). The models face +Z; this node turns them to face -Z like every
/// other Node3D.
/// </summary>
public partial class CharacterModel : Node3D
{
	[Export] public PackedScene Scene { get; set; }
	// How much further down the arms hang than in the model, and how far forward they're folded at the elbow.
	[Export] public float ArmDownDegrees { get; set; } = 30f;
	[Export] public float ElbowBendDegrees { get; set; } = 10f;
	// Walk swing at full speed, and the speed (m/s) that counts as full speed.
	[Export] public float LegSwingDegrees { get; set; } = 28f;
	[Export] public float ArmSwingDegrees { get; set; } = 18f;
	[Export] public float WalkSpeed { get; set; } = 4.5f;
	[Export] public float StrideLength { get; set; } = 1.4f;
	// Off for a character that never walks or idles (the mummy in its coffin).
	[Export] public bool Animate { get; set; } = true;
	// Idle at full strength when standing still: breathing (chest rise), weight shift (hip sway) and head drift.
	[Export] public float BreathDegrees { get; set; } = 1.6f;
	[Export] public float SwayDegrees { get; set; } = 1.8f;
	[Export] public float HeadDriftDegrees { get; set; } = 5f;

	// Extra tilt of the head, positive = looking up. Used by cutscenes.
	public float HeadPitchDegrees { get; set; }
	// Raises both arms in front of the face (0 = at the sides, 1 = shielding the head). Used by cutscenes.
	public float Shield { get; set; }
	// Holds both arms straight out in front (0 = at the sides, 1 = level), like a mummy. Used by cutscenes.
	public float Reach { get; set; }
	// Something carried in the right hand (the other player's view of what this one has out): the forearm comes up to
	// hold it, and it's kept upright in the hand, its "Grip" child (if any) at the palm.
	public Node3D HeldItem { get; set; }
	// The ending screens (gameplay/GameEnding.cs), 0 = off, 1 = full. Dance: arms take turns punching up while the
	// other rests on the hip, the hips swing and the knees bounce. Shame: head hung, hands on the head, slumped.
	public float Dance { get; set; }
	public float Shame { get; set; }

	private Skeleton3D _skeleton;
	private Node3D _model;
	private int _hips = -1, _spine = -1, _chest = -1, _head = -1;
	private int _leftUpperArm = -1, _leftLowerArm = -1, _rightUpperArm = -1, _rightLowerArm = -1;
	private int _leftUpperLeg = -1, _leftLowerLeg = -1, _rightUpperLeg = -1, _rightLowerLeg = -1;
	private int _rightHand = -1;
	private Vector3 _lastPosition;
	private bool _hasLast;
	private float _phase;
	private float _amount;
	private float _idleTime;
	private PhysicalBoneSimulator3D _ragdoll;

	public bool IsRagdoll => _ragdoll != null;

	public override void _Ready()
	{
		if (Scene == null)
		{
			GD.PushWarning($"{Name}: no character Scene set.");
			return;
		}
		_model = Scene.Instantiate<Node3D>();
		_model.Name = "Model";
		// The models face +Z.
		_model.RotationDegrees = new Vector3(0f, 180f, 0f);
		AddChild(_model);
		// The artist's preview lights (Preview_Fill, ...) only light the model in Blender.
		foreach (Node preview in _model.FindChildren("Preview*", "", false, false))
			preview.QueueFree();

		_skeleton = _model.FindChild("Skeleton3D", true, false) as Skeleton3D;
		if (_skeleton == null)
			return;
		_hips = _skeleton.FindBone("Hips");
		_spine = _skeleton.FindBone("Spine");
		_chest = _skeleton.FindBone("Chest");
		_head = _skeleton.FindBone("Head");
		_leftUpperArm = _skeleton.FindBone("LeftUpperArm");
		_leftLowerArm = _skeleton.FindBone("LeftLowerArm");
		_rightUpperArm = _skeleton.FindBone("RightUpperArm");
		_rightLowerArm = _skeleton.FindBone("RightLowerArm");
		_leftUpperLeg = _skeleton.FindBone("LeftUpperLeg");
		_leftLowerLeg = _skeleton.FindBone("LeftLowerLeg");
		_rightUpperLeg = _skeleton.FindBone("RightUpperLeg");
		_rightLowerLeg = _skeleton.FindBone("RightLowerLeg");
		_rightHand = _skeleton.FindBone("RightHand");
		Pose(0f);
	}

	public void SetCastShadow(GeometryInstance3D.ShadowCastingSetting setting)
	{
		if (_model == null)
			return;
		foreach (Node node in _model.FindChildren("*", "GeometryInstance3D", true, false))
			((GeometryInstance3D)node).CastShadow = setting;
	}

	public override void _Process(double delta)
	{
		// A ragdoll's bones are moved by physics.
		if (_skeleton == null || _ragdoll != null)
			return;
		float dt = (float)delta;
		if (Animate)
			_idleTime += dt;
		float target = 0f;
		if (Animate && dt > 0f)
		{
			// Speed from how far we moved, so it also works for the other player's copy (only its position is synced).
			Vector3 position = GlobalPosition;
			if (_hasLast)
			{
				Vector3 moved = position - _lastPosition;
				moved.Y = 0f;
				float speed = moved.Length() / dt;
				// A teleport (spawn, cutscene) isn't walking.
				if (speed < 20f)
				{
					target = Mathf.Clamp(speed / WalkSpeed, 0f, 1.3f);
					_phase = Mathf.Wrap(_phase + speed * dt * Mathf.Pi / StrideLength, 0f, Mathf.Tau);
				}
			}
			_lastPosition = position;
			_hasLast = true;
		}
		_amount = Mathf.Lerp(_amount, target, 1f - Mathf.Exp(-10f * dt));
		Pose(_amount);
		PlaceHeldItem();
	}

	private void PlaceHeldItem()
	{
		if (HeldItem == null || !IsInstanceValid(HeldItem) || _rightHand < 0)
			return;
		Transform3D hand = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(_rightHand);
		// The palm: a little along the hand from the wrist. Upright, facing where the character faces, leaning forward a
		// touch, whatever angle the hand is at (without the model's scale, so the item keeps its size).
		Vector3 palm = hand * new Vector3(0f, 0.07f, 0.02f);
		Basis upright = GlobalBasis.Orthonormalized() * new Basis(Vector3.Right, Mathf.DegToRad(-12f));
		Vector3 grip = HeldItem.GetNodeOrNull<Node3D>("Grip")?.Position ?? Vector3.Zero;
		HeldItem.GlobalTransform = new Transform3D(upright, palm - upright * grip);
	}

	private void Pose(float walk)
	{
		_skeleton.ResetBonePoses();
		// walk runs 0 (standing) to 1 (walking pace), and on to 1.3 when sprinting.
		float stride = Mathf.Clamp(walk, 0f, 1f);
		float sprint = Mathf.Clamp((walk - 1f) / 0.3f, 0f, 1f);
		float swing = Mathf.Sin(_phase) * stride;

		// Idle, fading out as it starts walking: slow breaths lift the chest, the weight drifts from hip to hip (the
		// spine leans back the other way so the head stays level), and the head wanders a little.
		float idle = Animate ? 1f - Mathf.Clamp(walk, 0f, 1f) : 0f;
		float breath = Mathf.Sin(_idleTime * 1.7f);
		float sway = Mathf.Sin(_idleTime * 0.55f) + 0.3f * Mathf.Sin(_idleTime * 1.3f + 1f);
		RotateBone(_hips, Vector3.Back, Mathf.DegToRad(SwayDegrees) * sway * idle);
		RotateBone(_spine, Vector3.Back, -Mathf.DegToRad(SwayDegrees) * 0.8f * sway * idle);
		RotateBone(_chest, Vector3.Right, -Mathf.DegToRad(BreathDegrees) * breath * idle);

		// Walking: the body dips as each foot passes under it, the hips turn with the stride and the chest turns back
		// against them. Sprinting: a forward lean (the head stays level), longer strides, higher knees, pumping arms.
		// Skeleton space: +X is the character's left, +Y up, +Z forward. Positive about X swings a down-pointing limb
		// backward and tips an up-pointing one (spine, head) forward.
		if (_model != null)
			_model.Position = new Vector3(0f, -Mathf.Abs(Mathf.Cos(_phase)) * 0.025f * stride * (1f + sprint), 0f);
		float twist = Mathf.Sin(_phase) * stride;
		RotateBone(_hips, Vector3.Up, Mathf.DegToRad(5f) * twist);
		RotateBone(_spine, Vector3.Up, -Mathf.DegToRad(8f) * twist);
		RotateBone(_spine, Vector3.Right, Mathf.DegToRad(11f) * sprint);
		RotateBone(_head, Vector3.Right, -Mathf.DegToRad(7f) * sprint);

		float legSwing = Mathf.DegToRad(LegSwingDegrees) * (1f + 0.45f * sprint);
		RotateBone(_leftUpperLeg, Vector3.Right, legSwing * swing);
		RotateBone(_rightUpperLeg, Vector3.Right, -legSwing * swing);
		// The back leg's knee bends as it lifts (much more when sprinting); the front one stays soft.
		float knee = Mathf.DegToRad(30f + 40f * sprint);
		float softKnee = Mathf.DegToRad(6f) * stride;
		RotateBone(_leftLowerLeg, Vector3.Right, -knee * Mathf.Max(0f, -swing) - softKnee);
		RotateBone(_rightLowerLeg, Vector3.Right, -knee * Mathf.Max(0f, swing) - softKnee);

		float down = Mathf.DegToRad(ArmDownDegrees);
		float reach = Mathf.SmoothStep(0f, 1f, Reach);
		// Reaching arms stay level instead of swinging.
		float armSwing = Mathf.DegToRad(ArmSwingDegrees) * (1f + 0.6f * sprint) * swing * (1f - reach);
		float shield = Mathf.SmoothStep(0f, 1f, Shield);
		bool holding = HeldItem != null && IsInstanceValid(HeldItem);
		// Arms hang at the sides and swing opposite to the legs; an arm carrying something swings much less.
		RotateBone(_leftUpperArm, Vector3.Back, -down);
		RotateBone(_leftUpperArm, Vector3.Right, -armSwing);
		RotateBone(_rightUpperArm, Vector3.Back, down);
		RotateBone(_rightUpperArm, Vector3.Right, armSwing * (holding ? 0.3f : 1f));
		float elbow = Mathf.DegToRad(ElbowBendDegrees + 55f * sprint);
		RotateBone(_leftLowerArm, Vector3.Right, -elbow);
		RotateBone(_rightLowerArm, Vector3.Right, -elbow);
		// Carrying: the upper arm a little forward and the forearm up, holding the item out in front.
		if (holding && reach <= 0f && shield <= 0f)
		{
			RotateBone(_rightUpperArm, Vector3.Right, -Mathf.DegToRad(20f));
			RotateBone(_rightLowerArm, Vector3.Right, -Mathf.DegToRad(65f));
		}
		if (reach > 0f)
		{
			RotateBone(_leftUpperArm, Vector3.Right, -Mathf.DegToRad(85f) * reach);
			RotateBone(_rightUpperArm, Vector3.Right, -Mathf.DegToRad(85f) * reach);
		}
		// Shielding the head: from hanging at the sides the arms swing up in front (about the shoulder's side axis) and
		// a little in, and the forearms fold in across above the face.
		if (shield > 0f)
		{
			RotateBone(_leftUpperArm, Vector3.Right, -Mathf.DegToRad(135f) * shield);
			RotateBone(_leftUpperArm, Vector3.Up, -Mathf.DegToRad(20f) * shield);
			RotateBone(_rightUpperArm, Vector3.Right, -Mathf.DegToRad(135f) * shield);
			RotateBone(_rightUpperArm, Vector3.Up, Mathf.DegToRad(20f) * shield);
			RotateBone(_leftLowerArm, Vector3.Forward, -Mathf.DegToRad(80f) * shield);
			RotateBone(_rightLowerArm, Vector3.Forward, Mathf.DegToRad(80f) * shield);
		}

		if (Dance > 0f)
			PoseDance(Mathf.Clamp(Dance, 0f, 1f));
		if (Shame > 0f)
			PoseShame(Mathf.Clamp(Shame, 0f, 1f));

		RotateBone(_head, Vector3.Right, -Mathf.DegToRad(HeadPitchDegrees));
		float drift = Mathf.DegToRad(HeadDriftDegrees) * idle;
		RotateBone(_head, Vector3.Up, drift * (Mathf.Sin(_idleTime * 0.31f) + 0.4f * Mathf.Sin(_idleTime * 0.83f + 2f)));
		RotateBone(_head, Vector3.Right, drift * 0.4f * Mathf.Sin(_idleTime * 0.47f + 0.5f));
	}

	// One arm up with the elbow bent, the other hand on the hip, swapping every half beat; the hips swing out on each
	// beat (the legs lean back the other way so the feet stay put) and the knees bounce.
	private void PoseDance(float amount)
	{
		const float beat = 0.6f;
		float t = _idleTime;
		float swap = Mathf.Sin(t * Mathf.Pi / beat);
		float leftUp = Mathf.SmoothStep(-0.35f, 0.65f, swap) * amount;
		float rightUp = Mathf.SmoothStep(-0.35f, 0.65f, -swap) * amount;
		float hip = Mathf.Sin(t * Mathf.Tau / beat) * amount;
		float bounce = Mathf.Abs(Mathf.Sin(t * Mathf.Pi / beat * 2f)) * amount;

		RotateBone(_hips, Vector3.Back, Mathf.DegToRad(9f) * hip);
		RotateBone(_leftUpperLeg, Vector3.Back, -Mathf.DegToRad(9f) * hip);
		RotateBone(_rightUpperLeg, Vector3.Back, -Mathf.DegToRad(9f) * hip);
		RotateBone(_spine, Vector3.Back, -Mathf.DegToRad(7f) * hip);
		RotateBone(_chest, Vector3.Back, -Mathf.DegToRad(4f) * hip);
		RotateBone(_head, Vector3.Back, Mathf.DegToRad(6f) * hip);
		// Knees give a little on each beat.
		RotateBone(_leftUpperLeg, Vector3.Right, -Mathf.DegToRad(10f) * bounce);
		RotateBone(_rightUpperLeg, Vector3.Right, -Mathf.DegToRad(10f) * bounce);
		RotateBone(_leftLowerLeg, Vector3.Right, Mathf.DegToRad(20f) * bounce);
		RotateBone(_rightLowerLeg, Vector3.Right, Mathf.DegToRad(20f) * bounce);
		if (_model != null)
			_model.Position = new Vector3(Mathf.DegToRad(9f) * hip * 0.25f, -0.035f * bounce, 0f);

		DanceArm(_leftUpperArm, _leftLowerArm, 1f, leftUp, amount);
		DanceArm(_rightUpperArm, _rightLowerArm, -1f, rightUp, amount);
	}

	// side: +1 left arm, -1 right arm. up: 0 = hand on the hip, 1 = raised high, elbow bent.
	private void DanceArm(int upper, int lower, float side, float up, float amount)
	{
		float hipHand = amount - up;
		// Hand on the hip: elbow out to the side and back, forearm folded in to the waist.
		RotateBone(upper, Vector3.Back, side * Mathf.DegToRad(28f) * hipHand);
		RotateBone(upper, Vector3.Right, Mathf.DegToRad(18f) * hipHand);
		RotateBone(lower, Vector3.Back, -side * Mathf.DegToRad(95f) * hipHand);
		// Raised: up past the shoulder, a little in front, the forearm bent back in over the head.
		RotateBone(upper, Vector3.Back, side * Mathf.DegToRad(150f) * up);
		RotateBone(upper, Vector3.Right, -Mathf.DegToRad(15f) * up);
		RotateBone(lower, Vector3.Back, side * Mathf.DegToRad(45f) * up);
	}

	// Head hung forward, back slumped, both hands clasped on top of the head; now and then a slow shake of the head.
	private void PoseShame(float amount)
	{
		float t = _idleTime;
		RotateBone(_spine, Vector3.Right, Mathf.DegToRad(10f) * amount);
		RotateBone(_chest, Vector3.Right, Mathf.DegToRad(10f) * amount);
		RotateBone(_head, Vector3.Right, Mathf.DegToRad(32f) * amount);
		RotateBone(_head, Vector3.Up, Mathf.DegToRad(9f) * Mathf.Sin(t * 1.4f) * Mathf.Max(0f, Mathf.Sin(t * 0.45f)) * amount);
		ShameArm(_leftUpperArm, _leftLowerArm, 1f, amount);
		ShameArm(_rightUpperArm, _rightLowerArm, -1f, amount);
	}

	private void ShameArm(int upper, int lower, float side, float amount)
	{
		// Elbows up and out to the sides, a little forward, forearms folded in so the hands meet on the crown.
		RotateBone(upper, Vector3.Back, side * Mathf.DegToRad(118f) * amount);
		RotateBone(upper, Vector3.Up, -side * Mathf.DegToRad(25f) * amount);
		RotateBone(lower, Vector3.Back, side * Mathf.DegToRad(125f) * amount);
	}

	// Goes limp: physics takes over the body from its current pose, and the push (an impulse, in N·s) knocks it down.
	// The ragdoll is a capsule along each main bone, jointed to its parent, on the world layer so it lands on the
	// floor and debris lands on it.
	public void StartRagdoll(Vector3 push)
	{
		if (_skeleton == null || _ragdoll != null)
			return;
		_ragdoll = new PhysicalBoneSimulator3D { Name = "Ragdoll" };
		_skeleton.AddChild(_ragdoll);

		// Each bone, the bone its capsule reaches to (none: a short one straight on), its radius and how far it can swing.
		var bones = new (string Bone, string Tip, float Radius, float Swing)[]
		{
			("Hips", "Spine", 0.13f, 0f), ("Spine", "Chest", 0.13f, 25f), ("Chest", "Neck", 0.14f, 25f), ("Head", "", 0.1f, 40f),
			("LeftUpperArm", "LeftLowerArm", 0.05f, 80f), ("LeftLowerArm", "LeftHand", 0.045f, 70f),
			("RightUpperArm", "RightLowerArm", 0.05f, 80f), ("RightLowerArm", "RightHand", 0.045f, 70f),
			("LeftUpperLeg", "LeftLowerLeg", 0.07f, 50f), ("LeftLowerLeg", "LeftFoot", 0.055f, 60f),
			("RightUpperLeg", "RightLowerLeg", 0.07f, 50f), ("RightLowerLeg", "RightFoot", 0.055f, 60f),
		};
		PhysicalBone3D chest = null;
		foreach ((string name, string tip, float radius, float swing) in bones)
		{
			int bone = _skeleton.FindBone(name);
			if (bone < 0)
				continue;
			int tipBone = tip == "" ? -1 : _skeleton.FindBone(tip);
			Vector3 reach = tipBone >= 0 ? _skeleton.GetBoneRest(tipBone).Origin : new Vector3(0f, 0.22f, 0f);
			PhysicalBone3D body = MakePhysicalBone(name, reach, radius, swing);
			_ragdoll.AddChild(body);
			if (name == "Chest")
				chest = body;
		}
		_ragdoll.PhysicalBonesStartSimulation();
		chest?.ApplyCentralImpulse(push);
	}

	// Like Godot's "Create Physical Skeleton": the capsule runs from the bone's head to reach (in the bone's space), and
	// the joint sits at the bone's head.
	private static PhysicalBone3D MakePhysicalBone(string bone, Vector3 reach, float radius, float swingDegrees)
	{
		float half = reach.Length() * 0.5f;
		Vector3 up = Vector3.Up.Cross(reach).IsZeroApprox() ? Vector3.Back : Vector3.Up;
		Basis toward = Basis.LookingAt(reach, up);
		var body = new PhysicalBone3D
		{
			Name = "Physical " + bone,
			CollisionLayer = Layers.World,
			CollisionMask = Layers.World,
			// Heavy and damped, so falling stones pin him rather than knock him about.
			Mass = 8f,
			LinearDamp = 0.8f,
			AngularDamp = 4f,
			BodyOffset = new Transform3D(toward, toward * new Vector3(0f, 0f, -half)),
			JointOffset = new Transform3D(Basis.Identity, new Vector3(0f, 0f, half)),
			JointType = swingDegrees > 0f ? PhysicalBone3D.JointTypeEnum.Cone : PhysicalBone3D.JointTypeEnum.None,
		};
		body.Set("bone_name", bone);
		if (swingDegrees > 0f)
		{
			// These two are set in degrees.
			body.Set("joint_constraints/swing_span", swingDegrees);
			body.Set("joint_constraints/twist_span", 20f);
		}
		body.AddChild(new CollisionShape3D
		{
			Shape = new CapsuleShape3D { Radius = radius, Height = Mathf.Max(half * 2f, radius * 2f + 0.01f) },
			// The capsule's length is along Y; the body looks along -Z.
			Basis = new Basis(new Vector3(1f, 0f, 0f), new Vector3(0f, 0f, -1f), new Vector3(0f, 1f, 0f)),
		});
		return body;
	}

	// Turns a bone (and so everything below it) about its own head, by an angle about a skeleton-space axis.
	private void RotateBone(int bone, Vector3 axis, float angle)
	{
		if (bone < 0 || Mathf.IsZeroApprox(angle))
			return;
		Transform3D global = _skeleton.GetBoneGlobalPose(bone);
		var turn = new Basis(axis, angle);
		_skeleton.SetBoneGlobalPose(bone, new Transform3D(turn * global.Basis, global.Origin));
	}
}
