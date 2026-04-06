using Godot;

public partial class SettingsUI : Control
{
    [Export(PropertyHint.File, "*.tscn")]
    public string MainMenuScenePath { get; set; } = "res://Scenes/MainMenu.tscn";

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GetNode<Button>("Panel/Margin/VBox/BackButton").Pressed += OnBackPressed;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Input.IsActionJustPressed("pause"))
            OnBackPressed();
    }

    private void OnBackPressed()
    {
        GetTree().ChangeSceneToFile(MainMenuScenePath);
    }
}
