using System;
using System.Collections.Generic;

/// <summary>Distances use the panel's shorter dimension; angles are degrees, time is seconds.</summary>
public sealed class FishMovementSettings
{
    public float CruiseSpeed { get; set; } = 0.44f;
    public float SpeedVariation { get; set; } = 0.18f;
    public float Acceleration { get; set; } = 1.0f;
    public float MaxTurnRate { get; set; } = 155;
    public float SteeringInterval { get; set; } = 0.7f;
    public float WanderAngle { get; set; } = 65;
    public float EdgeAvoidanceDistance { get; set; } = 0.14f;
    public float BurstSpeedMultiplier { get; set; } = 1.8f;
    public float BurstDuration { get; set; } = 0.55f;
    public float BurstInterval { get; set; } = 3.5f;
    public float BurstTurnRate { get; set; } = 160;
    public float DiveRate { get; set; } = 3;
    public float DiveDuration { get; set; } = 2.5f;
    public float DiveMinDelay { get; set; } = 3f;
    public float HorizontalBias { get; set; }
    public float ReversalChance { get; set; }
    public float PauseDuration { get; set; }
    public float PauseInterval { get; set; }
    public float AvoidanceStrength { get; set; } = 0.35f;
    public float ReactionDelay { get; set; } = 0.16f;
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
        if (SpeedVariation > 1 || AvoidanceStrength > 1 || BurstCounterTurnChance > 1 ||
            HorizontalBias > 1 || ReversalChance > 1)
            yield return "Variation, avoidance, horizontal bias and probabilities must be in [0, 1].";
        if (BurstSpeedMultiplier < 1)
            yield return "BurstSpeedMultiplier must be at least 1.";
        if (EdgeAvoidanceDistance > 0.5f || WanderAngle > 180)
            yield return "EdgeAvoidanceDistance must be <= 0.5 and WanderAngle <= 180.";
        if (ReactionDelay > 5)
            yield return "ReactionDelay must be <= 5 seconds.";
    }
}
