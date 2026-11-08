using Gamejam.Core;
using Godot;
using Godot.Collections;

namespace Gamejam.Gameplay;

// Solved once every keyhole holds a key, in any order. The keys then turn in their locks and can't be taken out.
// Pickups are local to the player using them, so each machine checks its own copy of the objects.
public partial class KeyholeLock : Puzzle
{
	[Export] public Array<PickupSlot> Keyholes { get; set; } = new();
	// The ItemName that fits. Keyholes without their own Accepts get this.
	[Export] public string ItemName { get; set; } = "Key";
	// Each key turns about its shaft (its local X axis, ShaftHeight above its origin) once all are in.
	[Export] public float TurnDegrees { get; set; } = 90f;
	[Export] public float TurnTime { get; set; } = 0.6f;
	[Export] public float ShaftHeight { get; set; } = 0.0276f;
	// {0} = keys in place, {1} = keyholes. Empty = no message.
	[Export] public string ProgressMessage { get; set; } = "{0} of {1} keys in place";
	[Export] public string SolvedMessage { get; set; } = "The keys turn... somewhere behind you, stone grinds against stone.";

	public override void _Ready()
	{
		foreach (PickupSlot keyhole in Keyholes)
		{
			if (keyhole == null)
				continue;
			if (keyhole.Accepts == "")
				keyhole.Accepts = ItemName;
			keyhole.OccupantChanged += OnOccupantChanged;
		}
	}

	public override void _ExitTree()
	{
		foreach (PickupSlot keyhole in Keyholes)
		{
			if (IsInstanceValid(keyhole))
				keyhole.OccupantChanged -= OnOccupantChanged;
		}
	}

	private void OnOccupantChanged(string slotId, string itemName)
	{
		if (IsSolved || itemName == "")
			return;

		int filled = CountFilled();
		if (filled < Keyholes.Count)
		{
			if (ProgressMessage != "")
				Message(string.Format(ProgressMessage, filled, Keyholes.Count));
			return;
		}
		// The slot hears about its new key before the key has moved into it and restored its collision,
		// so finish once that's done.
		CallDeferred(MethodName.Solve);
	}

	private int CountFilled()
	{
		int filled = 0;
		foreach (PickupSlot keyhole in Keyholes)
		{
			if (keyhole?.Occupant != null && keyhole.Occupant.ItemName == ItemName)
				filled++;
		}
		return filled;
	}

	private void Solve()
	{
		if (IsSolved || CountFilled() < Keyholes.Count)
			return;
		IsSolved = true;
		foreach (PickupSlot keyhole in Keyholes)
		{
			// Neither the keyholes nor the keys can be aimed at any more, so the keys stay put.
			keyhole.CollisionLayer = 0;
			keyhole.Occupant.CollisionLayer = 0;
			Turn(keyhole.Occupant, keyhole.GlobalTransform);
		}
		Message(SolvedMessage);
		EmitSignal(SignalName.Solved);
	}

	// start = the keyhole's transform, which a placed key takes.
	private void Turn(Pickup key, Transform3D start)
	{
		Vector3 axis = start.Basis.X.Normalized();
		Vector3 pivot = start * new Vector3(0f, ShaftHeight, 0f);
		Tween tween = CreateTween().SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		tween.TweenMethod(Callable.From<float>(degrees =>
		{
			var turn = new Basis(axis, Mathf.DegToRad(degrees));
			key.GlobalTransform = new Transform3D(turn * start.Basis, pivot + turn * (start.Origin - pivot));
		}), 0f, TurnDegrees, TurnTime);
	}

	private static void Message(string text)
	{
		if (text != "" && Events.Instance != null)
			Events.Instance.EmitSignal(Events.SignalName.ShowMessage, text, 3f);
	}
}
