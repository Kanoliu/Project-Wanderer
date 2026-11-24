using Godot;

[Tool]
public partial class DialogueBox : PanelContainer
{
	private Label _title;
	private Control _header;
	private RichTextLabel _text;
	private DialogueLineType _voice = DialogueLineType.Dialogue;

	[Export]
	public DialogueLineType Voice
	{
		get => _voice;
		set
		{
			_voice = value;
			if (_text != null) ApplyVoice();
		}
	}

	public override void _Ready()
	{
		_title = GetNode<Label>("DialogueStack/Header/DialogueTitle");
		_header = GetNode<Control>("DialogueStack/Header");
		_text = GetNode<RichTextLabel>("DialogueStack/Body/DialogueText");
		_title.VisibilityChanged += ApplyVoice;
		ApplyVoice();
	}

	private void ApplyVoice()
	{
		bool thought = Voice == DialogueLineType.Thought;
		bool narration = Voice == DialogueLineType.Narration;
		_header.Visible = !thought && !narration && _title.Visible && !string.IsNullOrWhiteSpace(_title.Text);
		ThemeTypeVariation = thought ? "ThoughtBody" : narration ? "" : "DialogueBodyStandalone";
		_text.ThemeTypeVariation = thought ? "ThoughtText" : narration ? "NarrationText" : "";
		_text.HorizontalAlignment = narration ? HorizontalAlignment.Center : HorizontalAlignment.Left;
	}
}
