using Godot;

namespace Gamejam.Gameplay;

// The player's hotbar of carried Pickups. Lives as a child of Player.
// Always packed from slot 1: there are never gaps, so the numbers shown are 1, 2, 3... with no missing ones. When an
// item leaves (placed, dropped, put on a wall), the items after it move down a slot.
public partial class Inventory : Node
{
	public const int SlotCount = 9;

	// Fires when contents or the selected slot change. The HUD redraws from GetItem / SelectedIndex.
	[Signal] public delegate void ChangedEventHandler();

	public int SelectedIndex { get; private set; }
	// Put away: the selected slot keeps its item, but nothing is in hand.
	public bool Holstered { get; private set; }
	public Pickup SelectedItem => Holstered ? null : _items[SelectedIndex];
	public int Count { get; private set; }
	public bool IsFull => Count >= SlotCount;

	private readonly Pickup[] _items = new Pickup[SlotCount];

	public Pickup GetItem(int index) => _items[index];

	// Goes after the last item. With nothing in hand, it goes straight into the hand.
	public bool TryAdd(Pickup pickup)
	{
		if (IsFull)
			return false;
		bool handEmpty = SelectedItem == null;
		_items[Count] = pickup;
		if (handEmpty)
		{
			SelectedIndex = Count;
			Holstered = false;
		}
		Count++;
		EmitSignal(SignalName.Changed);
		return true;
	}

	// Takes the item in hand out of the hotbar and closes the gap. The hand is empty afterwards.
	public Pickup RemoveSelected()
	{
		Pickup item = SelectedItem;
		if (item == null)
			return null;
		for (int i = SelectedIndex; i < Count - 1; i++)
			_items[i] = _items[i + 1];
		Count--;
		_items[Count] = null;
		SelectedIndex = Mathf.Clamp(SelectedIndex, 0, Mathf.Max(Count - 1, 0));
		Holstered = true;
		EmitSignal(SignalName.Changed);
		return item;
	}

	// Selecting the slot that's already selected puts its item away, or takes it out again. Empty slots can't be
	// selected.
	public void Select(int index)
	{
		if (index < 0 || index >= Count)
			return;
		if (index == SelectedIndex)
			Holstered = !Holstered;
		else
		{
			SelectedIndex = index;
			Holstered = false;
		}
		EmitSignal(SignalName.Changed);
	}

	// Scrolls through the items there are, wrapping around, and takes the new one out.
	public void Cycle(int direction)
	{
		if (Count == 0)
			return;
		SelectedIndex = Mathf.PosMod(SelectedIndex + direction, Count);
		Holstered = false;
		EmitSignal(SignalName.Changed);
	}
}
