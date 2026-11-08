using Godot;

namespace Gamejam.Gameplay;

/// <summary>
/// One of the rigged characters (art/explorer.glb, art/egyptian.glb, art/mummy.glb), posed in code since they come
/// without animations: arms lowered from the modelled A-pose, a simple walk (legs and arms swing) while it moves, and
/// a head that can look up. The models face +Z; this node turns them to face -Z like every other Node3D.
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
	// Off for a character that never walks (the mummy in its coffin).
	[Export] public bool Animate { get; set; } = true;

	// Extra tilt of the head, positive = looking up. Used by cutscenes.
	public float HeadPitchDegrees { get; set; }
	// Raises both arms in front of the face (0 = at the sides, 1 = shielding the head). Used by cutscenes.
	public float Shield { get; set; }

	private Skeleton3D _skeleton;
	private Node3D _model;
	private int _hips = -1, _spine = -1, _head = -1;
	private int _leftUpperArm = -1, _leftLowerArm = -1, _rightUpperArm = -1, _rightLowerArm = -1;
	private int _leftUpperLeg = -1, _leftLowerLeg = -1, _rightUpperLeg = -1, _rightLowerLeg = -1;
	private Vector3 _lastPosition;
	private bool _hasLast;
	private float _phase;
	private float _amount;

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
		_head = _skeleton.FindBone("Head");
		_leftUpperArm = _skeleton.FindBone("LeftUpperArm");
		_leftLowerArm = _skeleton.FindBone("LeftLowerArm");
		_rightUpperArm = _skeleton.FindBone("RightUpperArm");
		_rightLowerArm = _skeleton.FindBone("RightLowerArm");
		_leftUpperLeg = _skeleton.FindBone("LeftUpperLeg");
		_leftLowerLeg = _skeleton.FindBone("LeftLowerLeg");
		_rightUpperLeg = _skeleton.FindBone("RightUpperLeg");
		_rightLowerLeg = _skeleton.FindBone("RightLowerLeg");
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
		if (_skeleton == null)
			return;
		float dt = (float)delta;
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
	}

	private void Pose(float walk)
	{
		_skeleton.ResetBonePoses();
		float swing = Mathf.Sin(_phase) * walk;

		// Skeleton space: +X is the character's left, +Y up, +Z forward. Positive about X swings a limb backward.
		RotateBone(_leftUpperLeg, Vector3.Right, Mathf.DegToRad(LegSwingDegrees) * swing);
		RotateBone(_rightUpperLeg, Vector3.Right, -Mathf.DegToRad(LegSwingDegrees) * swing);
		// The back leg's knee bends as it lifts.
		RotateBone(_leftLowerLeg, Vector3.Right, -Mathf.DegToRad(30f) * Mathf.Max(0f, -swing));
		RotateBone(_rightLowerLeg, Vector3.Right, -Mathf.DegToRad(30f) * Mathf.Max(0f, swing));

		float down = Mathf.DegToRad(ArmDownDegrees);
		float armSwing = Mathf.DegToRad(ArmSwingDegrees) * swing;
		float shield = Mathf.SmoothStep(0f, 1f, Shield);
		// Arms swing opposite to the legs. Raised for shielding: forward and up, elbows bent in front of the face.
		RotateBone(_leftUpperArm, Vector3.Back, -down * (1f - shield));
		RotateBone(_leftUpperArm, Vector3.Right, -armSwing - Mathf.DegToRad(150f) * shield);
		RotateBone(_rightUpperArm, Vector3.Back, down * (1f - shield));
		RotateBone(_rightUpperArm, Vector3.Right, armSwing - Mathf.DegToRad(150f) * shield);
		float elbow = Mathf.DegToRad(ElbowBendDegrees) + Mathf.DegToRad(70f) * shield;
		RotateBone(_leftLowerArm, Vector3.Right, -elbow);
		RotateBone(_rightLowerArm, Vector3.Right, -elbow);
		RotateBone(_leftLowerArm, Vector3.Up, -Mathf.DegToRad(60f) * shield);
		RotateBone(_rightLowerArm, Vector3.Up, Mathf.DegToRad(60f) * shield);

		RotateBone(_head, Vector3.Right, -Mathf.DegToRad(HeadPitchDegrees));
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
