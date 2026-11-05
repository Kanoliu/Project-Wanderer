using System;
using System.Collections.Generic;

/// <summary>Distances use the panel's shorter dimension; angles are degrees, time is seconds.</summary>
public sealed class FishMovementSettings
{
    public float CruiseSpeed { get; set; } = 0.40f;
    public float SpeedVariation { get; set; } = 0.15f;
    public float Acceleration { get; set; } = 0.85f;
    public float MaxTurnRate { get; set; } = 120;
    public float SteeringInterval { get; set; } = 0.9f;
    public float WanderAngle { get; set; } = 40;
    public float EdgeAvoidanceDistance { get; set; } = 0.12f;
    public float BurstSpeedMultiplier { get; set; } = 1.8f;
    public float BurstDuration { get; set; } = 0.65f;
    public float BurstInterval { get; set; } = 4.5f;
    public float BurstTurnRate { get; set; } = 90;
    public float DiveRate { get; set; } = 3;
    public float DiveDuration { get; set; } = 2.5f;
    public float AvoidanceStrength { get; set; } = 0.35f;
    public float ReactionDelay { get; set; } = 0.15f;
    public float BurstCounterTurnChance { get; set; } = 0.5f;

    public FishMovementSettings Copy() => (FishMovementSettings)MemberwiseClone();

    public IEnumerable<string> ValidationErrors()
    {
        foreach (var property in typeof(FishMovementSettings).GetProperties())
        {
            float value = (float)property.GetValue(this);
            if (!float.IsFinite(value) || value < 0)
                yield return $"{property.Name} must be finite and non-negative.";
        }
        if (CruiseSpeed <= 0 || Acceleration <= 0 || SteeringInterval <= 0)
            yield return "CruiseSpeed, Acceleration and SteeringInterval must be positive.";
        if (SpeedVariation > 1 || AvoidanceStrength > 1 || BurstCounterTurnChance > 1)
            yield return "SpeedVariation, AvoidanceStrength and BurstCounterTurnChance must be in [0, 1].";
        if (BurstSpeedMultiplier < 1)
            yield return "BurstSpeedMultiplier must be at least 1.";
        if (EdgeAvoidanceDistance > 0.5f || WanderAngle > 180)
            yield return "EdgeAvoidanceDistance must be <= 0.5 and WanderAngle <= 180.";
        if (ReactionDelay > 5)
            yield return "ReactionDelay must be <= 5 seconds.";
    }
}
