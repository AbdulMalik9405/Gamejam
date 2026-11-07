using Godot;

namespace Gamejam.Core;

// Hooks for local feedback (sound, VFX, messages). Anyone may emit; listeners never change game state.
public partial class Events : Node
{
	public static Events Instance { get; private set; }

	// Vector3.Zero = non-positional.
	[Signal] public delegate void PlaySfxEventHandler(string sfxName, Vector3 position);
	[Signal] public delegate void SpawnFxEventHandler(string fxName, Vector3 position);
	[Signal] public delegate void CameraShakeEventHandler(float strength);
	[Signal] public delegate void ShowMessageEventHandler(string text, float duration);

	public override void _EnterTree() => Instance = this;
}
