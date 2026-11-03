using Godot;

/// <summary>Animates existing scene nodes. Bite timing remains owned by BiteController.</summary>
public partial class BobberView : Control
{
	[Export] public float ReferenceWidth { get; set; } = 440;
	[Export] public float BiteSinkDistance { get; set; } = 42;
	[Export] public float IdleBobAmplitude { get; set; } = 1.3f;
	[Export] public float IdleTiltAmplitude { get; set; } = 0.025f;
	[Export] public float RippleDuration { get; set; } = 2.4f;

	private Control _water = null!;
	private Control _clip = null!;
	private Control _pivot = null!;
	private TextureRect _float = null!;
	private Control _surface = null!;
	private Line2D _line = null!;
	private TextureRect[] _ripples = null!;
	private CpuParticles2D _splash = null!;
	private WaterDrift _drift = null!;
	private readonly float[] _rippleAlpha = new float[2];
	private Vector2 _motion;
	private float _rotation;
	private float _elapsed;
	private bool _idle = true;
	private bool _paused;
	private bool _splashed;

	public override void _Ready()
	{
		_water = GetNode<Control>("Water");
		_clip = GetNode<Control>("Water/BobberClip");
		_pivot = GetNode<Control>("Water/BobberClip/BobberPivot");
		_float = GetNode<TextureRect>("Water/BobberClip/BobberPivot/Bobber");
		_line = GetNode<Line2D>("Water/BobberClip/FishingLine");
		_surface = GetNode<Control>("Water/Surface");
		_splash = GetNode<CpuParticles2D>("Water/Surface/Splash");
		_drift = GetNode<WaterDrift>("Water/WaterDrift");
		_ripples = new[] {
			GetNode<TextureRect>("Water/Surface/RippleA"),
			GetNode<TextureRect>("Water/Surface/RippleB")
		};
		for (int i = 0; i < _ripples.Length; i++) _rippleAlpha[i] = _ripples[i].Modulate.A;
		ApplyPose();
	}

	public void ResetVisual()
	{
		_idle = true;
		_motion = Vector2.Zero;
		_rotation = 0;
		_splashed = false;
		SetPaused(false);
		if (!IsInstanceValid(_splash)) return;
		// Clear particles from the previous attempt, including an immediate cancel/re-enable.
		_splash.Restart();
		_splash.Emitting = false;
		ApplyPose();
	}

	public void SetMotion(Vector2 offset, float rotation)
	{
		_idle = false;
		_motion = offset;
		_rotation = rotation;
		ApplyPose();
		if (!_splashed && offset.Y >= BiteSinkDistance * 0.06f)
		{
			_splashed = true;
			_splash.Restart();
			_splash.Emitting = true;
		}
	}

	public void SetPaused(bool paused)
	{
		_paused = paused;
		if (IsInstanceValid(_drift)) _drift.SetPaused(paused);
		if (IsInstanceValid(_splash)) _splash.SpeedScale = paused ? 0 : 1;
	}

	public override void _Process(double delta)
	{
		if (_paused || !IsVisibleInTree()) return;
		_elapsed += (float)delta;
		ApplyPose();
		for (int i = 0; i < _ripples.Length; i++)
		{
			float phase = Mathf.PosMod(_elapsed / Mathf.Max(0.1f, RippleDuration) + i * 0.5f, 1);
			_ripples[i].Scale = Vector2.One * Mathf.Lerp(0.55f, 1.35f, phase);
			Color color = _ripples[i].Modulate;
			color.A = _rippleAlpha[i] * Mathf.Sin(phase * Mathf.Pi);
			_ripples[i].Modulate = color;
		}
	}

	private void ApplyPose()
	{
		if (!IsInstanceValid(_pivot)) return;
		float x = _motion.X * _water.Size.X / Mathf.Max(1, ReferenceWidth);
		// The clipping waterline stays fixed. At the hook window the complete visible stem
		// passes below it, so sinking cannot be confused with sliding across the surface.
		float y = _motion.Y / Mathf.Max(1, BiteSinkDistance) * (-_float.Position.Y + 4);
		float tilt = _rotation;
		if (_idle)
		{
			y += Mathf.Sin(_elapsed * 1.7f) * IdleBobAmplitude;
			tilt += Mathf.Sin(_elapsed * 1.1f) * IdleTiltAmplitude;
		}
		_pivot.Position = new Vector2(_clip.Size.X * 0.5f + x, _clip.Size.Y + y);
		_pivot.Rotation = tilt;
		_surface.Position = new Vector2(_water.Size.X * 0.5f + x, _clip.Size.Y);
		Vector2 tip = _pivot.Position + new Vector2(0, _float.Position.Y).Rotated(tilt);
		_line.SetPointPosition(0, new Vector2(_water.Size.X * 0.34f, -4));
		_line.SetPointPosition(1, tip);
	}
}
