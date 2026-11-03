using System;

public class CharacterRequest
{
	public string Id { get; set; } = "";
	public string CharacterId { get; set; } = "";
	public string FishId { get; set; } = "";
	public int RequiredQuantity { get; set; } = 1;
	public DialogueEffect[] FulfilledEffects { get; set; } =
		Array.Empty<DialogueEffect>();
	public DialogueEffect[] UnfulfilledEffects { get; set; } =
		Array.Empty<DialogueEffect>();
}
