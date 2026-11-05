using System;
using System.Linq;
using System.Numerics;

internal static class Program
{
    private static int _checks;
    private static readonly Vector2 Bounds = new(2.3f, 0.9f);
    private static readonly Vector2 FishSize = new(0.09f);
    private static readonly Vector2 BoxSize = new(0.4f, 0.3f);

    private static void Require(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new Exception(message);
    }

    private static void Tick(FishMovementSimulation fish, float dt, Vector2 box) =>
        fish.Step(dt, Bounds, FishSize, box, BoxSize);

    public static void Main()
    {
        Require(FishingDataRepository.TryLoad(out var repository, out var errors), string.Join("\n", errors));
        foreach (var species in repository.Fish)
        {
            Require(repository.TryGetFightProfile(species.FightProfileId, out var profile), $"No movement profile: {species.Id}");
            Require(!profile.Movement.ValidationErrors().Any(), $"Invalid profile: {profile.Id}");
        }
        var setup = new FishingSetup { BaitId = "bait.chao_gen_chong", Depth = FishingDepth.Shallow };
        foreach (string eventId in new[] { "act1.tutorial.first_catch", "test.natural_catch" })
        {
            var request = repository.CreateEventRequest(eventId, setup, new Random(42));
            Require(repository.TryGetFish(request.BaseFishId, out var fish), "Request lost its fish");
            Require(request.FightProfileId == fish.FightProfileId, "Request lost its fight profile");
            repository.TryGetFightProfile(request.FightProfileId, out var profile);
            Require(request.FightMovement.CruiseSpeed == profile.Movement.CruiseSpeed, "Request lost settings");
            request.FightMovement.CruiseSpeed = 99;
            Require(profile.Movement.CruiseSpeed != 99, "Fight mutates shared species settings");
        }
        Console.WriteLine("PASS: real catalog validation and scripted/natural request profile propagation");

        foreach (var profile in repository.FightProfiles)
        foreach (float dt in new[] { 1f / 30, 1f / 60, 1f / 144 })
        for (int seed = 0; seed < 5; seed++)
        {
            var fish = new FishMovementSimulation(profile.Movement, seed, Bounds * 0.5f);
            for (int frame = 0; frame < 60 / dt; frame++)
            {
                float before = fish.Speed;
                Tick(fish, dt, new Vector2(1 + MathF.Sin(frame * dt), 0.3f));
                Require(float.IsFinite(fish.Position.X) && float.IsFinite(fish.Position.Y), "Non-finite position");
                Require(fish.Position.X >= 0 && fish.Position.X <= Bounds.X && fish.Position.Y >= 0 && fish.Position.Y <= Bounds.Y,
                    $"Fish escaped bounds: {profile.Id}");
                Require(MathF.Abs(fish.Speed - before) <= profile.Movement.Acceleration * dt + 0.00001f, "Acceleration limit exceeded");
            }
        }
        Console.WriteLine("PASS: four profiles stay bounded and respect acceleration at 30/60/144 FPS over 60 seconds");

        var settings = new FishMovementSettings { DiveRate = 60, DiveDuration = 0.4f };
        var a = new FishMovementSimulation(settings, 42, Bounds * 0.5f);
        var b = new FishMovementSimulation(settings, 42, Bounds * 0.5f);
        int hiddenFrames = 0, finishedDives = 0;
        float hiddenDistance = 0;
        for (int i = 0; i < 60 * 60; i++)
        {
            bool wasHidden = a.IsDiving;
            int beforeCount = a.DiveCount;
            Vector2 before = a.Position;
            Vector2 box = new(0.8f + MathF.Sin(i / 60f) * 0.4f, 0.3f);
            Tick(a, 1f / 60, box);
            Tick(b, 1f / 60, box);
            Require(a.Position == b.Position && a.IsDiving == b.IsDiving && a.IsBursting == b.IsBursting,
                "Seed replay diverged");
            if (wasHidden)
            {
                Require(a.DiveCount == beforeCount, "Overlapping dive event");
                hiddenDistance += Vector2.Distance(before, a.Position);
            }
            if (a.IsDiving) hiddenFrames++;
            else if (wasHidden)
            {
                Require(MathF.Abs(hiddenFrames / 60f - settings.DiveDuration) <= 2f / 60, "Dive duration mismatch");
                finishedDives++;
                hiddenFrames = 0;
            }
        }
        Require(finishedDives > 5 && hiddenDistance > 1, "Repeated dives stopped movement");
        Console.WriteLine("PASS: deterministic replay, repeated non-overlapping dives, hidden duration and continued movement");

        var visibleSettings = settings.Copy();
        visibleSettings.DiveRate = 0;
        var visible = new FishMovementSimulation(visibleSettings, 22, Bounds * 0.5f);
        var diving = new FishMovementSimulation(settings, 22, Bounds * 0.5f);
        for (int i = 0; i < 3600; i++)
        {
            Tick(visible, 1f / 60, Vector2.Zero);
            Tick(diving, 1f / 60, Vector2.Zero);
            Require(visible.Position == diving.Position && visible.Speed == diving.Speed,
                "Dive event changed the movement trajectory");
        }
        Require(diving.DiveCount > 5, "Dive independence check never dived");

        float escapeBias = 0;
        for (int seed = 0; seed < 100; seed++)
        {
            var avoidSettings = new FishMovementSettings { AvoidanceStrength = 1, DiveRate = 0,
                BurstInterval = 0, ReactionDelay = 0, MaxTurnRate = 180 };
            var neutralSettings = avoidSettings.Copy();
            neutralSettings.AvoidanceStrength = 0;
            var avoid = new FishMovementSimulation(avoidSettings, seed, new Vector2(1.15f, 0.4f));
            var neutral = new FishMovementSimulation(neutralSettings, seed, new Vector2(1.15f, 0.4f));
            for (int i = 0; i < 60; i++)
            {
                Tick(avoid, 1f / 60, new Vector2(0.8f, 0.3f));
                Tick(neutral, 1f / 60, new Vector2(0.8f, 0.3f));
            }
            escapeBias += avoid.Position.X - neutral.Position.X;
        }
        Require(escapeBias > 2, "Avoidance did not favour the nearby box exit");
        Console.WriteLine("PASS: dive events do not alter trajectories; box avoidance biases toward a nearby exit");

        var disabled = new FishMovementSimulation(new FishMovementSettings { DiveRate = 0, BurstInterval = 0 }, 1, Bounds * 0.5f);
        for (int i = 0; i < 3600; i++) Tick(disabled, 1f / 60, Vector2.Zero);
        Require(disabled.DiveCount == 0 && disabled.BurstCount == 0, "Disabled events triggered");
        var noDuration = new FishMovementSimulation(new FishMovementSettings { DiveDuration = 0 }, 1, Bounds * 0.5f);
        for (int i = 0; i < 3600; i++) Tick(noDuration, 1f / 60, Vector2.Zero);
        Require(noDuration.DiveCount == 0, "Zero-duration dive triggered");

        var counter = new FishMovementSimulation(new FishMovementSettings {
            BurstInterval = 0.04f, BurstDuration = 0.02f, BurstCounterTurnChance = 1, DiveRate = 0
        }, 7, Bounds * 0.5f);
        Vector2 lastBox = new(0.8f, 0.3f), previousVelocity = Vector2.Zero;
        for (int i = 0; i < 500; i++)
        {
            Vector2 box = lastBox + new Vector2(i % 2 == 0 ? 0.005f : -0.005f, 0);
            int count = counter.CounterTurnCount;
            Tick(counter, 0.01f, box);
            if (counter.CounterTurnCount != count)
                Require(Vector2.Dot(counter.LastCounterDirection, Vector2.Normalize(previousVelocity)) < -0.95f,
                    "Counter-turn did not oppose previous-frame box motion");
            previousVelocity = (box - lastBox) / 0.01f;
            lastBox = box;
        }
        Require(counter.CounterTurnCount > 10, "Counter turns never triggered");
        var stationary = new FishMovementSimulation(new FishMovementSettings {
            BurstInterval = 0.04f, BurstDuration = 0.02f, BurstCounterTurnChance = 1
        }, 7, Bounds * 0.5f);
        for (int i = 0; i < 500; i++) Tick(stationary, 0.01f, Vector2.Zero);
        Require(stationary.CounterTurnCount == 0, "Stationary box supplied a counter-turn direction");
        Console.WriteLine("PASS: event disabling and counter-turns use previous-frame motion, with stationary fallback");
        Require(new FishMovementSettings { DiveRate = float.NaN }.ValidationErrors().Any(), "NaN accepted");
        Require(new FishMovementSettings { Acceleration = 0 }.ValidationErrors().Any(), "Zero acceleration accepted");
        Console.WriteLine($"All {_checks:N0} assertions passed.");
    }
}
