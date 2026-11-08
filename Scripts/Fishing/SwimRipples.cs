using Godot;
using System.Collections.Generic;

/// <summary>A small scene-authored pool of world-positioned rings; no hidden-fish tracking.</summary>
public partial class SwimRipples : Control
{
	[Export] public float MinimumTravel { get; set; } = 36.0f;
	[Export] public float TurnThreshold { get; set; } = 0.85f;
	[Export] public float AccelerationThreshold { get; set; } = 80.0f;
	[Export] public float MinimumGap { get; set; } = 0.65f;
	[Export] public float Lifetime { get; set; } = 1.25f;
	[Export] public Vector2 Expansion { get; set; } = new(0.60f, 1.65f);
	private readonly List<TextureRect> _rings = new();
	private readonly List<float> _ages = new();
	private readonly List<float> _opacity = new();
	private float _cooldown;
	private float _travel;
	private float _turn;
	private Vector2 _previousCenter;
	private Vector2 _previousVelocity;
	private bool _hasMotion;
	private bool _accelerating;
	private int _next;
	public int EmittedCount { get; private set; }

	public override void _Ready()
	{
		foreach (Node child in GetChildren())
		{
			if (child is not TextureRect ring) continue;
			_rings.Add(ring);
			_ages.Add(Lifetime);
			_opacity.Add(ring.Modulate.A);
		}
		ResetRipples();
	}

	public void ResetRipples()
	{
		_cooldown = 0;
		_travel = 0;
		_turn = 0;
		_hasMotion = false;
		_accelerating = false;
		_next = 0;
		EmittedCount = 0;
		for (int i = 0; i < _rings.Count; i++)
		{
			_rings[i].Visible = false;
			_ages[i] = Lifetime;
		}
	}

	public void Advance(Vector2 center, bool mayEmit, float delta)
	{
		for (int i = 0; i < _rings.Count; i++)
		{
			_ages[i] += delta;
			float phase = Mathf.Clamp(_ages[i] / Mathf.Max(0.1f, Lifetime), 0, 1);
			TextureRect ring = _rings[i];
			ring.Visible = phase < 1;
			ring.Scale = Vector2.One * Mathf.Lerp(Expansion.X, Expansion.Y, phase);
			Color color = ring.Modulate;
			color.A = _opacity[i] * Mathf.SmoothStep(0, 0.12f, phase) *
				(1 - Mathf.SmoothStep(0.20f, 1, phase));
			ring.Modulate = color;
		}
		if (!mayEmit)
		{
			_hasMotion = false;
			_travel = 0;
			_turn = 0;
			_accelerating = false;
			return;
		}
		_cooldown = Mathf.Max(0, _cooldown - delta);
		if (!_hasMotion)
		{
			_previousCenter = center;
			_previousVelocity = Vector2.Zero;
			_hasMotion = true;
			return;
		}
		Vector2 displacement = center - _previousCenter;
		Vector2 velocity = displacement / Mathf.Max(0.0001f, delta);
		float speed = velocity.Length();
		float previousSpeed = _previousVelocity.Length();
		bool accelerationEvent = false;
		if (speed > 30 && previousSpeed > 30)
		{
			_turn += Mathf.Abs(_previousVelocity.AngleTo(velocity));
			bool accelerating = (speed - previousSpeed) / Mathf.Max(0.0001f, delta) > AccelerationThreshold;
			accelerationEvent = accelerating && !_accelerating;
			_accelerating = accelerating;
		}
		_travel += displacement.Length();
		_previousCenter = center;
		_previousVelocity = velocity;
		// The timer only limits event density. Time passing alone never emits a ring.
		if (_cooldown > 0 || _travel < MinimumTravel || _rings.Count == 0 ||
			(_turn < TurnThreshold && !accelerationEvent)) return;
		TextureRect fresh = _rings[_next];
		fresh.Position = center - fresh.Size * 0.5f;
		_ages[_next] = 0;
		fresh.Modulate = new Color(1, 1, 1, 0);
		fresh.Scale = Vector2.One * Expansion.X;
		fresh.Visible = true;
		_next = (_next + 1) % _rings.Count;
		_cooldown = MinimumGap;
		_travel = 0;
		_turn = 0;
		EmittedCount++;
	}
}
