using Godot;

[Tool]
public partial class BaseButton : Button
{
    private TextureRect _confirmationMark;

    public override void _Ready()
    {
        _confirmationMark = GetNode<TextureRect>("ConfirmationMark");
        UpdateConfirmationMark();
    }

    public override void _Process(double delta) => UpdateConfirmationMark();

    private void UpdateConfirmationMark()
    {
        // Godot owns input states; only synchronize the existing scene marker.
        bool visible = !Disabled && (HasFocus() || IsPressed());
        if (_confirmationMark.Visible != visible)
            _confirmationMark.Visible = visible;
    }
}

