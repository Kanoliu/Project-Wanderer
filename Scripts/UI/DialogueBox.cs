using Godot;

[Tool]
public partial class DialogueBox : PanelContainer
{
    private Label _title;
    private Control _header;
    private PanelContainer _body;

    public override void _Ready()
    {
        _title = GetNode<Label>("DialogueStack/Header/DialogueTitle");
        _header = GetNode<Control>("DialogueStack/Header");
        _body = GetNode<PanelContainer>("DialogueStack/Body");
        UpdateSpeakerState();
    }

    public override void _Process(double delta) => UpdateSpeakerState();

    private void UpdateSpeakerState()
    {
        bool showHeader = _title.Visible && !string.IsNullOrWhiteSpace(_title.Text);
        if (_header.Visible != showHeader)
            _header.Visible = showHeader;
        StringName variation = showHeader ? "DialogueBody" : "DialogueBodyStandalone";
        if (_body.ThemeTypeVariation != variation)
            _body.ThemeTypeVariation = variation;
    }
}
