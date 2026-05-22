using Godot;

public partial class SettingsUI : Control
{
    [Export(PropertyHint.File, "*.tscn")] public string MainMenuScenePath;
    [Export(PropertyHint.File, "*.tscn")] public string ControlsScenePath;
    [Export(PropertyHint.File, "*.tscn")] public string AudioScenePath;
    [Export(PropertyHint.File, "*.tscn")] public string VideoScenePath;
    [Export] private Button _controlsButton;
    [Export] private Button _audioButton;
    [Export] private Button _videoButton;
    [Export] private Button _backButton;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GameSettings.LoadAndApply();
        _controlsButton.Pressed += OnControlsPressed;
        _audioButton.Pressed += OnAudioPressed;
        _videoButton.Pressed += OnVideoPressed;
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

    private void OnControlsPressed()
    {
        GetTree().ChangeSceneToFile(ControlsScenePath);
    }

    private void OnAudioPressed()
    {
        GetTree().ChangeSceneToFile(AudioScenePath);
    }

    private void OnVideoPressed()
    {
        GetTree().ChangeSceneToFile(VideoScenePath);
    }
}
