using Godot;

public partial class SettingsUI : Control
{
    [Export(PropertyHint.File, "*.tscn")] public string MainMenuScenePath;
    [Export] private Button _backButton;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _backButton.Pressed += OnBackPressed;
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
