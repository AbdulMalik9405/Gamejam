using Godot;

namespace Gamejam.Gameplay;

// The player's hotbar of carried Pickups. Lives as a child of Player.
public partial class Inventory : Node
{
	public const int SlotCount = 9;

	// Fires when contents or the selected slot change. The HUD redraws from GetItem / SelectedIndex.
	[Signal] public delegate void ChangedEventHandler();

	public int SelectedIndex { get; private set; }
	// Put away: the selected slot keeps its item, but nothing is in hand.
	public bool Holstered { get; private set; }
	public Pickup SelectedItem => Holstered ? null : _items[SelectedIndex];

	public bool IsFull
	{
		get
		{
			for (int i = 0; i < SlotCount; i++)
			{
				if (_items[i] == null)
					return false;
			}
			return true;
		}
	}

	private readonly Pickup[] _items = new Pickup[SlotCount];

	public Pickup GetItem(int index) => _items[index];

	// Goes into the selected slot when it's empty, otherwise the first free one.
	public bool TryAdd(Pickup pickup)
	{
		int index = _items[SelectedIndex] == null ? SelectedIndex : FirstFreeSlot();
		if (index < 0)
			return false;
		_items[index] = pickup;
		// Picked up straight into the hand.
		if (index == SelectedIndex)
			Holstered = false;
		EmitSignal(SignalName.Changed);
		return true;
	}

	public Pickup RemoveSelected()
	{
		Pickup item = SelectedItem;
		if (item == null)
			return null;
		_items[SelectedIndex] = null;
		EmitSignal(SignalName.Changed);
		return item;
	}

	// Selecting the slot that's already selected puts its item away, or takes it out again.
	public void Select(int index)
	{
		index = Mathf.PosMod(index, SlotCount);
		if (index == SelectedIndex)
			Holstered = !Holstered;
		else
		{
			SelectedIndex = index;
			Holstered = false;
		}
		EmitSignal(SignalName.Changed);
	}

	public void Cycle(int direction) => Select(SelectedIndex + direction);

	private int FirstFreeSlot()
	{
		for (int i = 0; i < SlotCount; i++)
		{
			if (_items[i] == null)
				return i;
		}
		return -1;
	}
}
