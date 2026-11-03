using Godot;
using System;

public partial class GameSession : Node
{
	public GameState State { get; private set; } = new();

	public void StartNewGame()
	{
		State = new GameState();
	}

	public void LoadState(GameState state)
	{
		State = state ?? throw new ArgumentNullException(nameof(state));
	}
}
