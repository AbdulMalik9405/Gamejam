using Gamejam.Core;
using Godot;
using Godot.Collections;

namespace Gamejam.Gameplay;

// Light exactly the Answer torches before time runs out. The first torch lit starts the timer;
// when it runs out unsolved, every torch goes dark again and the next one lit restarts it.
public partial class TorchPuzzle : Puzzle
{
	// Every torch that can be lit, right or wrong.
	[Export] public Array<WallTorch> Torches { get; set; } = new();
	// The torches that must be lit (and no others).
	[Export] public Array<WallTorch> Answer { get; set; } = new();
	[Export] public float TimeLimit { get; set; } = 30f;
	[Export] public string StartMessage { get; set; } = "The flames will hold for thirty breaths...";
	[Export] public string FailMessage { get; set; } = "The flames die out.";

	[Signal] public delegate void TimerStartedEventHandler(float seconds);
	[Signal] public delegate void TimedOutEventHandler();

	public bool IsRunning { get; private set; }
	public double TimeLeft { get; private set; }

	public override void _Ready()
	{
		foreach (WallTorch torch in Torches)
		{
			if (torch != null)
				torch.LitChanged += OnLitChanged;
		}
		SetProcess(false);
	}

	public override void _ExitTree()
	{
		foreach (WallTorch torch in Torches)
		{
			if (IsInstanceValid(torch))
				torch.LitChanged -= OnLitChanged;
		}
	}

	public override void _Process(double delta)
	{
		TimeLeft -= delta;
		if (TimeLeft > 0)
			return;

		IsRunning = false;
		SetProcess(false);
		foreach (WallTorch torch in Torches)
			torch?.Extinguish();
		Message(FailMessage);
		EmitSignal(SignalName.TimedOut);
	}

	private void OnLitChanged(WallTorch torch, bool lit)
	{
		if (IsSolved || !lit)
			return;

		if (!IsRunning)
		{
			IsRunning = true;
			TimeLeft = TimeLimit;
			SetProcess(true);
			Message(StartMessage);
			EmitSignal(SignalName.TimerStarted, TimeLimit);
		}

		if (!AnswerIsLit())
			return;
		IsRunning = false;
		SetProcess(false);
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
		if (text != "" && Events.Instance != null)
			Events.Instance.EmitSignal(Events.SignalName.ShowMessage, text, 3f);
	}
}
