using Godot;

public partial class FightController : Control
{
	[Signal]
	public delegate void FightResolvedEventHandler(bool caught);

	[Export] public float FishSpeed { get; set; } = 145.0f;
	[Export] public float DirectionChangeInterval { get; set; } = 0.85f;
	[Export] public float CatchAreaSpeed { get; set; } = 245.0f;
	[Export] public float GainRate { get; set; } = 0.32f;
	[Export] public float LossRate { get; set; } = 0.08f;

	private const float InitialProgress = 0.35f;

	private readonly RandomNumberGenerator _random = new();

	private Control _fishingArea = null!;
	private Control _fishCursor = null!;
	private Control _catchArea = null!;
	private ProgressBar _progressBar = null!;
	private Label _progressLabel = null!;
	private Vector2 _fishDirection;
	private float _directionTimeRemaining;
	private float _progress;
	private bool _fightActive;

	public override void _Ready()
	{
		_fishingArea = GetNode<Control>("FishingArea");
		_fishCursor = GetNode<Control>("FishingArea/FishCursor");
		_catchArea = GetNode<Control>("FishingArea/CatchArea");
		_progressBar = GetNode<ProgressBar>("ProgressBar");
		_progressLabel = GetNode<Label>("ProgressLabel");
		_random.Randomize();
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

	public void StartFight()
	{
		_progress = InitialProgress;
		_fightActive = true;
		_fishCursor.Position = (_fishingArea.Size - _fishCursor.Size) * 0.5f;
		_catchArea.Position = (_fishingArea.Size - _catchArea.Size) * 0.5f;
		ChooseFishDirection();
		UpdateProgressDisplay();
		SetProcess(true);
	}

	public void ResetFight()
	{
		_fightActive = false;
		_progress = InitialProgress;
		_fishDirection = Vector2.Zero;
		_directionTimeRemaining = 0.0f;
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
		_directionTimeRemaining -= delta;

		if (_directionTimeRemaining <= 0.0f)
		{
			ChooseFishDirection();
		}

		Vector2 nextPosition = _fishCursor.Position +
			_fishDirection * FishSpeed * delta;
		Vector2 maximum = _fishingArea.Size - _fishCursor.Size;

		if (nextPosition.X < 0.0f || nextPosition.X > maximum.X)
		{
			_fishDirection.X *= -1.0f;
			nextPosition.X = Mathf.Clamp(nextPosition.X, 0.0f, maximum.X);
		}

		if (nextPosition.Y < 0.0f || nextPosition.Y > maximum.Y)
		{
			_fishDirection.Y *= -1.0f;
			nextPosition.Y = Mathf.Clamp(nextPosition.Y, 0.0f, maximum.Y);
		}

		_fishCursor.Position = nextPosition;
	}

	private void UpdateProgress(float delta)
	{
		Vector2 fishCenter = _fishCursor.Position + _fishCursor.Size * 0.5f;
		Rect2 catchBounds = new(_catchArea.Position, _catchArea.Size);
		bool fishInside = catchBounds.HasPoint(fishCenter);

		_progress += (fishInside ? GainRate : -LossRate) * delta;
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

	private void ResolveFight(bool caught)
	{
		if (!_fightActive)
		{
			return;
		}

		_fightActive = false;
		SetProcess(false);
		EmitSignal(SignalName.FightResolved, caught);
	}

	private void ChooseFishDirection()
	{
		float angle = _random.RandfRange(0.0f, Mathf.Tau);
		_fishDirection = Vector2.FromAngle(angle);
		_directionTimeRemaining = DirectionChangeInterval;
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
