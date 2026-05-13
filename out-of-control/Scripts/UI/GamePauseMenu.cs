using Godot;

public partial class GamePauseMenu : Control
{
	private const string NetworkManagerNodeName = "NetworkManager";

	[Export] private Button _resumeButton;
	[Export] private Button _menuButton;
	[Export] private Button _perksButton;
	[Export] private NodePath _hudPanelPath = new("HUD");
	[Export] private NodePath _crosshairPath = new("Crosshair");

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
		if (_perksButton != null)
			_perksButton.Pressed += OnPerksPressed;
	}

	public void ShowMenu()
	{
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
		Input.MouseMode = dead ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
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

	private void OnPerksPressed()
	{
		HideMenu();
		var hud = GetSiblingHud();
		hud?.RequestPerkSelectionFromPause();
	}

	private GameHudPanel GetSiblingHud()
	{
		return GetParent()?.GetNodeOrNull<GameHudPanel>(_hudPanelPath);
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
