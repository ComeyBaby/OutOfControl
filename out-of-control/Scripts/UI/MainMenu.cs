using Godot;

public partial class MainMenu : Control
{
	[Export] private Button _playButton;
	[Export] private Button _settingsButton;
	[Export] private Button _quitButton;
	[Export(PropertyHint.File, "*.tscn")] public string LobbyScenePath = "res://Scenes/UI/Lobby.tscn";
	[Export(PropertyHint.File, "*.tscn")] public string SettingsScenePath = "res://Scenes/UI/Settings.tscn";

	public override void _Ready()
	{
		_playButton.Pressed += () => GetTree().ChangeSceneToFile(LobbyScenePath);
		_settingsButton.Pressed += () => GetTree().ChangeSceneToFile(SettingsScenePath);
		_quitButton.Pressed += () => GetTree().Quit();
		UiNavigationHelper.FocusControl(_playButton);
	}
}
