using System;
using Gamejam.Core;
using Godot;

namespace Gamejam.Gameplay;

// Anything the player presses interact on that isn't a Pickup or PickupSlot (levers, buttons, wall torches).
// Give it a CollisionShape3D child. The owner reacts to Interacted, and can set PromptFor when the
// prompt depends on the player (e.g. what they're holding).
public partial class Interactable : Area3D
{
	[Export] public string Id { get; set; } = "";
	[Export] public string Prompt { get; set; } = "";
	// The mummy (the past player after the ceiling collapse) can only use Interactables with this on, e.g. levers.
	[Export] public bool MummyCanUse { get; set; }

	[Signal] public delegate void InteractedEventHandler(Player player);

	// Returns the prompt for this player, "" when there's nothing to do. Should return cached strings: it runs every frame.
	public Func<Player, string> PromptFor { get; set; }

	public override void _Ready()
	{
		AddToGroup("interactables");
		CollisionLayer = Layers.Interactables;
		CollisionMask = 0;
		Monitoring = false;
	}

	public string GetPrompt(Player player) => PromptFor != null ? PromptFor(player) : Prompt;

	public void Interact(Player player) => EmitSignal(SignalName.Interacted, player);
}
