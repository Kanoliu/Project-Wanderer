using Godot;
using System;
using System.Collections.Generic;

public partial class DinnerStage : Control
{
	public event Action LineFinished;

	private readonly Dictionary<string, PanelContainer> _bubbles = new(StringComparer.Ordinal);
	private PanelContainer _activeBubble;
	private AnimationPlayer _activeAnimation;
	private Timer _readingTime;
	private double _holdSeconds;

	public override void _Ready()
	{
		Control canvas = GetNode<Control>("Composition/Canvas");
		_bubbles.Add("character.aelyar", canvas.GetNode<PanelContainer>("Aelyar/Bubble"));
		_bubbles.Add("character.wuzuona", canvas.GetNode<PanelContainer>("Wuzuona/Bubble"));
		_bubbles.Add("character.wensheng", canvas.GetNode<PanelContainer>("Wensheng/Bubble"));
		_bubbles.Add("character.yang", canvas.GetNode<PanelContainer>("Yang/Bubble"));
		foreach (PanelContainer bubble in _bubbles.Values)
		{
			bubble.Hide();
			bubble.GetNode<AnimationPlayer>("AnimationPlayer").AnimationFinished += OnAnimationFinished;
		}
		_readingTime = GetNode<Timer>("ReadingTime");
		_readingTime.Timeout += BeginExit;
	}

	public void PlayLine(string speakerId, string text)
	{
		if (!_bubbles.TryGetValue(speakerId, out _activeBubble))
		{
			GD.PushWarning($"晚餐画面中未设置角色 {speakerId} 的位置。");
			Callable.From(() => LineFinished?.Invoke()).CallDeferred();
			return;
		}

		Label label = _activeBubble.GetNode<Label>("Text");
		label.Text = text;
		_holdSeconds = Math.Max(2.4, 1.0 + text.Length / 7.0);
		_activeAnimation = _activeBubble.GetNode<AnimationPlayer>("AnimationPlayer");
		_activeAnimation.Play("appear");
		_activeAnimation.Advance(0);
		_activeBubble.Show();
	}

	public void StopLine()
	{
		_readingTime.Stop();
		_activeAnimation?.Stop();
		_activeBubble?.Hide();
		_activeBubble = null;
	}

	public void Advance()
	{
		if (!_readingTime.IsStopped())
		{
			_readingTime.Stop();
			BeginExit();
		}
	}

	private void BeginExit()
	{
		_activeAnimation.Play("disappear");
	}

	private void OnAnimationFinished(StringName animation)
	{
		if (animation == "appear")
		{
			_readingTime.Start(_holdSeconds);
		}
		else if (animation == "disappear")
		{
			_activeBubble.Hide();
			_activeBubble = null;
			LineFinished?.Invoke();
		}
	}
}
