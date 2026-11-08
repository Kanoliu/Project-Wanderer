using Godot;

/// <summary>Presentation only. Advanced by FightController so pause also freezes fades.</summary>
public partial class FishDiveVisual : Control
{
	[Export] public float FadeOutSeconds { get; set; } = 0.22f;
	[Export] public float FadeInSeconds { get; set; } = 0.26f;
	[Export] public float TurnResponse { get; set; } = 12.0f;
	[Export] public Vector2 SwimFrequency { get; set; } = new(0.85f, 1.35f);
	[Export] public Vector2 TailSwingRadians { get; set; } = new(0.26f, 0.40f);
	[Export] public float TurnFlex { get; set; } = 0.17f;
	[Export] public float MaxTurnBend { get; set; } = 0.90f;
	[Export] public float TurnBendResponse { get; set; } = 12.0f;
	[Export] public float TurnStrokeBoost { get; set; } = 0.08f;
	[Export] public Vector2 FinFlutterRadians { get; set; } = new(0.065f, 0.12f);
	[Export] public float FinTipFlex { get; set; } = 0.12f;
	[Export] public float FinTurnFlare { get; set; } = 0.20f;
	[Export] public float FastSwimSpeed { get; set; } = 240;
	private Bone2D[] _spine;
	private Bone2D[] _finRoots;
	private Bone2D[] _finTips;
	private static readonly float[] Flex = { 0, 0.06f, 0.18f, 0.38f, 0.62f, 0.84f, 1.0f };
	private static readonly float[] Delay = { 0, 0.08f, 0.28f, 0.55f, 0.85f, 1.15f, 1.45f };
	private readonly float[] _jointTurnLag = new float[7];
	private float _phase;
	private float _swimEffort;
	private float _turnBend;
	private float _strokeWeight;
	private float _lastTravelHeading;
	private bool _hasTravelHeading;
	public float TurnBendRadians => _turnBend;

	public override void _Ready()
	{
		_spine = new[]
		{
			GetNode<Bone2D>("Skeleton2D/Head"),
			GetNode<Bone2D>("Skeleton2D/Head/Shoulder"),
			GetNode<Bone2D>("Skeleton2D/Head/Shoulder/Body"),
			GetNode<Bone2D>("Skeleton2D/Head/Shoulder/Body/Middle"),
			GetNode<Bone2D>("Skeleton2D/Head/Shoulder/Body/Middle/RearBody"),
			GetNode<Bone2D>("Skeleton2D/Head/Shoulder/Body/Middle/RearBody/TailBase"),
			GetNode<Bone2D>("Skeleton2D/Head/Shoulder/Body/Middle/RearBody/TailBase/Tail")
		};
		_finRoots = new[]
		{
			GetNode<Bone2D>("Skeleton2D/Head/Shoulder/FinUpper"),
			GetNode<Bone2D>("Skeleton2D/Head/Shoulder/FinLower")
		};
		_finTips = new[] { _finRoots[0].GetNode<Bone2D>("Tip"), _finRoots[1].GetNode<Bone2D>("Tip") };
	}

	public void ResetVisual()
	{
		Modulate = new Color(1, 1, 1, 1);
		Rotation = 0;
		_phase = 0;
		_swimEffort = 0;
		_turnBend = 0;
		_strokeWeight = 0;
		_lastTravelHeading = 0;
		_hasTravelHeading = false;
		System.Array.Clear(_jointTurnLag);
		foreach (Bone2D bone in _spine) bone.ApplyRest();
		foreach (Bone2D bone in _finRoots) bone.ApplyRest();
		foreach (Bone2D bone in _finTips) bone.ApplyRest();
	}

	public void Advance(bool diving, Vector2 displacement, float delta)
	{
		float duration = diving ? FadeOutSeconds : FadeInSeconds;
		float alpha = Mathf.MoveToward(Modulate.A, diving ? 0 : 1,
			delta / Mathf.Max(0.001f, duration));
		Modulate = new Color(1, 1, 1, alpha);
		float speed = displacement.Length() / Mathf.Max(0.0001f, delta);
		_swimEffort = Mathf.Lerp(_swimEffort, Mathf.Clamp(speed / FastSwimSpeed, 0, 1),
			1 - Mathf.Exp(-3 * delta));
		_phase = Mathf.PosMod(_phase + delta * Mathf.Tau *
			Mathf.Lerp(SwimFrequency.X, SwimFrequency.Y, _swimEffort), Mathf.Tau);
		float angularSpeed = 0;
		float headingError = 0;
		if (displacement.LengthSquared() > 0.00001f)
		{
			float travelHeading = displacement.Angle();
			if (!_hasTravelHeading)
			{
				// Starting direction is not a turn. Avoid a false full-body swing on each cast.
				Rotation = travelHeading;
				_hasTravelHeading = true;
			}
			else
			{
				angularSpeed = Mathf.AngleDifference(_lastTravelHeading, travelHeading) / Mathf.Max(0.0001f, delta);
				headingError = Mathf.AngleDifference(Rotation, travelHeading);
				Rotation = Mathf.LerpAngle(Rotation, travelHeading, 1 - Mathf.Exp(-TurnResponse * delta));
			}
			_lastTravelHeading = travelHeading;
		}
		// The rear follows the head's old heading: bend opposite the turn, not into a rigid pivot.
		float turnTarget = -Mathf.Clamp(angularSpeed * TurnFlex + headingError * 0.55f, -MaxTurnBend, MaxTurnBend);
		_turnBend = Mathf.Lerp(_turnBend, turnTarget, 1 - Mathf.Exp(-TurnBendResponse * delta));
		_strokeWeight = Mathf.Lerp(_strokeWeight, 1, 1 - Mathf.Exp(-3 * delta));
		float moving = Mathf.SmoothStep(0, 1, Mathf.Clamp((speed - 2) / 33, 0, 1));
		float swing = Mathf.Lerp(0.045f, Mathf.Lerp(TailSwingRadians.X, TailSwingRadians.Y, _swimEffort), moving)
			+ Mathf.Abs(_turnBend) * TurnStrokeBoost;
		float parentAngle = 0;
		for (int i = 0; i < _spine.Length; i++)
		{
			// Desired angles are relative to the head. Subtract the parent angle so
			// curvature travels down the chain without compounding joint rotations.
			_jointTurnLag[i] = Mathf.Lerp(_jointTurnLag[i], _turnBend * Flex[i],
				1 - Mathf.Exp(Mathf.Lerp(-18, -8, Flex[i]) * delta));
			float angle = Flex[i] * Mathf.Sin(_phase - Delay[i]) * swing * _strokeWeight + _jointTurnLag[i];
			_spine[i].Rotation = angle - parentAngle;
			parentAngle = angle;
		}
		float flutter = Mathf.Lerp(FinFlutterRadians.X, FinFlutterRadians.Y, _swimEffort);
		for (int i = 0; i < _finRoots.Length; i++)
		{
			float side = i == 0 ? -1 : 1;
			float finPhase = _phase * 0.78f + i * 0.65f;
			float stroke = side * Mathf.Sin(finPhase) * flutter * _strokeWeight;
			float flare = _turnBend * FinTurnFlare;
			_finRoots[i].Rotation = stroke + flare;
			_finTips[i].Rotation = side * Mathf.Sin(finPhase - 0.6f) * FinTipFlex * _strokeWeight
				- stroke * 0.45f + flare * 0.35f;
		}
	}
}
