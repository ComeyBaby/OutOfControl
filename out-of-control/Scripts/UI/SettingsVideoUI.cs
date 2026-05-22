using Godot;

public partial class SettingsVideoUI : Control
{
	[Export(PropertyHint.File, "*.tscn")] public string SettingsScenePath = "res://Scenes/UI/Settings.tscn";
	[Export] private CheckButton _fullscreenToggle;
	[Export] private CheckButton _showFpsToggle;
	[Export] private CheckButton _vSyncToggle;
	[Export] private HSlider _uiScaleSlider;
	[Export] private Label _uiScaleValueLabel;
	[Export] private Button _backButton;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		_fullscreenToggle.ButtonPressed = GameSettings.Fullscreen;
		_fullscreenToggle.Toggled += OnFullscreenToggled;

		_showFpsToggle.ButtonPressed = GameSettings.ShowFps;
		_showFpsToggle.Toggled += OnShowFpsToggled;

		_vSyncToggle.ButtonPressed = GameSettings.VSyncEnabled;
		_vSyncToggle.Toggled += OnVSyncToggled;

		_uiScaleSlider.MinValue = 0.75f;
		_uiScaleSlider.MaxValue = 4.0f;
		_uiScaleSlider.Step = 0.05f;
		_uiScaleSlider.Value = GameSettings.UiScale;
		_uiScaleSlider.ValueChanged += OnUiScaleChanged;
		UpdateUiScaleLabel((float)_uiScaleSlider.Value);

		_backButton.Pressed += OnBackPressed;
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Input.IsActionJustPressed("pause"))
			OnBackPressed();
	}

	private void OnFullscreenToggled(bool enabled)
	{
		GameSettings.SetFullscreen(enabled);
		GameSettings.Save();
	}

	private void OnUiScaleChanged(double value)
	{
		GameSettings.SetUiScale((float)value);
		GameSettings.Save();
		UpdateUiScaleLabel((float)value);
	}

	private void OnShowFpsToggled(bool enabled)
	{
		GameSettings.SetShowFps(enabled);
		GameSettings.Save();
	}

	private void OnVSyncToggled(bool enabled)
	{
		GameSettings.SetVSyncEnabled(enabled);
		GameSettings.Save();
	}

	private void UpdateUiScaleLabel(float scale)
	{
		_uiScaleValueLabel.Text = $"{scale:0.00}x";
	}

	private void OnBackPressed()
	{
		if (TryNavigateInOverlay(SettingsScenePath))
			return;

		GetTree().ChangeSceneToFile(SettingsScenePath);
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
}
