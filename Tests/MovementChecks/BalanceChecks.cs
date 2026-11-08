using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

/// <summary>Diagnostic bot, not a human difficulty rating. Matches the shared HUD geometry and key input.</summary>
internal static class BalanceChecks
{
    public static void Run(FishingDataRepository repository, string output)
    {
        const float dt = 1f / 60, scale = 312.5f;
        Vector2 area = new(750 / scale, 1), fishSize = new(24 / scale), boxSize = new(110 / scale, 76 / scale);
        var rows = new List<object>();
        foreach (var species in repository.Fish)
        foreach (bool isSpecialVariant in new[] { false, true })
        {
            var profile = repository.CreateFightProfile(species.Id, isSpecialVariant);
            int caught = 0, escaped = 0, timedOut = 0, idleCaught = 0;
            float totalTime = 0;
            for (int seed = 0; seed < 32; seed++)
            foreach (bool idle in new[] { false, true })
            {
                Vector2 box = (area - boxSize) / 2;
                var fish = new FishMovementSimulation(profile.Movement, seed, (area - fishSize) / 2);
                var observations = new Queue<Vector2>();
                Vector2 target = fish.Position + fishSize / 2;
                float progress = .35f, elapsed = 0;
                for (int frame = 0; frame < 60 * 45 && progress > 0 && progress < 1; frame++)
                {
                    if (!fish.IsDiving) target = fish.Position + fishSize / 2;
                    observations.Enqueue(target);
                    Vector2 observed = observations.Count > 9 ? observations.Dequeue() : (area / 2);
                    Vector2 difference = observed - (box + boxSize / 2);
                    // Eight directions with a small dead zone, 150ms observation lag; no hidden-position oracle.
                    Vector2 direction = new(MathF.Abs(difference.X) > .018f ? MathF.Sign(difference.X) : 0,
                        MathF.Abs(difference.Y) > .018f ? MathF.Sign(difference.Y) : 0);
                    if (!idle && direction != Vector2.Zero)
                        box = Vector2.Clamp(box + Vector2.Normalize(direction) * (220 / scale) * dt,
                            Vector2.Zero, area - boxSize);
                    fish.Step(dt, area - fishSize, fishSize, box, boxSize);
                    Vector2 center = fish.Position + fishSize / 2;
                    bool inside = center.X >= box.X && center.X < box.X + boxSize.X &&
                                  center.Y >= box.Y && center.Y < box.Y + boxSize.Y;
                    progress += (inside ? profile.GainRate : -profile.LossRate) * dt;
                    elapsed += dt;
                }
                if (idle) { if (progress >= 1) idleCaught++; continue; }
                totalTime += elapsed;
                if (progress >= 1) caught++;
                else if (progress <= 0) escaped++;
                else timedOut++;
            }
            rows.Add(new { profile.Id, fishId = species.Id, isSpecialVariant, profile.LossDifficulty,
                lossRate = profile.LossRate, caught, escaped, timedOut, idleCaught,
                meanSeconds = MathF.Round(totalTime / 32, 2),
                perfectSeconds = MathF.Round(.65f / profile.GainRate, 2),
                requiredTrackingFraction = MathF.Round(profile.LossRate / (profile.GainRate + profile.LossRate), 3) });
            Console.WriteLine($"BALANCE {species.DisplayName}{(isSpecialVariant ? " special" : "")}: tracked {caught}/32, idle {idleCaught}/32, mean {totalTime / 32:0.0}s, timeout {timedOut}");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            method = "32 seeds; 60 FPS; 750x312.5 water; 110x76 box; 220px/s; 150ms delay; last visible target during dives; 45s cap. Diagnostic only, not human win rates.",
            profiles = rows
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
