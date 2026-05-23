using Godot;

public partial class SettingsUI : Control
{
	[Export(PropertyHint.File, "*.tscn")] public string MainMenuScenePath = "res://Scenes/UI/MainMenu.tscn";
	[Export(PropertyHint.File, "*.tscn")] public string ControlsScenePath = "res://Scenes/UI/SettingsControls.tscn";
	[Export(PropertyHint.File, "*.tscn")] public string AudioScenePath = "res://Scenes/UI/SettingsAudio.tscn";
	[Export(PropertyHint.File, "*.tscn")] public string VideoScenePath = "res://Scenes/UI/SettingsVideo.tscn";
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
		if (SettingsOverlayNavigation.TryClose(this))
			return;

		GetTree().ChangeSceneToFile(MainMenuScenePath);
	}

	private void OnControlsPressed()
	{
		GameAudio.PlayUiAccent(this);
		if (SettingsOverlayNavigation.TryNavigate(this, ControlsScenePath))
			return;

		GetTree().ChangeSceneToFile(ControlsScenePath);
	}

	private void OnAudioPressed()
	{
		GameAudio.PlayUiAccent(this);
		if (SettingsOverlayNavigation.TryNavigate(this, AudioScenePath))
			return;

		GetTree().ChangeSceneToFile(AudioScenePath);
	}

	private void OnVideoPressed()
	{
		GameAudio.PlayUiAccent(this);
		if (SettingsOverlayNavigation.TryNavigate(this, VideoScenePath))
			return;

		GetTree().ChangeSceneToFile(VideoScenePath);
	}
}
