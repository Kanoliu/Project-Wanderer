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

    public Vector2 Position { get; private set; }
    public float Speed { get; private set; }
    public bool IsBursting => _burstRemaining > 0;
    public bool IsDiving => _diveRemaining > 0;
    public int DiveCount { get; private set; }
    public int BurstCount { get; private set; }
    public int CounterTurnCount { get; private set; }
    public Vector2 LastCounterDirection { get; private set; }

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
            float targetSpeed = _targetCruiseSpeed * (IsBursting ? _settings.BurstSpeedMultiplier : 1);
            Speed = Approach(Speed, targetSpeed, _settings.Acceleration * dt);
            Position = Vector2.Clamp(Position + Direction(_heading) * Speed * dt, Vector2.Zero, bounds);
        }
        // Used only by the next frame's burst: never counter the player's current-frame input.
        _previousBoxVelocity = (boxPosition - _lastBoxPosition) / delta;
        _lastBoxPosition = boxPosition;
    }

    private void UpdateEvents(float dt)
    {
        if (_burstRemaining > 0) _burstRemaining = MathF.Max(0, _burstRemaining - dt);
        else if (_settings.BurstInterval > 0 && _settings.BurstDuration > 0)
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
            Vector2 intention = Direction(wander);
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

        // Curved, anticipatory edge steering. Clamping is only a containment safety net.
        float margin = MathF.Max(0.02f, _settings.EdgeAvoidanceDistance);
        Vector2 forward = Position + Direction(_heading) * Speed * 0.3f;
        Vector2 edge = new(
            MathF.Max(0, margin - forward.X) - MathF.Max(0, forward.X - (bounds.X - margin)),
            MathF.Max(0, margin - forward.Y) - MathF.Max(0, forward.Y - (bounds.Y - margin)));
        float desired = _targetHeading;
        bool avoidingEdge = edge.LengthSquared() > 0.000001f;
        if (avoidingEdge)
        {
            Vector2 intention = Direction(desired) + edge / margin * 3;
            desired = MathF.Atan2(intention.Y, intention.X);
        }
        float turnRate = IsBursting ? _settings.BurstTurnRate : _settings.MaxTurnRate;
        if (avoidingEdge) turnRate = MathF.Max(turnRate, 240); // Edge safety outranks burst commitment.
        float difference = MathF.IEEERemainder(desired - _heading, MathF.Tau);
        _heading = MathF.IEEERemainder(_heading + Math.Clamp(difference,
            -turnRate * DegreesToRadians * dt, turnRate * DegreesToRadians * dt), MathF.Tau);
    }

    private float NextDiveDelay() => _settings.DiveRate <= 0 ? float.PositiveInfinity
        : (float)(-Math.Log(1 - _diveRandom.NextDouble()) * 60 / _settings.DiveRate);
    private float Range(float min, float max) => min + (max - min) * (float)_random.NextDouble();
    private float Jitter(float value) => value * Range(0.7f, 1.3f);
    private static Vector2 Direction(float angle) => new(MathF.Cos(angle), MathF.Sin(angle));
    private static float Approach(float current, float target, float maximumDelta) =>
        current + Math.Clamp(target - current, -maximumDelta, maximumDelta);
}
