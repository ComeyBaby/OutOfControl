using Godot;

public partial class SettingsAudioUI : Control
{
	[Export(PropertyHint.File, "*.tscn")] public string SettingsScenePath = "res://Scenes/UI/Settings.tscn";
	[Export] private HSlider _masterSlider;
	[Export] private HSlider _sfxSlider;
	[Export] private HSlider _musicSlider;
	[Export] private Label _masterValueLabel;
	[Export] private Label _sfxValueLabel;
	[Export] private Label _musicValueLabel;
	[Export] private Button _backButton;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		InitSlider(_masterSlider, _masterValueLabel, GameSettings.GetBusVolume("Master", 0.0f));
		InitSlider(_sfxSlider, _sfxValueLabel, GameSettings.GetBusVolume("SFX", 0.0f));
		InitSlider(_musicSlider, _musicValueLabel, GameSettings.GetBusVolume("Music", 0.0f));

		_masterSlider.ValueChanged += OnMasterChanged;
		_sfxSlider.ValueChanged += OnSfxChanged;
		_musicSlider.ValueChanged += OnMusicChanged;

		_backButton.Pressed += OnBackPressed;
		UiNavigationHelper.FocusControl(_masterSlider);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Input.IsActionJustPressed("pause"))
			OnBackPressed();
	}

	private void InitSlider(HSlider slider, Label label, float value)
	{
		slider.MinValue = 0;
		slider.MaxValue = 100;
		slider.Step = 1;
		var percent = GameSettings.VolumeDbToPercent(value);
		slider.Value = percent;
		UpdatePercentLabel(label, (float)percent);
	}

	private void OnMasterChanged(double value)
	{
		var db = GameSettings.PercentToVolumeDb((float)value);
		GameSettings.SetBusVolume("Master", db);
		GameSettings.Save();
		UpdatePercentLabel(_masterValueLabel, (float)value);
	}

	private void OnSfxChanged(double value)
	{
		var db = GameSettings.PercentToVolumeDb((float)value);
		GameSettings.SetBusVolume("SFX", db);
		GameSettings.Save();
		UpdatePercentLabel(_sfxValueLabel, (float)value);
	}

	private void OnMusicChanged(double value)
	{
		var db = GameSettings.PercentToVolumeDb((float)value);
		GameSettings.SetBusVolume("Music", db);
		GameSettings.Save();
		UpdatePercentLabel(_musicValueLabel, (float)value);
	}

	private static void UpdatePercentLabel(Label label, float value)
	{
		label.Text = $"{Mathf.RoundToInt(value)}%";
	}

	private void OnBackPressed()
	{
		GameAudio.PlayUiAccent(this);
		if (SettingsOverlayNavigation.TryNavigate(this, SettingsScenePath))
			return;

		GetTree().ChangeSceneToFile(SettingsScenePath);
	}
}
