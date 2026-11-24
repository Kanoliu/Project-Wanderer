using System.Collections.Generic;

public interface IFishingDataRepository
{
	IReadOnlyCollection<FishDefinition> Fish { get; }
	IReadOnlyCollection<FishingBaitDefinition> Baits { get; }
	IReadOnlyCollection<FishingDepthDefinition> Depths { get; }
	IReadOnlyCollection<BiteProfileDefinition> BiteProfiles { get; }
	IReadOnlyCollection<FightProfileDefinition> FightProfiles { get; }
	IReadOnlyCollection<FishingEventDefinition> ScriptedEvents { get; }
	float SpecialVariantMultiplier { get; }
	float ProgressCapacityMultiplier { get; }
	float ProgressGainRate { get; }
	FightProfileDefinition CreateFightProfile(string baseFishId, bool isSpecialVariant = false);

	bool TryGetFish(string id, out FishDefinition fish);
	bool TryGetVariant(string id, out FishVariantDefinition variant);
	bool TryGetBait(string id, out FishingBaitDefinition bait);
	IReadOnlyList<FishingCatchCandidate> GetCatchCandidates(FishingSetup setup);
	bool TryGetBiteProfile(string id, out BiteProfileDefinition profile);
	bool TryGetFightProfile(string id, out FightProfileDefinition profile);
	bool TryGetScriptedEvent(string id, out FishingEventDefinition fishingEvent);
	FishingEventRequest CreateEventRequest(string eventId);
	FishingEventRequest CreateEventRequest(string eventId, FishingSetup setup, int day = 0);
}
