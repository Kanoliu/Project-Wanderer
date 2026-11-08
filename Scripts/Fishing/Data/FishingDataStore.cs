using Godot;
using System.Collections.Generic;

public partial class FishingDataStore : Node, IFishingDataRepository
{
	private FishingDataRepository _repository =
		FishingDataRepository.Empty;

	public bool IsLoaded { get; private set; }
	public float SpecialVariantMultiplier => _repository.SpecialVariantMultiplier;
	public float ProgressCapacityMultiplier => _repository.ProgressCapacityMultiplier;
	public float ProgressGainRate => _repository.ProgressGainRate;
	public FightProfileDefinition CreateFightProfile(string baseFishId, bool isSpecialVariant = false) =>
		_repository.CreateFightProfile(baseFishId, isSpecialVariant);
	public IReadOnlyCollection<FishDefinition> Fish => _repository.Fish;
	public IReadOnlyCollection<FishingBaitDefinition> Baits => _repository.Baits;
	public IReadOnlyCollection<FishingDepthDefinition> Depths => _repository.Depths;
	public IReadOnlyCollection<BiteProfileDefinition> BiteProfiles =>
		_repository.BiteProfiles;
	public IReadOnlyCollection<FightProfileDefinition> FightProfiles =>
		_repository.FightProfiles;
	public IReadOnlyCollection<FishingEventDefinition> ScriptedEvents =>
		_repository.ScriptedEvents;

	public override void _Ready()
	{
		IsLoaded = FishingDataRepository.TryLoad(
			out _repository,
			out string[] errors
		);

		foreach (string error in errors)
		{
			GD.PushError(error);
		}
	}

	public bool TryGetFish(string id, out FishDefinition fish)
	{
		return _repository.TryGetFish(id, out fish);
	}

	public bool TryGetVariant(
		string id,
		out FishVariantDefinition variant)
	{
		return _repository.TryGetVariant(id, out variant);
	}

	public bool TryGetBait(string id, out FishingBaitDefinition bait)
	{
		return _repository.TryGetBait(id, out bait);
	}

	public IReadOnlyList<FishingCatchCandidate> GetCatchCandidates(
		FishingSetup setup)
	{
		return _repository.GetCatchCandidates(setup);
	}

	public bool TryGetBiteProfile(
		string id,
		out BiteProfileDefinition profile)
	{
		return _repository.TryGetBiteProfile(id, out profile);
	}

	public bool TryGetFightProfile(
		string id,
		out FightProfileDefinition profile)
	{
		return _repository.TryGetFightProfile(id, out profile);
	}

	public bool TryGetScriptedEvent(
		string id,
		out FishingEventDefinition fishingEvent)
	{
		return _repository.TryGetScriptedEvent(id, out fishingEvent);
	}

	public FishingEventRequest CreateEventRequest(string eventId)
	{
		return _repository.CreateEventRequest(eventId);
	}

	public FishingEventRequest CreateEventRequest(
		string eventId,
		FishingSetup setup)
	{
		return _repository.CreateEventRequest(eventId, setup);
	}
}
