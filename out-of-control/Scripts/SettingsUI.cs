using System;
using Godot;

public partial class SettingsUI : Control
{
    [Export]
    public bool ReturnToMainMenuOnBack { get; set; } = true;

    [Export(PropertyHint.File, "*.tscn")]
    public string MainMenuScenePath { get; set; } = "res://Scenes/MainMenu.tscn";

    public event Action BackRequested;
    public event Action EscapeRequested;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        GetNode<Button>("Panel/Margin/VBox/BackButton").Pressed += OnBackPressed;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            if (ReturnToMainMenuOnBack)
                OnBackPressed();
            else
                EscapeRequested?.Invoke();
        }
    }

    private void OnBackPressed()
    {
        if (ReturnToMainMenuOnBack)
        {
            GetTree().ChangeSceneToFile(MainMenuScenePath);
            return;
        }

        BackRequested?.Invoke();
    }
}
