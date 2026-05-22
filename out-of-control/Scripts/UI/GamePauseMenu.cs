using Godot;

public partial class GamePauseMenu : Control
{
	private const string NetworkManagerNodeName = "NetworkManager";

	[Export] private Button _resumeButton;
	[Export] private Button _menuButton;
	[Export] private Button _settingsButton;
	[Export(PropertyHint.File, "*.tscn")] public string SettingsScenePath = "res://Scenes/UI/Settings.tscn";
	[Export] private NodePath _crosshairPath = new("Crosshair");
	[Export] private NodePath _settingsOverlayPath = new("SettingsOverlay");

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		var player = FindOwningPlayer();
		if (player == null || !player.IsMultiplayerAuthority())
		{
			Visible = false;
			ProcessMode = ProcessModeEnum.Disabled;
			return;
		}

		Visible = false;

		if (_resumeButton != null)
			_resumeButton.Pressed += OnResumePressed;
		if (_menuButton != null)
			_menuButton.Pressed += OnMenuPressed;
		if (_settingsButton != null)
			_settingsButton.Pressed += OnSettingsPressed;
	}

	public void ShowMenu()
	{
		MoveToFront();
		MouseFilter = MouseFilterEnum.Stop;
		Visible = true;
		_resumeButton?.GrabFocus();
	}

	public void HideMenu()
	{
		Visible = false;
	}

	private void OnResumePressed()
	{
		HideMenu();
		var player = FindOwningPlayer();
		if (player == null || !player.IsMultiplayerAuthority())
			return;

		player.SetPauseControlsLocked(false);
		GetTree().Paused = false;
		var stats = player.GetStats();
		var dead = stats != null && GodotObject.IsInstanceValid(stats) && stats.CurrentHealth <= 0;
		if (!dead)
		{
			var cross = GetParent()?.GetNodeOrNull<GameCrosshair>(_crosshairPath);
			cross?.SetReticleVisible(true);
		}
	}

	private void OnMenuPressed()
	{
		HideMenu();
		GetTree().Paused = false;
		var nm = GetTree().Root.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName)
			?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName);
		nm?.ReturnToMainMenu();
	}

	private void OnSettingsPressed()
	{
		var overlay = GetParent()?.GetNodeOrNull<InGameSettingsOverlay>(_settingsOverlayPath);
		if (overlay != null)
		{
			HideMenu();
			overlay.ShowOverlay();
			return;
		}

		HideMenu();
		GetTree().Paused = false;
		if (!string.IsNullOrWhiteSpace(SettingsScenePath))
			GetTree().ChangeSceneToFile(SettingsScenePath);
	}

	private PlayerController FindOwningPlayer()
	{
		for (Node n = GetParent(); n != null; n = n.GetParent())
		{
			if (n is PlayerController pc)
				return pc;
		}

		return null;
	}
}
