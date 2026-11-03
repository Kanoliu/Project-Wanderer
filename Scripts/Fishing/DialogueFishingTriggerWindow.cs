public sealed class DialogueFishingTriggerWindow
{
	public string Id { get; set; } = "";
	public string ChapterId { get; set; } = "";
	public string StartId { get; set; } = "";
	public string EndId { get; set; } = "";
	public double MinDelaySeconds { get; set; } = 2.0;
	public double MaxDelaySeconds { get; set; } = 9.0;
}

public sealed class DialogueFishingTriggerWindowDocument
{
	public int SchemaVersion { get; set; }
	public DialogueFishingTriggerWindow[] Windows { get; set; } =
		System.Array.Empty<DialogueFishingTriggerWindow>();
}
