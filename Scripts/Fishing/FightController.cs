using Godot;

public partial class FightController : Control
{
	[Signal]
	public delegate void FightResolvedEventHandler(bool caught);

	[Export] public float CatchAreaSpeed { get; set; } = 245.0f;
	[Export] public float GainRate { get; set; } = 0.32f;
	[Export] public float LossRate { get; set; } = 0.08f;

	private const float InitialProgress = 0.35f;

	private Control _fishingArea = null!;
	private Control _fishCursor = null!;
	private Control _catchArea = null!;
	private ProgressBar _progressBar = null!;
	private Label _progressLabel = null!;
	private FishMovementSimulation _movement;
	private StyleBoxFlat _catchAreaStyle;
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
		_progressLabel = GetNode<Label>("ProgressLabel");
		// Each controller owns its colour feedback; never mutate shared scene resources.
		_catchAreaStyle = _catchArea.GetThemeStylebox("panel").Duplicate() as StyleBoxFlat;
		if (_catchAreaStyle != null)
			_catchArea.AddThemeStyleboxOverride("panel", _catchAreaStyle);
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
	}

	public void StartFight(FishMovementSettings settings = null, int? seed = null)
	{
		_progress = InitialProgress;
		_fightActive = true;
		_fishCursor.Position = (_fishingArea.Size - _fishCursor.Size) * 0.5f;
		_catchArea.Position = (_fishingArea.Size - _catchArea.Size) * 0.5f;
		MovementSeed = seed ?? System.Random.Shared.Next();
		float scale = MovementScale();
		_movement = new FishMovementSimulation(settings, MovementSeed,
			ToNumerics(_fishCursor.Position / scale));
		_fishCursor.Visible = true;
		UpdateCatchFeedback();
		UpdateProgressDisplay();
		SetProcess(true);
	}

	public void ResetFight()
	{
		_fightActive = false;
		_progress = InitialProgress;
		_movement = null;
		if (IsInstanceValid(_fishCursor)) _fishCursor.Visible = true;
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
		}
	}

	public void ResumeFight()
	{
		if (_fightActive)
		{
			SetProcess(true);
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
		float scale = MovementScale();
		_movement.Step(delta,
			ToNumerics((_fishingArea.Size - _fishCursor.Size) / scale),
			ToNumerics(_fishCursor.Size / scale),
			ToNumerics(_catchArea.Position / scale), ToNumerics(_catchArea.Size / scale));
		_fishCursor.Position = new Vector2(_movement.Position.X, _movement.Position.Y) * scale;
		_fishCursor.Visible = !_movement.IsDiving;
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
		if (_catchAreaStyle == null) return;
		_catchAreaStyle.BorderColor = FishInsideBox
			? new Color(0.30f, 0.90f, 0.60f, 1)
			: new Color(0.92f, 0.43f, 0.36f, 1);
		_catchAreaStyle.BgColor = FishInsideBox
			? new Color(0.15f, 0.65f, 0.40f, 0.23f)
			: new Color(0.70f, 0.23f, 0.18f, 0.16f);
	}

	private void ResolveFight(bool caught)
	{
		if (!_fightActive)
		{
			return;
		}

		_fightActive = false;
		_fishCursor.Visible = true;
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
		_progressLabel.Text = $"收竿进度：{percent:0}%";
	}
}
