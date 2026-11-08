using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

internal static class MovementFeelChecks
{
    public static void Run(FishingDataRepository repository, Action<bool, string> require, string output)
    {
        var rows = new List<object>();
        Vector2 size = new(750 / 312.5f, 1), fishSize = new(24 / 312.5f), boxSize = new(110 / 312.5f, 76 / 312.5f);
        foreach (var species in repository.Fish)
        foreach (bool special in new[] { false, true })
        {
            var profile = repository.CreateFightProfile(species.Id, special);
            var delays = new List<float>();
            int corrections = 0, resting = 0, bursts = 0, frames = 0;
            float dx = 0, dy = 0, speed = 0, speedSquared = 0, peakSpeed = 0;
            for (int seed = 0; seed < 32; seed++)
            {
                var fish = new FishMovementSimulation(profile.Movement, seed, (size - fishSize) / 2);
                delays.Add(fish.FirstDiveDelay);
                require(fish.FirstDiveDelay >= profile.Movement.DiveMinDelay, "First dive ignored minimum visible time");
                for (int frame = 0; frame < 60 * 60; frame++)
                {
                    var previous = fish.Position;
                    var box = new Vector2(.9f + MathF.Sin(frame / 90f) * .6f, .3f);
                    fish.Step(1f / 60, size - fishSize, fishSize, box, boxSize);
                    dx += MathF.Abs(fish.Position.X - previous.X);
                    dy += MathF.Abs(fish.Position.Y - previous.Y);
                    speed += fish.Speed;
                    speedSquared += fish.Speed * fish.Speed;
                    peakSpeed = MathF.Max(peakSpeed, fish.Speed);
                    frames++;
                    if (fish.IsResting) resting++;
                }
                corrections += fish.BoundaryCorrectionCount;
                bursts += fish.BurstCount;
            }
            delays.Sort();
            float correctionRate = corrections / (frames * 2f);
            require(correctionRate < .001f, $"Frequent hard wall correction: {species.Id} special={special}, rate={correctionRate}");
            require(delays[28] - delays[3] > 5, $"First dive times are clustered: {species.Id}");
            require(peakSpeed <= .96001f, $"Fish exceeds the 300px/s pursuit budget: {species.Id} special={special}");
            require(profile.Movement.PauseInterval <= 0 ? resting == 0 : resting / (float)frames > .05f,
                $"Species stop/swim rhythm is not observable: {species.Id}");
            if (species.Id == "fish.xia_fan_kuo") require(bursts == 0, "Slow drifting species started sprinting");
            if (species.Id == "fish.men_shen_xie" || species.Id == "fish.fu_zi_yu")
                require(dx / (dx + dy) > .6f, $"Horizontal species lost its sideward motion: {species.Id}");
            float meanSpeed = speed / frames;
            rows.Add(new {
                species.Id, special, corrections, correctionRate,
                firstDiveP10 = delays[3], firstDiveMedian = delays[16], firstDiveP90 = delays[28],
                horizontalTravelFraction = dx / (dx + dy), restingFraction = resting / (float)frames,
                burstsPerMinute = bursts / 32f, meanSpeed, peakSpeedPixels = peakSpeed * 312.5f,
                speedVariation = MathF.Sqrt(MathF.Max(0, speedSquared / frames - meanSpeed * meanSpeed)) / meanSpeed
            });
            Console.WriteLine($"FEEL {species.DisplayName}{(special ? " special" : "")}: wall={corrections}, dive P10/P90={delays[3]:0.0}/{delays[28]:0.0}s, horizontal={dx / (dx + dy):P0}, resting={resting / (float)frames:P0}, bursts/min={bursts / 32f:0.0}");
        }
        if (output != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, JsonSerializer.Serialize(new {
                method = "Each species and derived special form: 32 seeds, 60 seconds, 60 FPS, actual HUD geometry. Wall corrections count integration substeps (120 Hz).", rows
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
