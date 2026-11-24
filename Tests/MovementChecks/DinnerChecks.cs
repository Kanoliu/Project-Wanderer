using System;
using System.Linq;
using System.Text.Json;

internal static class DinnerChecks
{
    public static void Run(IFishingDataRepository fishingData, Action<bool, string> require)
    {
        using var characters = JsonDocument.Parse(Godot.FileAccess.GetFileAsString("res://Data/Characters/characters.json"));
        var crewIds = characters.RootElement.EnumerateArray()
            .Where(character => character.TryGetProperty("initialRelationship", out _))
            .Select(character => character.GetProperty("id").GetString())
            .Where(id => id != "character.qiaqia")
            .ToHashSet();
        var recipes = fishingData.Fish.Select(fish => fish.DinnerRecipe).ToArray();
        require(recipes.Length == 14 && recipes.All(recipe => recipe != null), "All 14 base fish must have recipes");
        var recipientIds = recipes.SelectMany(recipe => recipe.FavoriteCharacterIds.Concat(recipe.LikedCharacterIds));
        require(crewIds.Count == 6 && crewIds.SetEquals(recipientIds), "Dinner preferences must reference the six participating crew members");

        var state = new GameState();
        state.ApplyFishingResult(FishingResult.CreateCatch("prologue", "fish.mang_man"));
        state.BeginDay(1, new FishingSetup());
        state.ApplyFishingResult(FishingResult.CreateCatch("variant", "fish.da_dou_diao", 3));
        state.ApplyFishingResult(FishingResult.CreateCatch("same_recipe", "fish.dou_diao"));
        state.ApplyFishingResult(FishingResult.CreateCatch("other_recipe", "fish.qing_lu"));
        state.ApplyFishingResult(new FishingResult
        {
            EventId = "escaped",
            FishId = "fish.dian_you",
            Quantity = 1,
            Outcome = FishingOutcome.Escaped
        });
        state.ApplyFishingResult(FishingResult.CreateCatch("no_quantity", "fish.dian_you", 0));
        var options = state.GetDinnerOptions(fishingData);
        var optionIds = options.Select(fish => fish.Id).ToHashSet();
        require(options.Count == 2 && optionIds.SetEquals(new[] { "fish.dou_diao", "fish.qing_lu" }),
            "Menu must filter by day and catch outcome, grouping variants and duplicates");
        require(!state.TryServeDinner(fishingData, "fish.dou_diao"), "Dinner must be served during settlement");

        state.ApplyDialogueEffect(new DialogueEffect
        {
            Type = DialogueEffectType.Relationship,
            TargetId = "character.wuzuona",
            Amount = 3
        });
        state.EnterSettlement();
        require(!state.TryServeDinner(fishingData, "fish.mang_man") && state.DinnerFishByDay.Count == 0,
            "An unavailable choice must leave settlement open");
        require(state.TryServeDinner(fishingData, "fish.dou_diao"), "A caught dinner must be selectable");
        require(state.Relationships.Count == 2 && state.Relationships["character.wuzuona"] == 5 &&
            state.Relationships["character.wensheng"] == 1,
            "Dinner must add +2/+1 once to the same scores used by dialogue");

        state = JsonSerializer.Deserialize<GameState>(JsonSerializer.Serialize(state));
        require(state.DinnerFishByDay[1] == "fish.dou_diao" && state.GetDinnerOptions(fishingData).Count == 0 &&
            !state.TryServeDinner(fishingData, "fish.qing_lu"), "Reloading must not allow another dish that day");
        state.BeginDay(2, new FishingSetup());
        state.EnterSettlement();
        require(state.GetDinnerOptions(fishingData).Count == 0, "Earlier catches must not roll into the new day's menu");
        state.ApplyFishingResult(FishingResult.CreateCatch("variant_only", "fish.da_dou_diao"));
        require(state.TryServeDinner(fishingData, "fish.dou_diao") && state.Relationships["character.wuzuona"] == 7 &&
            state.Relationships["character.wensheng"] == 2, "A variant alone must unlock its base recipe on a new day");

        var legacyState = JsonSerializer.Deserialize<GameState>("""
            {"CurrentDay": 1, "FishingHistory": [{"EventId": "legacy", "FishId": "fish.dou_diao", "Quantity": 1, "Outcome": 1}]}
            """);
        require(legacyState.GetDinnerOptions(fishingData).Count == 0, "Legacy catches without a day must not be credited to today's dinner");
        Console.WriteLine("PASS: 14 dinner recipes, today's catches and variants, and once-per-day relationship scoring across save/load");
    }
}
