using System;

public class DialogueLine
{
	public string Id { get; set; } = "";
	public DialogueLineType Type { get; set; }
	public string Speaker { get; set; } = "";
	public string SpeakerId { get; set; } = "";
	public string Text { get; set; } = "";
	public string ChapterSubtitle { get; set; } = "";
	public int? Day { get; set; }
	public string IllustrationId { get; set; } = "";
	public string SceneId { get; set; } = "";
	public DialoguePace Pace { get; set; } = DialoguePace.Normal;
	public double DurationSeconds { get; set; }
	public string NextId { get; set; } = "";
	public string RequiredCatchId { get; set; } = "";
	public bool RequiresDinnerFish { get; set; }
	public string FallbackId { get; set; } = "";
	public DialogueChoice[] Choices { get; set; } =
		Array.Empty<DialogueChoice>();
	public DialogueEffect[] Effects { get; set; } =
		Array.Empty<DialogueEffect>();
	public string FishingEventId { get; set; } = "";
	public FishingDirective Fishing { get; set; } = FishingDirective.None;
	public FishingTutorialStep Tutorial { get; set; } =
		FishingTutorialStep.None;
}
