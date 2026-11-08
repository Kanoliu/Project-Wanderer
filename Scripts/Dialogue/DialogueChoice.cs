using System;

public class DialogueChoice
{
	public string Text { get; set; } = "";
	public string TargetId { get; set; } = "";
	public DialogueEffect[] Effects { get; set; } =
		Array.Empty<DialogueEffect>();
}
