using Godot;

namespace Gamejam.Gameplay;

// Base for puzzle checkers (SlotSequence, TorchPuzzle), so rewards like RiseOnSolved work with any of them.
public partial class Puzzle : Node
{
	[Signal] public delegate void SolvedEventHandler();
	[Signal] public delegate void UnsolvedEventHandler();

	public bool IsSolved { get; protected set; }
}
