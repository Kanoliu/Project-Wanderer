using System;
using System.Linq;
using System.Numerics;
using System.IO;
using System.Text.Json.Nodes;

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

    public static void Main(string[] args)
    {
        Require(FishingDataRepository.TryLoad(out var repository, out var errors), string.Join("\n", errors));
        CheckLossTierLoading();
        CheckGlobalGainLoading();
        foreach (var species in repository.Fish)
        {
            Require(repository.TryGetFightProfile(species.FightProfileId, out var profile), $"No movement profile: {species.Id}");
            Require(!profile.Movement.ValidationErrors().Any(), $"Invalid profile: {profile.Id}");
        }
        Require(repository.FightProfiles.Count == repository.Fish.Count, "Only species should own fight configurations");
        Require(repository.SpecialVariantMultiplier == 1.15f, "Unified special strength changed");
        Require(!repository.TryGetFightProfile("fight.active", out _) &&
            !repository.TryGetFightProfile("fight.agile", out _) &&
            !repository.TryGetFightProfile("fight.sprint", out _), "Removed test tiers still exist");
        var baseline = new FishMovementSettings();
        Require(baseline.CruiseSpeed == .44f && baseline.Acceleration == 1f, "Original active movement floor changed");
        var seenFish = new System.Collections.Generic.HashSet<string>();
        foreach (var bait in repository.Baits)
        foreach (var depth in repository.Depths)
        for (int seed = 0; seed < 500; seed++)
        {
            var request = repository.CreateEventRequest("test.all_fish",
                new FishingSetup { BaitId = bait.Id, Depth = depth.Value }, new Random(seed));
            if (string.IsNullOrEmpty(request.BaseFishId)) continue;
            seenFish.Add(request.FishId);
            repository.TryGetFish(request.BaseFishId, out var species);
            string expected = species.FightProfileId;
            if (request.IsSpecialVariant)
            {
                Require(repository.TryGetVariant(request.FishId, out _), "Variant lost");
            }
            Require(request.FightProfileId == expected, "Variant/species profile propagation failed");
            repository.TryGetFightProfile(expected, out var profile);
            float strength = request.IsSpecialVariant ? repository.SpecialVariantMultiplier : 1f;
            Require(request.FightGainRate == repository.ProgressGainRate / repository.ProgressCapacityMultiplier &&
                request.FightLossRate == repository.GetBaseProgressLossRate(profile.LossDifficulty.Value) * strength / repository.ProgressCapacityMultiplier,
                "Encounter lost species gain/loss rates");
            Require(repository.GetBaseProgressLossRate(profile.LossDifficulty.Value) >= .08f,
                "Loss tier is easier than the original active floor");
            Require(request.FightMovement.CruiseSpeed == profile.Movement.CruiseSpeed * strength &&
                request.FightMovement.MaxTurnRate == profile.Movement.MaxTurnRate * strength &&
                request.FightMovement.Acceleration == profile.Movement.Acceleration * strength &&
                request.FightMovement.BurstTurnRate == profile.Movement.BurstTurnRate * strength,
                "Special form did not use the unified movement multiplier");
            Require(request.FightMovement.BurstDuration == profile.Movement.BurstDuration &&
                request.FightMovement.DiveRate == profile.Movement.DiveRate &&
                request.FightMovement.AvoidanceStrength == profile.Movement.AvoidanceStrength &&
                request.FightMovement.BurstCounterTurnChance == profile.Movement.BurstCounterTurnChance,
                "Special form changed its species behaviour");
            Require(repository.TryGetBiteProfile(species.BiteProfileId, out var bite) &&
                request.BitePattern == bite.Pattern, "Species/variant lost its bite pattern");
            request.FightMovement.CruiseSpeed = 99;
            Require(profile.Movement.CruiseSpeed != 99, "Variant request mutated shared settings");
        }
        Require(seenFish.Count == repository.Fish.Sum(f => 1 + f.Variants.Length), "Not every species/variant was exercised");
        Console.WriteLine($"PASS: {seenFish.Count} fish forms propagate movement and progress rates through natural requests");
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

        var movementProfiles = repository.Fish.SelectMany(species => new[] {
            repository.CreateFightProfile(species.Id), repository.CreateFightProfile(species.Id, true)
        }).ToArray();
        foreach (var profile in movementProfiles)
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
        Console.WriteLine($"PASS: {movementProfiles.Length} derived forms stay bounded and respect acceleration at 30/60/144 FPS over 60 seconds");

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
        MovementFeelChecks.Run(repository, Require, args.Length == 2
            ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[1]), "movement-feel.json") : null);
        Console.WriteLine($"All {_checks:N0} assertions passed.");
        if (args.Length == 2 && args[0] == "--balance-report") BalanceChecks.Run(repository, args[1]);
    }

    private static void CheckLossTierLoading()
    {
        // Only alter the copied test fixture under bin/, never the project's authoritative JSON.
        string path = Path.Combine(AppContext.BaseDirectory, "Data/Fishing/profiles.json");
        string original = File.ReadAllText(path);
        try
        {
            foreach (string failure in new[] { "missing", "duplicate", "reversed", "missing_assignment" })
            {
                var data = JsonNode.Parse(original);
                var tiers = data["progressLossTiers"].AsArray();
                if (failure == "missing") tiers.RemoveAt(2);
                if (failure == "duplicate") tiers.Add(tiers[0].DeepClone());
                if (failure == "reversed") tiers[2]["lossRate"] = .01f;
                if (failure == "missing_assignment") data["fightProfiles"][0].AsObject().Remove("lossDifficulty");
                File.WriteAllText(path, data.ToJsonString());
                Require(!FishingDataRepository.TryLoad(out _, out _), $"Invalid loss-tier configuration accepted: {failure}");
            }
            var overrideData = JsonNode.Parse(original);
            overrideData["fightProfiles"][0]["lossDifficulty"] = "High";
            File.WriteAllText(path, overrideData.ToJsonString());
            Require(FishingDataRepository.TryLoad(out var overrideRepository, out var errors), string.Join("\n", errors));
            Require(overrideRepository.TryGetFish("fish.qing_lu", out var fish) && fish.CatchDifficulty == FishCatchDifficulty.Low,
                "Changing loss tier changed encounter rarity");
            Require(overrideRepository.CreateFightProfile(fish.Id).LossRate ==
                overrideRepository.GetBaseProgressLossRate(FishCatchDifficulty.High) / overrideRepository.ProgressCapacityMultiplier,
                "Per-species loss-tier override did not reach the fight");
        }
        finally { File.WriteAllText(path, original); }
        Console.WriteLine("PASS: missing/duplicate/reversed loss tiers rejected; per-species loss tier is independent of encounter rarity");
    }

    private static void CheckGlobalGainLoading()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Data/Fishing/profiles.json");
        string original = File.ReadAllText(path);
        try
        {
            foreach (float? invalid in new float?[] { null, 0, -0.1f, 1.1f })
            {
                var data = JsonNode.Parse(original);
                if (invalid.HasValue) data["progressGainRate"] = invalid.Value;
                else data.AsObject().Remove("progressGainRate");
                File.WriteAllText(path, data.ToJsonString());
                Require(!FishingDataRepository.TryLoad(out _, out _), "Missing/invalid global gain accepted");
            }
            var changed = JsonNode.Parse(original);
            changed["progressGainRate"] = .2f;
            File.WriteAllText(path, changed.ToJsonString());
            Require(FishingDataRepository.TryLoad(out var repository, out var errors), string.Join("\n", errors));
            foreach (var species in repository.Fish)
            {
                var standard = repository.CreateFightProfile(species.Id);
                var special = repository.CreateFightProfile(species.Id, true);
                Require(standard.GainRate == .2f / repository.ProgressCapacityMultiplier && special.GainRate == standard.GainRate,
                    "Shared gain did not reach every species and special form");
                Require(special.LossRate > standard.LossRate, "Uniform gain removed the special-form loss modifier");
            }
            Require(repository.CreateEventRequest("act1.tutorial.first_catch").FightGainRate == .2f / repository.ProgressCapacityMultiplier,
                "Scripted encounter lost the global gain");
            Require(repository.CreateEventRequest("test.empty").FightGainRate == .2f / repository.ProgressCapacityMultiplier,
                "Fallback encounter lost the global gain");
        }
        finally { File.WriteAllText(path, original); }
        Console.WriteLine("PASS: one global gain applies to every species, special form, scripted and fallback encounter");
    }
}
