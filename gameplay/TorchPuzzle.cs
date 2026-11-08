using Gamejam.Core;
using Godot;
using Godot.Collections;

namespace Gamejam.Gameplay;

// Light exactly the Answer torches. Lighting a wrong torch resets the attempt.
public partial class TorchPuzzle : Puzzle
{
	// Every torch that can be lit, right or wrong.
	[Export] public Array<WallTorch> Torches { get; set; } = new();
	// The torches that must be lit (and no others).
	[Export] public Array<WallTorch> Answer { get; set; } = new();
	[Export] public string StartMessage { get; set; } = "The flames are ready...";
	[Export] public string FailMessage { get; set; } = "The flames die out.";
	// Lighting a torch that isn't in Answer fails straight away, so players can try again at once.
	[Export] public string WrongMessage { get; set; } = "That fire never burned in the past...";
	[Export] public float WrongDelay { get; set; } = 1.2f;

	public override void _Ready()
	{
		foreach (WallTorch torch in Torches)
		{
			if (torch != null)
				torch.LitChanged += OnLitChanged;
		}
	}

	public override void _ExitTree()
	{
		foreach (WallTorch torch in Torches)
		{
			if (IsInstanceValid(torch))
				torch.LitChanged -= OnLitChanged;
		}
	}

	// Lights exactly the answer (admin panel fast-forward), as if the players had.
	public void ForceSolve()
	{
		if (IsSolved)
			return;
		foreach (WallTorch torch in Answer)
			torch?.Light();
	}

	private void Fail()
	{
		foreach (WallTorch torch in Torches)
			torch?.Extinguish();
		Message(FailMessage);
	}

	private async void FailSoon()
	{
		await ToSignal(GetTree().CreateTimer(WrongDelay), SceneTreeTimer.SignalName.Timeout);
		if (!IsSolved)
			Fail();
	}

	private void OnLitChanged(WallTorch torch, bool lit)
	{
		if (IsSolved || !lit)
			return;

		if (!Answer.Contains(torch))
		{
			Message(WrongMessage);
			FailSoon();
			return;
		}

		Message(StartMessage);

		if (!AnswerIsLit())
			return;
		IsSolved = true;
		foreach (WallTorch t in Torches)
		{
			if (t != null)
				t.Locked = true;
		}
		EmitSignal(SignalName.Solved);
	}

	// Every answer torch lit, and nothing else.
	private bool AnswerIsLit()
	{
		int lit = 0;
		foreach (WallTorch torch in Torches)
		{
			if (torch != null && torch.IsLit)
				lit++;
		}
		if (lit != Answer.Count)
			return false;
		foreach (WallTorch torch in Answer)
		{
			if (torch == null || !torch.IsLit)
				return false;
		}
		return true;
	}

	private static void Message(string text)
	{
		if (text != "" && Events.Instance != null && GameState.Instance?.SkippingStages != true)
			Events.Instance.EmitSignal(Events.SignalName.ShowMessage, text, 3f);
	}
}
