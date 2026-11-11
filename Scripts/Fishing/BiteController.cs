using Godot;
using System;

public partial class BiteController : Control
{
	public enum BiteResult
	{
		Early,
		Hooked,
		Late
	}

	[Signal]
	public delegate void BiteStartedEventHandler();

	[Signal]
	public delegate void HookWindowHeldEventHandler();

	[Signal]
	public delegate void BiteResolvedEventHandler(int result);

	[Export] public double WaitMinimum { get; set; } = 1.5;
	[Export] public double WaitMaximum { get; set; } = 3.2;
	[Export] public FishBitePattern DefaultPattern { get; set; } =
		FishBitePattern.Probe;

	[Export] public double ProbeHookWindowStart { get; set; } = 1.45;
	[Export] public double ProbeHookWindowEnd { get; set; } = 2.35;
	[Export] public double StrikeHookWindowStart { get; set; } = 0.42;
	[Export] public double StrikeHookWindowEnd { get; set; } = 0.92;
	[Export] public double DragHookWindowStart { get; set; } = 1.55;
	[Export] public double DragHookWindowEnd { get; set; } = 2.35;

	private const double ProbeFirstTwitchEnd = 0.38;
	private const double ProbePauseEnd = 0.76;
	private const double ProbeSecondTwitchEnd = 1.16;
	private const double ProbeSignalStart = 1.22;
	private const double StrikeSignalStart = 0.24;
	private const double DragSignalStart = 1.20;
	private const double DragFrequency = 7.2;
	private const float SinkDistance = 42.0f;
	private const float DragProbeDistance = 32.0f;
	private const float DragSignalDistance = 48.0f;

	private readonly RandomNumberGenerator _random = new();

	private BobberView _visual = null!;
	private Label _phaseLabel = null!;
	private Label _debugLabel = null!;
	private FishBitePattern _activePattern = FishBitePattern.Probe;
	private double _waitDuration;
	private double _waitElapsed;
	private double _biteElapsed;
	private bool _attemptActive;
	private bool _biteActive;
	private bool _inputEnabled;
	private bool _holdAtHookWindow;
	private bool _lastResolutionWasPlayerInitiated;

	public FishBitePattern ActivePattern => _activePattern;
	public double ActiveHookWindowStart => GetHookWindowStart();
	public double ActiveHookWindowEnd => GetHookWindowEnd();
	public bool LastResolutionWasPlayerInitiated =>
		_lastResolutionWasPlayerInitiated;

	public override void _Ready()
	{
		_visual = GetNode<BobberView>("Visual");
		_phaseLabel = GetNode<Label>("PhaseLabel");
		_debugLabel = GetNode<Label>("DebugLabel");
		_random.Randomize();
		SetProcess(false);
	}

	public override void _Process(double delta)
	{
		if (!_attemptActive)
		{
			return;
		}

		if (!_biteActive)
		{
			_waitElapsed += delta;
			UpdateWaitingDisplay();

			if (_waitElapsed >= _waitDuration)
			{
				_biteActive = true;
				_biteElapsed = 0.0;
				EmitSignal(SignalName.BiteStarted);
			}

			return;
		}

		_biteElapsed += delta;
		if (_holdAtHookWindow && _biteElapsed >= ActiveHookWindowStart)
		{
			_holdAtHookWindow = false;
			_biteElapsed = ActiveHookWindowStart;
			UpdateBiteMotion();
			UpdateBiteDisplay();
			PauseAttempt();
			EmitSignal(SignalName.HookWindowHeld);
			return;
		}

		UpdateBiteMotion();
		UpdateBiteDisplay();

		if (_biteElapsed > ActiveHookWindowEnd)
		{
			Resolve(BiteResult.Late, false);
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (!_attemptActive ||
			!_inputEnabled ||
			!@event.IsActionPressed("fishing_hook", false))
		{
			return;
		}

		Hook();
		GetViewport().SetInputAsHandled();
	}

	public void StartAttempt()
	{
		StartAttempt(DefaultPattern);
	}

	public void StartAttempt(FishBitePattern pattern)
	{
		StartAttempt(pattern, false);
	}

	public void StartAttempt(FishBitePattern pattern, bool holdAtHookWindow)
	{
		ResetAttempt();
		_activePattern = Enum.IsDefined(pattern)
			? pattern
			: DefaultPattern;
		_waitDuration = _random.RandfRange(
			(float)WaitMinimum,
			(float)WaitMaximum
		);
		_attemptActive = true;
		_inputEnabled = true;
		_holdAtHookWindow = holdAtHookWindow;
		SetProcess(true);
		UpdateWaitingDisplay();
	}

	public void ShowIdle(string message)
	{
		ResetAttempt();
		_visual.ResetVisual();
		_phaseLabel.Text = message;
		_debugLabel.Text = "等待剧情中的鱼讯窗口。";
	}

	public void PauseAttempt()
	{
		if (_attemptActive)
		{
			_inputEnabled = false;
			SetProcess(false);
			_visual.SetPaused(true);
		}
	}

	public void ResumeAttempt()
	{
		if (_attemptActive)
		{
			_inputEnabled = true;
			SetProcess(true);
			_visual.SetPaused(false);
		}
	}

	public void ResetAttempt()
	{
		_attemptActive = false;
		_biteActive = false;
		_inputEnabled = false;
		_holdAtHookWindow = false;
		_waitElapsed = 0.0;
		_biteElapsed = 0.0;
		_lastResolutionWasPlayerInitiated = false;
		SetProcess(false);

		if (IsInstanceValid(_visual)) _visual.ResetVisual();
	}

	public void Hook()
	{
		if (!_attemptActive || !_inputEnabled)
		{
			return;
		}

		if (!_biteActive || _biteElapsed < ActiveHookWindowStart)
		{
			Resolve(BiteResult.Early, true);
			return;
		}

		if (_biteElapsed <= ActiveHookWindowEnd)
		{
			Resolve(BiteResult.Hooked, true);
			return;
		}

		Resolve(BiteResult.Late, true);
	}

	private void Resolve(
		BiteResult result,
		bool playerInitiated)
	{
		if (!_attemptActive)
		{
			return;
		}

		_attemptActive = false;
		_inputEnabled = false;
		_lastResolutionWasPlayerInitiated = playerInitiated;
		SetProcess(false);
		EmitSignal(SignalName.BiteResolved, (int)result);
	}

	private void UpdateBiteMotion()
	{
		switch (_activePattern)
		{
			case FishBitePattern.Probe:
				UpdateProbeMotion();
				break;

			case FishBitePattern.Strike:
				UpdateStrikeMotion();
				break;

			case FishBitePattern.Drag:
				UpdateDragMotion();
				break;
		}
	}

	private void UpdateProbeMotion()
	{
		float horizontalOffset = 0.0f;
		float verticalOffset = 0.0f;
		float rotation = 0.0f;

		if (_biteElapsed < ProbeFirstTwitchEnd)
		{
			double strength = Math.Sin(_biteElapsed * 32.0);
			horizontalOffset = (float)strength * 3.0f;
			rotation = (float)strength * 0.08f;
			_phaseLabel.Text = "浮漂轻轻点了一下……";
		}
		else if (_biteElapsed < ProbePauseEnd)
		{
			_phaseLabel.Text = "水面又安静下来……";
		}
		else if (_biteElapsed < ProbeSecondTwitchEnd)
		{
			double strength = Math.Sin(
				(_biteElapsed - ProbePauseEnd) * 42.0
			);
			horizontalOffset = (float)strength * 5.0f;
			rotation = (float)strength * 0.12f;
			_phaseLabel.Text = "又是一次试探……";
		}
		else if (_biteElapsed < ProbeSignalStart)
		{
			_phaseLabel.Text = "水面又安静下来……";
		}
		else
		{
			float sinkProgress = GetSignalProgress(
				ProbeSignalStart,
				ProbeHookWindowStart
			);
			verticalOffset = SinkDistance * sinkProgress;
			_phaseLabel.Text = _biteElapsed < ActiveHookWindowStart
				? "浮漂正在下沉……"
				: "浮漂没入水中——提竿！";
		}

		ApplyBobberMotion(
			new Vector2(horizontalOffset, verticalOffset),
			rotation
		);
	}

	private void UpdateStrikeMotion()
	{
		float horizontalOffset = 0.0f;
		float verticalOffset;
		float rotation = 0.0f;

		if (_biteElapsed < StrikeSignalStart)
		{
			double warningProgress =
				_biteElapsed / StrikeSignalStart;
			double strength = Math.Sin(
				warningProgress * Math.PI * 2.0
			);
			horizontalOffset = (float)strength * 2.0f;
			verticalOffset = (float)Math.Sin(
				warningProgress * Math.PI
			) * 2.0f;
			rotation = (float)strength * 0.06f;
			_phaseLabel.Text = "一次突然的抽动……";
		}
		else
		{
			float sinkProgress = GetSignalProgress(
				StrikeSignalStart,
				StrikeHookWindowStart
			);
			verticalOffset = SinkDistance * 1.18f * sinkProgress;
			_phaseLabel.Text = _biteElapsed < ActiveHookWindowStart
				? "浮漂猛地扎进水里！"
				: "现在提竿！";
		}

		ApplyBobberMotion(
			new Vector2(horizontalOffset, verticalOffset),
			rotation
		);
	}

	private void UpdateDragMotion()
	{
		float horizontalOffset;
		float verticalOffset = 0.0f;
		float rotation;

		if (_biteElapsed < DragSignalStart)
		{
			double wave = Math.Sin(_biteElapsed * DragFrequency);
			horizontalOffset = (float)wave * DragProbeDistance;
			rotation = (float)Math.Cos(
				_biteElapsed * DragFrequency
			) * 0.08f;
			_phaseLabel.Text = "浮漂正在左右游移……";
		}
		else
		{
			float signalStartX = (float)Math.Sin(
				DragSignalStart * DragFrequency
			) * DragProbeDistance;
			float dragDirection = Math.Cos(
				DragSignalStart * DragFrequency
			) >= 0.0
				? 1.0f
				: -1.0f;
			float sinkProgress = GetSignalProgress(
				DragSignalStart,
				DragHookWindowStart
			);

			horizontalOffset = signalStartX +
				dragDirection * DragSignalDistance * sinkProgress;
			verticalOffset = SinkDistance * sinkProgress;
			rotation = dragDirection * 0.24f * sinkProgress;
			_phaseLabel.Text = _biteElapsed < ActiveHookWindowStart
				? "鱼线斜着沉了下去！"
				: "现在提竿！";
		}

		ApplyBobberMotion(
			new Vector2(horizontalOffset, verticalOffset),
			rotation
		);
	}

	private float GetSignalProgress(
		double signalStart,
		double hookWindowStart)
	{
		if (hookWindowStart <= signalStart)
		{
			return 1.0f;
		}

		float progress = Mathf.Clamp(
			(float)((_biteElapsed - signalStart) /
				(hookWindowStart - signalStart)),
			0.0f,
			1.0f
		);

		return progress * progress * (3.0f - 2.0f * progress);
	}

	private void ApplyBobberMotion(Vector2 offset, float rotation)
	{
		_visual.SetMotion(offset, rotation);
	}

	private void UpdateWaitingDisplay()
	{
		_phaseLabel.Text = "等待鱼靠近……";
		_debugLabel.Text =
			$"鱼讯：{GetPatternDisplayName()}\n" +
			$"提竿窗口：{ActiveHookWindowStart:0.00} - " +
			$"{ActiveHookWindowEnd:0.00}\n" +
			"当前：等待";
	}

	private void UpdateBiteDisplay()
	{
		_debugLabel.Text =
			$"鱼讯：{GetPatternDisplayName()}\n" +
			$"提竿窗口：{ActiveHookWindowStart:0.00} - " +
			$"{ActiveHookWindowEnd:0.00}\n" +
			$"当前：{_biteElapsed:0.00}";
	}

	private double GetHookWindowStart()
	{
		return _activePattern switch
		{
			FishBitePattern.Probe => ProbeHookWindowStart,
			FishBitePattern.Strike => StrikeHookWindowStart,
			FishBitePattern.Drag => DragHookWindowStart,
			_ => ProbeHookWindowStart
		};
	}

	private double GetHookWindowEnd()
	{
		return _activePattern switch
		{
			FishBitePattern.Probe => ProbeHookWindowEnd,
			FishBitePattern.Strike => StrikeHookWindowEnd,
			FishBitePattern.Drag => DragHookWindowEnd,
			_ => ProbeHookWindowEnd
		};
	}

	private string GetPatternDisplayName()
	{
		return _activePattern switch
		{
			FishBitePattern.Probe => "Probe / 试探型",
			FishBitePattern.Strike => "Strike / 猛咬型",
			FishBitePattern.Drag => "Drag / 拖饵型",
			_ => _activePattern.ToString()
		};
	}

}
