using System.Text.Json.Serialization;

public class FishingResult
{
	public string EventId { get; set; } = "";
	public int Day { get; set; }
	public string BaseFishId { get; set; } = "";
	public string FishId { get; set; } = "";
	public bool IsSpecialVariant { get; set; }
	public int Quantity { get; set; }
	public FishingOutcome Outcome { get; set; } = FishingOutcome.None;

	[JsonIgnore]
	public bool Caught => Outcome == FishingOutcome.Caught;

	internal FishingResult ForDay(int day)
	{
		FishingResult entry = (FishingResult)MemberwiseClone();
		entry.Day = day;
		return entry;
	}

	public static FishingResult CreateCatch(
		string eventId,
		string fishId = "",
		int quantity = 1,
		string baseFishId = "",
		bool isSpecialVariant = false)
	{
		return new FishingResult
		{
			EventId = eventId,
			BaseFishId = baseFishId,
			FishId = fishId,
			IsSpecialVariant = isSpecialVariant,
			Quantity = quantity,
			Outcome = FishingOutcome.Caught
		};
	}

	public static FishingResult CreateFailure(
		string eventId,
		FishingOutcome outcome)
	{
		return new FishingResult
		{
			EventId = eventId,
			Outcome = outcome
		};
	}
}
