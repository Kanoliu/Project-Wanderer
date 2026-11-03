using System;
using System.Collections.Generic;

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

		FishingHistory.Add(result);

		if (!result.Caught ||
			result.Quantity <= 0 ||
			string.IsNullOrWhiteSpace(result.FishId))
		{
			return;
		}

		Inventory.TryGetValue(result.FishId, out int currentQuantity);
		Inventory[result.FishId] = currentQuantity + result.Quantity;
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
				Relationships.TryGetValue(
					effect.TargetId,
					out int currentRelationship
				);
				Relationships[effect.TargetId] =
					currentRelationship + effect.Amount;
				break;

			case DialogueEffectType.SetFlag:
				Flags.Add(effect.TargetId);
				break;

			case DialogueEffectType.ClearFlag:
				Flags.Remove(effect.TargetId);
				break;
		}
	}
}
