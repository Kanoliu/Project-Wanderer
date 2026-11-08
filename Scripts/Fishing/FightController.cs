using Godot;

public partial class FightController : Control
{
	[Signal]
	public delegate void FightResolvedEventHandler(bool caught);

	[Export] public float CatchAreaSpeed { get; set; } = 220.0f;
	[Export] public float GainRate { get; set; } = 0.32f;
	[Export] public float LossRate { get; set; } = 0.08f;
	[Export] public float FeedbackSeconds { get; set; } = 0.12f;

	private const float InitialProgress = 0.35f;

	private Control _fishingArea = null!;
	private Control _fishCursor = null!;
	private Control _catchArea = null!;
	private ProgressBar _progressBar = null!;
	private FishMovementSimulation _movement;
	private StyleBox _insideStyle;
	private StyleBox _outsideStyle;
	private StyleBoxFlat _feedbackStyle;
	private float _feedbackAmount;
	private SwimRipples _ripples;
	private FishDiveVisual _fishVisual;
	private Control _insideMark;
	private Control _reticle;
	private CpuParticles2D _diveParticles;
	private bool _wasDiving;
	private WaterDrift _waterDrift;
	public int MovementSeed { get; private set; }
	public bool FishInsideBox { get; private set; }
	public bool FishIsDiving => _movement?.IsDiving ?? false;
	private float _progress;
	private bool _fightActive;

	public override void _Ready()
	{
		_fishingArea = GetNode<Control>("FishingArea");
		_fishCursor = GetNode<Control>("FishingArea/FishCursor");
		_catchArea = GetNode<Control>("FishingArea/CatchArea");
		_progressBar = GetNode<ProgressBar>("ProgressBar");
		_insideStyle = _catchArea.GetThemeStylebox("inside", "FightCatch");
		_outsideStyle = _catchArea.GetThemeStylebox("outside", "FightCatch");
		_feedbackStyle = (StyleBoxFlat)_outsideStyle.Duplicate();
		_catchArea.AddThemeStyleboxOverride("panel", _feedbackStyle);
		_ripples = GetNode<SwimRipples>("FishingArea/SwimRipples");
		_fishVisual = GetNode<FishDiveVisual>("FishingArea/FishCursor/Appearance");
		_insideMark = GetNode<Control>("FishingArea/CatchArea/InsideMark");
		_reticle = GetNode<Control>("FishingArea/CatchArea/Reticle");
		_diveParticles = GetNode<CpuParticles2D>("FishingArea/DiveParticles");
		_waterDrift = GetNode<WaterDrift>("FishingArea/WaterSurface/WaterDrift");
		_waterDrift.SetPaused(true);
		SetProcess(false);
	}

	public override void _Process(double delta)
	{
		if (!_fightActive)
		{
			return;
		}

		float frameDelta = (float)delta;
		MoveCatchArea(frameDelta);
		MoveFish(frameDelta);
		UpdateProgress(frameDelta);
		_feedbackAmount = Mathf.MoveToward(_feedbackAmount, FishInsideBox ? 1 : 0,
			frameDelta / Mathf.Max(0.01f, FeedbackSeconds));
		ApplyCatchStyle();
	}

	public void StartFight(FishMovementSettings settings = null, int? seed = null,
		float? gainRate = null, float? lossRate = null)
	{
		if (gainRate.HasValue) GainRate = gainRate.Value;
		if (lossRate.HasValue) LossRate = lossRate.Value;
		_progress = InitialProgress;
		_fightActive = true;
		_fishCursor.Position = (_fishingArea.Size - _fishCursor.Size) * 0.5f;
		_catchArea.Position = (_fishingArea.Size - _catchArea.Size) * 0.5f;
		MovementSeed = seed ?? System.Random.Shared.Next();
		float scale = MovementScale();
		_movement = new FishMovementSimulation(settings, MovementSeed,
			ToNumerics(_fishCursor.Position / scale));
		_fishCursor.Visible = true;
		_fishVisual.ResetVisual();
		ResetDiveParticles();
		_ripples.ResetRipples();
		_waterDrift.SetPaused(false);
		UpdateCatchFeedback();
		_feedbackAmount = FishInsideBox ? 1 : 0;
		ApplyCatchStyle();
		UpdateProgressDisplay();
		SetProcess(true);
	}

	public void ResetFight()
	{
		_fightActive = false;
		_progress = InitialProgress;
		_movement = null;
		if (IsInstanceValid(_fishCursor)) _fishCursor.Visible = true;
		if (IsInstanceValid(_fishVisual)) _fishVisual.ResetVisual();
		if (IsInstanceValid(_waterDrift)) _waterDrift.SetPaused(true);
		if (IsInstanceValid(_diveParticles)) ResetDiveParticles();
		if (IsInstanceValid(_ripples)) _ripples.ResetRipples();
		SetProcess(false);

		if (IsInstanceValid(_progressBar))
		{
			UpdateProgressDisplay();
		}
	}

	public void PauseFight()
	{
		if (_fightActive)
		{
			SetProcess(false);
			_waterDrift.SetPaused(true);
			_diveParticles.SpeedScale = 0;
		}
	}

	public void ResumeFight()
	{
		if (_fightActive)
		{
			SetProcess(true);
			_waterDrift.SetPaused(false);
			_diveParticles.SpeedScale = 1;
		}
	}

	private void MoveCatchArea(float delta)
	{
		Vector2 inputDirection = Input.GetVector(
			"fishing_left",
			"fishing_right",
			"fishing_up",
			"fishing_down"
		);

		_catchArea.Position = ClampToFishingArea(
			_catchArea.Position + inputDirection * CatchAreaSpeed * delta,
			_catchArea.Size
		);
	}

	private void MoveFish(float delta)
	{
		Vector2 previous = _fishCursor.Position;
		float scale = MovementScale();
		_movement.Step(delta,
			ToNumerics((_fishingArea.Size - _fishCursor.Size) / scale),
			ToNumerics(_fishCursor.Size / scale),
			ToNumerics(_catchArea.Position / scale), ToNumerics(_catchArea.Size / scale));
		_fishCursor.Position = new Vector2(_movement.Position.X, _movement.Position.Y) * scale;
		if (_movement.IsDiving && !_wasDiving)
		{
			// Emit once at the entry point; never track the hidden fish with particles.
			_diveParticles.Position = _fishCursor.Position + _fishCursor.Size * 0.5f;
			_diveParticles.Restart();
			_diveParticles.Emitting = true;
		}
		_wasDiving = _movement.IsDiving;
		_fishVisual.Advance(_movement.IsDiving, _fishCursor.Position - previous, delta);
		_ripples.Advance(_fishCursor.Position + _fishCursor.Size * 0.5f,
			!_movement.IsDiving && _fishVisual.Modulate.A > 0.75f, delta);
	}

	private float MovementScale() => Mathf.Max(1, Mathf.Min(_fishingArea.Size.X, _fishingArea.Size.Y));
	private static System.Numerics.Vector2 ToNumerics(Vector2 value) => new(value.X, value.Y);

	private void UpdateProgress(float delta)
	{
		UpdateCatchFeedback();
		_progress += (FishInsideBox ? GainRate : -LossRate) * delta;
		_progress = Mathf.Clamp(_progress, 0.0f, 1.0f);
		UpdateProgressDisplay();

		if (_progress >= 1.0f)
		{
			ResolveFight(true);
		}
		else if (_progress <= 0.0f)
		{
			ResolveFight(false);
		}
	}

	private void UpdateCatchFeedback()
	{
		Vector2 fishCenter = _fishCursor.Position + _fishCursor.Size * 0.5f;
		Rect2 catchBounds = new(_catchArea.Position, _catchArea.Size);
		FishInsideBox = catchBounds.HasPoint(fishCenter);
		_insideMark.Visible = FishInsideBox;
	}

	private void ApplyCatchStyle()
	{
		var inside = (StyleBoxFlat)_insideStyle;
		var outside = (StyleBoxFlat)_outsideStyle;
		_feedbackStyle.BorderColor = outside.BorderColor.Lerp(inside.BorderColor, _feedbackAmount);
		_feedbackStyle.BgColor = outside.BgColor.Lerp(inside.BgColor, _feedbackAmount);
		_reticle.Modulate = _feedbackStyle.BorderColor;
		int width = Mathf.RoundToInt(Mathf.Lerp(outside.BorderWidthLeft, inside.BorderWidthLeft, _feedbackAmount));
		_feedbackStyle.SetBorderWidthAll(width);
	}

	private void ResolveFight(bool caught)
	{
		if (!_fightActive)
		{
			return;
		}

		_fightActive = false;
		_fishCursor.Visible = true;
		_fishVisual.ResetVisual();
		ResetDiveParticles();
		_ripples.ResetRipples();
		_waterDrift.SetPaused(true);
		SetProcess(false);
		EmitSignal(SignalName.FightResolved, caught);
	}

	private Vector2 ClampToFishingArea(Vector2 position, Vector2 itemSize)
	{
		Vector2 maximum = _fishingArea.Size - itemSize;
		return new Vector2(
			Mathf.Clamp(position.X, 0.0f, maximum.X),
			Mathf.Clamp(position.Y, 0.0f, maximum.Y)
		);
	}

	private void UpdateProgressDisplay()
	{
		float percent = _progress * 100.0f;
		_progressBar.Value = percent;
	}

	private void ResetDiveParticles()
	{
		_wasDiving = false;
		_diveParticles.SpeedScale = 1;
		_diveParticles.Restart();
		_diveParticles.Emitting = false;
	}
}
