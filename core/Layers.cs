namespace Gamejam.Core;

// Physics layer bitmasks. Names match [layer_names] in project.godot.
public static class Layers
{
	public const uint World = 1u << 0;
	public const uint Players = 1u << 1;
	public const uint Interactables = 1u << 2;
	public const uint Triggers = 1u << 3;
}
