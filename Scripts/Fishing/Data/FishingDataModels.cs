using System;
using System.Text.Json.Serialization;

public enum FishCatchDifficulty
{
	Low,
	Medium,
	High
}

public enum FishBitePattern
{
	Probe,
	Strike,
	Drag
}

public enum FishFightPattern
{
	Stable,
	Active,
	Sprint,
	Heavy
}

public class FishingSourceReference
{
	public string Title { get; set; } = "";
	public string Url { get; set; } = "";
}

public class FishAvailabilityDefinition
{
	public string[] BaitIds { get; set; } = Array.Empty<string>();
	public string[] DepthIds { get; set; } = Array.Empty<string>();
	public string[] WeatherIds { get; set; } = Array.Empty<string>();
	public string[] WindIds { get; set; } = Array.Empty<string>();
	public string[] TimeIds { get; set; } = Array.Empty<string>();
	public string[] AreaIds { get; set; } = Array.Empty<string>();
	public string[] HabitatTags { get; set; } = Array.Empty<string>();
}

public class FishVariantDefinition
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string Description { get; set; } = "";
	public int OccurrenceWeight { get; set; } = 1;
}

public class FishingBaitDefinition
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string Description { get; set; } = "";
}

public class FishingDepthDefinition
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public FishingDepth Value { get; set; }
}

public class FishingDifficultyWeightDefinition
{
	public FishCatchDifficulty Difficulty { get; set; }
	public int Weight { get; set; }
}

public class FishingSelectionRulesDefinition
{
	public int StandardFormWeight { get; set; } = 19;
	public FishingDifficultyWeightDefinition[] DifficultyWeights { get; set; } =
		Array.Empty<FishingDifficultyWeightDefinition>();
}

public class FishingCatchCandidate
{
	public FishDefinition Fish { get; set; } = new();
	public int Weight { get; set; }
}

public class FishDefinition
{
	public string Id { get; set; } = "";
	public string DisplayName { get; set; } = "";
	public string Description { get; set; } = "";
	public FishCatchDifficulty CatchDifficulty { get; set; }
	public FishAvailabilityDefinition Availability { get; set; } = new();
	public string BiteProfileId { get; set; } = "";
	public string FightProfileId { get; set; } = "";
	public string[] EcologyTags { get; set; } = Array.Empty<string>();
	public string[] UseTags { get; set; } = Array.Empty<string>();
	public string[] ResearchNotes { get; set; } = Array.Empty<string>();
	public FishVariantDefinition[] Variants { get; set; } =
		Array.Empty<FishVariantDefinition>();
}

public class BiteProfileDefinition
{
	public string Id { get; set; } = "";
	public FishBitePattern Pattern { get; set; }
	public string Description { get; set; } = "";
}

public class FightProfileDefinition
{
	public string Id { get; set; } = "";
	public FishFightPattern Pattern { get; set; }
	public string Description { get; set; } = "";
	public FishMovementSettings Movement { get; set; } = new();
	// Resolved from the global growth parameter; species and variants cannot override it.
	[JsonIgnore]
	public float GainRate { get; set; } = 0.32f;
	public FishCatchDifficulty? LossDifficulty { get; set; }
	// Resolved per encounter from the shared difficulty table, not a per-species JSON number.
	[JsonIgnore]
	public float LossRate { get; set; } = 0.08f;
}

public class ProgressLossTierDefinition
{
	public FishCatchDifficulty Difficulty { get; set; }
	public float LossRate { get; set; }
}

public class FishingEventDefinition
{
	public string Id { get; set; } = "";
	public string FishId { get; set; } = "";
	public int Quantity { get; set; } = 1;
}

public class FishingEventRequest
{
	public string EventId { get; set; } = "";
	public string BaseFishId { get; set; } = "";
	public string FishId { get; set; } = "";
	public bool IsSpecialVariant { get; set; }
	public int Quantity { get; set; } = 1;
	public FishBitePattern BitePattern { get; set; } =
		FishBitePattern.Probe;
	public string FightProfileId { get; set; } = "";
	public FishMovementSettings FightMovement { get; set; } = new();
	public float FightGainRate { get; set; } = 0.32f;
	public float FightLossRate { get; set; } = 0.08f;
}

public class FishCatalogDocument
{
	public int SchemaVersion { get; set; } = 1;
	public FishingSourceReference[] Sources { get; set; } =
		Array.Empty<FishingSourceReference>();
	public FishingBaitDefinition[] Baits { get; set; } =
		Array.Empty<FishingBaitDefinition>();
	public FishingDepthDefinition[] Depths { get; set; } =
		Array.Empty<FishingDepthDefinition>();
	public FishingSelectionRulesDefinition SelectionRules { get; set; } = new();
	public FishDefinition[] Fish { get; set; } =
		Array.Empty<FishDefinition>();
}

public class FishingProfilesDocument
{
	public int SchemaVersion { get; set; } = 1;
	public float SpecialVariantMultiplier { get; set; } = 1.15f;
	public float ProgressCapacityMultiplier { get; set; } = 2.5f;
	public float ProgressGainRate { get; set; }
	public ProgressLossTierDefinition[] ProgressLossTiers { get; set; } = Array.Empty<ProgressLossTierDefinition>();
	public FishingSourceReference[] Sources { get; set; } =
		Array.Empty<FishingSourceReference>();
	public BiteProfileDefinition[] BiteProfiles { get; set; } =
		Array.Empty<BiteProfileDefinition>();
	public FightProfileDefinition[] FightProfiles { get; set; } =
		Array.Empty<FightProfileDefinition>();
}

public class FishingEventsDocument
{
	public int SchemaVersion { get; set; } = 1;
	public FishingSourceReference[] Sources { get; set; } =
		Array.Empty<FishingSourceReference>();
	public FishingEventDefinition[] Events { get; set; } =
		Array.Empty<FishingEventDefinition>();
}
