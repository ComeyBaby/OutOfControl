using Godot;

public interface ISettingsOverlayHost
{
	void NavigateSettings(string scenePath);
	void CloseSettingsOverlay();
}

public partial class InGameSettingsOverlay : Control, ISettingsOverlayHost
{
	[Export(PropertyHint.File, "*.tscn")] public string RootSettingsScenePath = "res://Scenes/UI/Settings.tscn";
	[Export(PropertyHint.File, "*.tscn")] public string AudioSettingsScenePath = "res://Scenes/UI/SettingsAudio.tscn";
	[Export(PropertyHint.File, "*.tscn")] public string VideoSettingsScenePath = "res://Scenes/UI/SettingsVideo.tscn";
	[Export(PropertyHint.File, "*.tscn")] public string ControlsSettingsScenePath = "res://Scenes/UI/SettingsControls.tscn";
	[Export] private NodePath _pauseMenuPath = new("../PauseMenu");
	[Export] private NodePath _rootSettingsPath = new("SettingsRoot");
	[Export] private NodePath _audioSettingsPath = new("SettingsAudio");
	[Export] private NodePath _videoSettingsPath = new("SettingsVideo");
	[Export] private NodePath _controlsSettingsPath = new("SettingsControls");

	private Control _rootSettings;
	private Control _audioSettings;
	private Control _videoSettings;
	private Control _controlsSettings;
	private Control _activeSettingsScreen;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_rootSettings = GetNodeOrNull<Control>(_rootSettingsPath);
		_audioSettings = GetNodeOrNull<Control>(_audioSettingsPath);
		_videoSettings = GetNodeOrNull<Control>(_videoSettingsPath);
		_controlsSettings = GetNodeOrNull<Control>(_controlsSettingsPath);
		HideAllSettingsScreens();
		Visible = false;
		MouseFilter = MouseFilterEnum.Ignore;
	}

	public void ShowOverlay()
	{
		GameAudio.PlayUiAccent(this);
		if (string.IsNullOrWhiteSpace(RootSettingsScenePath))
			return;

		Visible = true;
		MouseFilter = MouseFilterEnum.Stop;
		MoveToFront();
		NavigateSettings(RootSettingsScenePath);
	}

	public void NavigateSettings(string scenePath)
	{
		if (string.IsNullOrWhiteSpace(scenePath))
			return;

		var target = ResolveScreenByScenePath(scenePath);
		if (target == null)
			return;

		HideAllSettingsScreens();
		target.Visible = true;
		target.MouseFilter = MouseFilterEnum.Stop;
		target.ProcessMode = ProcessModeEnum.Always;
		target.MoveToFront();
		_activeSettingsScreen = target;
	}

	public void CloseSettingsOverlay()
	{
		GameAudio.PlayUiAccent(this);
		HideAllSettingsScreens();
		_activeSettingsScreen = null;

		Visible = false;
		MouseFilter = MouseFilterEnum.Ignore;

		var pauseMenu = GetParent()?.GetNodeOrNull<GamePauseMenu>(_pauseMenuPath);
		pauseMenu?.ShowMenu();
	}

	private Control ResolveScreenByScenePath(string scenePath)
	{
		if (scenePath == RootSettingsScenePath)
			return _rootSettings;
		if (scenePath == AudioSettingsScenePath)
			return _audioSettings;
		if (scenePath == VideoSettingsScenePath)
			return _videoSettings;
		if (scenePath == ControlsSettingsScenePath)
			return _controlsSettings;

		return null;
	}

	private void HideAllSettingsScreens()
	{
		HideScreen(_rootSettings);
		HideScreen(_audioSettings);
		HideScreen(_videoSettings);
		HideScreen(_controlsSettings);
	}

	private static void HideScreen(Control screen)
	{
		if (screen == null)
			return;
		screen.Visible = false;
		screen.MouseFilter = MouseFilterEnum.Ignore;
	}
}
