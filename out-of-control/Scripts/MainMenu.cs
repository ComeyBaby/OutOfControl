using Godot;

public partial class MainMenu : Control
{
    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        GetNode<Button>("Panel/Margin/VBox/PlayButton").Pressed += OnPlayPressed;
        GetNode<Button>("Panel/Margin/VBox/SettingsButton").Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/Settings.tscn");
        GetNode<Button>("Panel/Margin/VBox/QuitButton").Pressed += () => GetTree().Quit();
    }

    private void OnPlayPressed()
    {
        GetTree().ChangeSceneToFile("res://Scenes/Lobby.tscn");
    }
}
