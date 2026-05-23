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
        _controlsButton.Pressed += OnControlsPressed;
        _audioButton.Pressed += OnAudioPressed;
        _videoButton.Pressed += OnVideoPressed;
        _backButton.Pressed += OnBackPressed;
        UiNavigationHelper.FocusControl(_controlsButton);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Input.IsActionJustPressed("pause"))
            OnBackPressed();
    }

    private void OnBackPressed()
    {
        GameAudio.PlayUiAccent(this);
        if (TryCloseInOverlay())
            return;

        GetTree().ChangeSceneToFile(MainMenuScenePath);
    }

    private void OnControlsPressed()
    {
        GameAudio.PlayUiAccent(this);
        if (TryNavigateInOverlay(ControlsScenePath))
            return;

        GetTree().ChangeSceneToFile(ControlsScenePath);
    }

    private void OnAudioPressed()
    {
        GameAudio.PlayUiAccent(this);
        if (TryNavigateInOverlay(AudioScenePath))
            return;

        GetTree().ChangeSceneToFile(AudioScenePath);
    }

    private void OnVideoPressed()
    {
        GameAudio.PlayUiAccent(this);
        if (TryNavigateInOverlay(VideoScenePath))
            return;

        GetTree().ChangeSceneToFile(VideoScenePath);
    }

    private bool TryNavigateInOverlay(string scenePath)
    {
        for (Node n = this; n != null; n = n.GetParent())
        {
            if (n is ISettingsOverlayHost host)
            {
                host.NavigateSettings(scenePath);
                return true;
            }
        }

        return false;
    }

    private bool TryCloseInOverlay()
    {
        for (Node n = this; n != null; n = n.GetParent())
        {
            if (n is ISettingsOverlayHost host)
            {
                host.CloseSettingsOverlay();
                return true;
            }
        }

        return false;
    }
}
