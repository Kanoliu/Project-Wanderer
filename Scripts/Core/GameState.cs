using System;
using System.Collections.Generic;
using System.Linq;

public class GameState
{
	public int SaveVersion { get; set; } = 1;
	public int CurrentDay { get; set; }
	public DayPhase CurrentPhase { get; set; } = DayPhase.Prologue;
	public FishingSetup CurrentFishingSetup { get; set; } = new();
	public Dictionary<string, int> Relationships { get; set; } = new();
	public Dictionary<string, int> Inventory { get; set; } = new();
	public HashSet<string> Flags { get; set; } = new();
	public List<FishingResult> FishingHistory { get; set; } = new();
	public Dictionary<int, string> DinnerFishByDay { get; set; } = new();

	public void InitializeRelationships(IReadOnlyDictionary<string, int> defaults)
	{
		Relationships ??= new Dictionary<string, int>(StringComparer.Ordinal);
		foreach ((string characterId, int value) in defaults)
		{
			Relationships.TryAdd(characterId, value);
		}
	}

	public void BeginDay(int day, FishingSetup setup)
	{
		if (day < 1)
		{
			throw new ArgumentOutOfRangeException(
				nameof(day),
				"Day numbers start at 1."
			);
		}

		CurrentDay = day;
		CurrentFishingSetup = setup ?? new FishingSetup();
		CurrentPhase = DayPhase.Preparation;
	}

	public void EnterDialogueFishing()
	{
		CurrentPhase = DayPhase.DialogueFishing;
	}

	public void EnterSettlement()
	{
		CurrentPhase = DayPhase.Settlement;
	}

	public void CompleteDay()
	{
		CurrentPhase = DayPhase.Complete;
	}

	public void ApplyFishingResult(FishingResult result)
	{
		if (result == null)
		{
			throw new ArgumentNullException(nameof(result));
		}

		if (string.IsNullOrWhiteSpace(result.EventId))
		{
			throw new ArgumentException(
				"Fishing results require a stable event ID.",
				nameof(result)
			);
		}

		FishingHistory.Add(result.ForDay(CurrentDay));

		if (!result.Caught ||
			result.Quantity <= 0 ||
			string.IsNullOrWhiteSpace(result.FishId))
		{
			return;
		}

		Inventory.TryGetValue(result.FishId, out int currentQuantity);
		Inventory[result.FishId] = currentQuantity + result.Quantity;
	}

	public IReadOnlyDictionary<string, int> GetDailyCatch()
	{
		return FishingHistory
			.Where(result => result.Day == CurrentDay && result.Caught &&
				result.Quantity > 0 && !string.IsNullOrWhiteSpace(result.FishId))
			.GroupBy(result => result.FishId, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.Sum(result => result.Quantity),
				StringComparer.Ordinal);
	}

	public IReadOnlyList<FishDefinition> GetDinnerOptions(IFishingDataRepository fishingData)
	{
		if (CurrentDay < 1 || DinnerFishByDay?.ContainsKey(CurrentDay) == true)
		{
			return Array.Empty<FishDefinition>();
		}

		HashSet<string> caughtFishIds = new(StringComparer.Ordinal);
		foreach (FishingResult result in FishingHistory)
		{
			if (result.Day == CurrentDay && result.Caught && result.Quantity > 0)
			{
				caughtFishIds.Add(result.FishId);
			}
		}

		return fishingData.Fish
			.Where(fish => fish.DinnerRecipe != null &&
				(caughtFishIds.Contains(fish.Id) ||
				fish.Variants?.Any(variant => caughtFishIds.Contains(variant.Id)) == true))
			.ToArray();
	}

	public bool TryServeDinner(IFishingDataRepository fishingData, string baseFishId)
	{
		if (CurrentPhase != DayPhase.Settlement)
		{
			return false;
		}

		FishDefinition fish = GetDinnerOptions(fishingData)
			.FirstOrDefault(option => option.Id == baseFishId);
		if (fish == null)
		{
			return false;
		}

		DinnerFishByDay ??= new Dictionary<int, string>();
		DinnerFishByDay.Add(CurrentDay, fish.Id);
		foreach (string characterId in fish.DinnerRecipe.FavoriteCharacterIds)
		{
			AdjustRelationship(characterId, 2);
		}
		foreach (string characterId in fish.DinnerRecipe.LikedCharacterIds)
		{
			AdjustRelationship(characterId, 1);
		}
		return true;
	}

	public void ApplyDialogueEffect(DialogueEffect effect)
	{
		if (effect == null || effect.Type == DialogueEffectType.None)
		{
			return;
		}

		if (string.IsNullOrWhiteSpace(effect.TargetId))
		{
			throw new ArgumentException(
				"Dialogue effects require a stable target ID.",
				nameof(effect)
			);
		}

		switch (effect.Type)
		{
			case DialogueEffectType.Relationship:
				AdjustRelationship(effect.TargetId, effect.Amount);
				break;

			case DialogueEffectType.SetFlag:
				Flags.Add(effect.TargetId);
				break;

			case DialogueEffectType.ClearFlag:
				Flags.Remove(effect.TargetId);
				break;
		}
	}

	private void AdjustRelationship(string characterId, int amount)
	{
		Relationships ??= new Dictionary<string, int>(StringComparer.Ordinal);
		Relationships[characterId] = Relationships.GetValueOrDefault(characterId) + amount;
	}
}
