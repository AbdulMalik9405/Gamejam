using Godot;
using Godot.Collections;

namespace Gamejam.Gameplay;

// Solved when every listed slot holds its matching Pickup, e.g. symbol blocks arranged to spell a word.
// Pickups are local to the player using them, so each machine checks its own copy of the objects.
public partial class SlotSequence : Puzzle
{
	// The slots, in order. A Pickup here stands for the slot it starts in (Pickup.CreateSlotAtStart),
	// so blocks can be rearranged in place without adding slot nodes by hand.
	[Export] public Array<Node3D> Slots { get; set; } = new();
	// The Pickup that must end up in each slot, same order as Slots.
	[Export] public Array<Pickup> Answer { get; set; } = new();
	// Once solved, taking a piece out again doesn't undo it.
	[Export] public bool StaySolved { get; set; } = true;

	private PickupSlot[] _slots = System.Array.Empty<PickupSlot>();

	public override void _Ready()
	{
		// A Pickup only knows its start slot after its own _Ready, which may come after ours.
		CallDeferred(MethodName.Setup);
	}

	public override void _ExitTree()
	{
		foreach (PickupSlot slot in _slots)
		{
			if (IsInstanceValid(slot))
				slot.OccupantChanged -= OnOccupantChanged;
		}
	}

	private void Setup()
	{
		if (Slots.Count != Answer.Count || Slots.Count == 0)
		{
			GD.PushWarning($"{Name}: Slots ({Slots.Count}) and Answer ({Answer.Count}) must be the same, non-zero length.");
			return;
		}

		_slots = new PickupSlot[Slots.Count];
		for (int i = 0; i < Slots.Count; i++)
		{
			// Read the start slot now: Pickup.Slot changes as soon as the block is moved.
			PickupSlot slot = Slots[i] switch
			{
				PickupSlot s => s,
				Pickup p => p.Slot,
				_ => null,
			};
			if (slot == null)
			{
				GD.PushWarning($"{Name}: Slots[{i}] is not a PickupSlot or a Pickup with CreateSlotAtStart on.");
				_slots = System.Array.Empty<PickupSlot>();
				return;
			}
			_slots[i] = slot;
			slot.OccupantChanged += OnOccupantChanged;
		}
		Check();
	}

	private void OnOccupantChanged(string slotId, string itemName) => Check();

	private void Check()
	{
		if (IsSolved && StaySolved)
			return;

		bool solved = true;
		for (int i = 0; i < _slots.Length; i++)
		{
			if (_slots[i].Occupant != Answer[i])
			{
				solved = false;
				break;
			}
		}

		if (solved == IsSolved)
			return;
		IsSolved = solved;
		EmitSignal(solved ? SignalName.Solved : SignalName.Unsolved);
	}
}
