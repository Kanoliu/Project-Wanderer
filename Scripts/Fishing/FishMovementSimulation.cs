using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

/// <summary>Engine-independent movement. A seed and the same input steps reproduce a fight.</summary>
public sealed class FishMovementSimulation
{
    private const float DegreesToRadians = MathF.PI / 180;
    private readonly FishMovementSettings _settings;
    private readonly Random _random;
    private readonly Random _diveRandom;
    private readonly Queue<(float Time, Vector2 Center, Vector2 Size)> _boxHistory = new();
    private (float Time, Vector2 Center, Vector2 Size) _observedBox;
    private Vector2 _lastBoxPosition;
    private Vector2 _previousBoxVelocity;
    private bool _hasBox;
    private float _heading;
    private float _targetHeading;
    private float _targetCruiseSpeed;
    private float _steeringRemaining;
    private float _burstRemaining;
    private float _untilBurst;
    private float _diveRemaining;
    private float _untilDive;
    private float _elapsed;
    private float _pauseRemaining;
    private float _untilPause;
    private float _wallSpeedLimit = float.PositiveInfinity;
    private const float VisualPadding = 0.07f;

    public Vector2 Position { get; private set; }
    public float Speed { get; private set; }
    public bool IsBursting => _burstRemaining > 0;
    public bool IsDiving => _diveRemaining > 0;
    public int DiveCount { get; private set; }
    public int BurstCount { get; private set; }
    public int CounterTurnCount { get; private set; }
    public Vector2 LastCounterDirection { get; private set; }
    public int BoundaryCorrectionCount { get; private set; }
    public float FirstDiveDelay { get; }
    public bool IsResting => _pauseRemaining > 0;

    public FishMovementSimulation(FishMovementSettings settings, int seed, Vector2 position)
    {
        _settings = (settings ?? new FishMovementSettings()).Copy();
        string[] errors = _settings.ValidationErrors().ToArray();
        if (errors.Length != 0) throw new ArgumentException(string.Join(" ", errors), nameof(settings));
        _random = new Random(seed);
        _diveRandom = new Random(seed ^ 0x51ED270B);
        Position = position;
        _heading = _targetHeading = Range(-MathF.PI, MathF.PI);
        Speed = _targetCruiseSpeed = _settings.CruiseSpeed;
        _untilBurst = Jitter(_settings.BurstInterval);
        _untilDive = NextDiveDelay();
        FirstDiveDelay = _untilDive;
        _untilPause = _settings.PauseInterval > 0 ? Jitter(_settings.PauseInterval) : float.PositiveInfinity;
    }

    /// <param name="bounds">Available top-left travel area after subtracting fish size, in normalized units.</param>
    public void Step(float delta, Vector2 bounds, Vector2 fishSize, Vector2 boxPosition, Vector2 boxSize)
    {
        if (!float.IsFinite(delta) || delta <= 0) return;
        bounds = Vector2.Max(Vector2.Zero, bounds);
        if (!_hasBox)
        {
            _observedBox = (0, boxPosition + boxSize * 0.5f, boxSize);
            _lastBoxPosition = boxPosition;
            _hasBox = true;
        }
        _boxHistory.Enqueue((_elapsed, boxPosition + boxSize * 0.5f, boxSize));
        // Small integration steps keep turning, boundary handling and event clocks stable at low FPS.
        float remaining = delta;
        while (remaining > 0)
        {
            float dt = MathF.Min(remaining, 1f / 120);
            remaining -= dt;
            _elapsed += dt;
            while (_boxHistory.Count > 0 && _boxHistory.Peek().Time <= _elapsed - _settings.ReactionDelay)
                _observedBox = _boxHistory.Dequeue();
            UpdateEvents(dt);
            UpdateSteering(dt, bounds, fishSize);
            float targetSpeed = IsResting ? _settings.CruiseSpeed * 0.08f
                : _targetCruiseSpeed * (IsBursting ? _settings.BurstSpeedMultiplier : 1);
            targetSpeed = MathF.Min(targetSpeed, _wallSpeedLimit);
            Speed = Approach(Speed, targetSpeed, _settings.Acceleration * dt);
            Vector2 padding = Vector2.Min(new Vector2(VisualPadding), bounds * 0.25f);
            Vector2 next = Position + Direction(_heading) * Speed * dt;
            Position = Vector2.Clamp(next, padding, bounds - padding);
            if (Vector2.DistanceSquared(next, Position) > 0.00000001f) BoundaryCorrectionCount++;
        }
        // Used only by the next frame's burst: never counter the player's current-frame input.
        _previousBoxVelocity = (boxPosition - _lastBoxPosition) / delta;
        _lastBoxPosition = boxPosition;
    }

    private void UpdateEvents(float dt)
    {
        if (IsResting) _pauseRemaining = MathF.Max(0, _pauseRemaining - dt);
        else if (_settings.PauseInterval > 0 && _settings.PauseDuration > 0)
        {
            _untilPause -= dt;
            if (_untilPause <= 0 && !IsBursting)
            {
                _pauseRemaining = Jitter(_settings.PauseDuration);
                _untilPause = Jitter(_settings.PauseInterval);
            }
        }
        if (_burstRemaining > 0) _burstRemaining = MathF.Max(0, _burstRemaining - dt);
        else if (!IsResting && _settings.BurstInterval > 0 && _settings.BurstDuration > 0)
        {
            _untilBurst -= dt;
            if (_untilBurst <= 0)
            {
                _burstRemaining = Jitter(_settings.BurstDuration);
                _untilBurst = Jitter(_settings.BurstInterval);
                BurstCount++;
                if (_previousBoxVelocity.LengthSquared() > 0.0001f &&
                    _random.NextDouble() < _settings.BurstCounterTurnChance)
                {
                    Vector2 opposite = -Vector2.Normalize(_previousBoxVelocity);
                    _targetHeading = MathF.Atan2(opposite.Y, opposite.X) + Range(-15, 15) * DegreesToRadians;
                    LastCounterDirection = Direction(_targetHeading);
                    CounterTurnCount++;
                }
            }
        }

        // Independent event clock: diving never changes movement, steering or capture logic.
        if (IsDiving) _diveRemaining = MathF.Max(0, _diveRemaining - dt);
        else if (_settings.DiveRate > 0 && _settings.DiveDuration > 0)
        {
            _untilDive -= dt;
            if (_untilDive <= 0)
            {
                _diveRemaining = _settings.DiveDuration;
                _untilDive = NextDiveDelay();
                DiveCount++;
            }
        }
    }

    private void UpdateSteering(float dt, Vector2 bounds, Vector2 fishSize)
    {
        _steeringRemaining -= dt;
        if (_steeringRemaining <= 0 && !IsBursting)
        {
            _steeringRemaining = Jitter(_settings.SteeringInterval);
            _targetCruiseSpeed = _settings.CruiseSpeed * (1 + Range(-1, 1) * _settings.SpeedVariation);
            float wander = _heading + Range(-1, 1) * _settings.WanderAngle * DegreesToRadians;
            if (_settings.ReversalChance > 0 && _random.NextDouble() < _settings.ReversalChance)
                wander = _heading + MathF.PI + Range(-20, 20) * DegreesToRadians;
            Vector2 intention = Direction(wander);
            if (_settings.HorizontalBias > 0)
                intention = Vector2.Lerp(intention,
                    new Vector2(intention.X >= 0 ? 1 : -1, intention.Y * 0.12f), _settings.HorizontalBias);
            Vector2 center = Position + fishSize * 0.5f;
            Vector2 boxMin = _observedBox.Center - _observedBox.Size * 0.5f;
            Vector2 boxMax = boxMin + _observedBox.Size;
            Vector2 nearest = Vector2.Clamp(center, boxMin, boxMax);
            if (Vector2.DistanceSquared(center, nearest) < 0.08f * 0.08f)
            {
                // Prefer a nearby exit rather than always fleeing radially from the box centre.
                float[] distances = { MathF.Abs(center.X - boxMin.X), MathF.Abs(boxMax.X - center.X),
                    MathF.Abs(center.Y - boxMin.Y), MathF.Abs(boxMax.Y - center.Y) };
                Vector2[] exits = { -Vector2.UnitX, Vector2.UnitX, -Vector2.UnitY, Vector2.UnitY };
                int exit = 0;
                float best = float.PositiveInfinity;
                for (int i = 0; i < distances.Length; i++)
                {
                    float score = distances[i] + Range(0, 0.06f);
                    if (score < best) { best = score; exit = i; }
                }
                float angle = MathF.Atan2(exits[exit].Y, exits[exit].X) + Range(-30, 30) * DegreesToRadians;
                intention = Vector2.Lerp(intention, Direction(angle), _settings.AvoidanceStrength);
            }
            if (intention.LengthSquared() > 0.0001f)
                _targetHeading = MathF.Atan2(intention.Y, intention.X);
        }

        // Turn before the visual silhouette reaches the wall, accounting for speed and turn radius.
        Vector2 padding = Vector2.Min(new Vector2(VisualPadding), bounds * 0.25f);
        float edgeTurnRate = MathF.Max(300, IsBursting ? _settings.BurstTurnRate : _settings.MaxTurnRate);
        float radius = Speed / (edgeTurnRate * DegreesToRadians);
        float margin = MathF.Max(_settings.EdgeAvoidanceDistance, radius * 1.3f + 0.06f);
        Vector2 forward = Position + Direction(_heading) * Speed * 0.35f;
        Vector2 edge = new(
            MathF.Max(0, padding.X + margin - forward.X) - MathF.Max(0, forward.X - (bounds.X - padding.X - margin)),
            MathF.Max(0, padding.Y + margin - forward.Y) - MathF.Max(0, forward.Y - (bounds.Y - padding.Y - margin)));
        float desired = _targetHeading;
        bool avoidingEdge = edge.LengthSquared() > 0.000001f;
        if (avoidingEdge)
        {
            desired = MathF.Atan2(edge.Y, edge.X);
            _targetHeading = desired; // Do not immediately resume an intention pointing into the same wall.
        }
        float turnRate = IsBursting ? _settings.BurstTurnRate : _settings.MaxTurnRate;
        if (avoidingEdge) turnRate = edgeTurnRate;
        float difference = MathF.IEEERemainder(desired - _heading, MathF.Tau);
        _heading = MathF.IEEERemainder(_heading + Math.Clamp(difference,
            -turnRate * DegreesToRadians * dt, turnRate * DegreesToRadians * dt), MathF.Tau);
        Vector2 direction = Direction(_heading);
        float distance = float.PositiveInfinity;
        if (MathF.Abs(direction.X) > 0.001f)
            distance = MathF.Min(distance, (direction.X > 0 ? bounds.X - padding.X - Position.X : padding.X - Position.X) / direction.X);
        if (MathF.Abs(direction.Y) > 0.001f)
            distance = MathF.Min(distance, (direction.Y > 0 ? bounds.Y - padding.Y - Position.Y : padding.Y - Position.Y) / direction.Y);
        _wallSpeedLimit = MathF.Sqrt(2 * _settings.Acceleration * MathF.Max(0, distance - 0.02f));
    }

    private float NextDiveDelay()
    {
        if (_settings.DiveRate <= 0) return float.PositiveInfinity;
        float mean = 60 / _settings.DiveRate;
        float minimum = MathF.Min(_settings.DiveMinDelay, mean * 0.75f);
        return minimum + (float)(-Math.Log(1 - _diveRandom.NextDouble()) * (mean - minimum));
    }
    private float Range(float min, float max) => min + (max - min) * (float)_random.NextDouble();
    private float Jitter(float value) => value * Range(0.7f, 1.3f);
    private static Vector2 Direction(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));
    private static float Approach(float current, float target, float maximumDelta) =>
        current + Math.Clamp(target - current, -maximumDelta, maximumDelta);
}
