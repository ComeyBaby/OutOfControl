using Godot;

public partial class HUD : CanvasLayer
{
	private ProgressBar _healthBar;
	private Label _healthValueLabel;
	private ProgressBar _armorBar;
	private Label _armorValueLabel;
	private Control _pauseMenu;
	private Button _resumeButton;
	private Button _pauseSettingsButton;
	private Button _menuButton;
	private SettingsUI _inGameSettings;
	private PlayerStats _trackedStats;
	private Callable _healthChangedCallable;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_healthBar = GetNode<ProgressBar>("HealthUI/VBoxContainer/Health/Margin/HealthBar");
		_healthValueLabel = GetNode<Label>("HealthUI/VBoxContainer/Health/Margin/HealthBar/HealthValueLabel");
		_armorBar = GetNode<ProgressBar>("HealthUI/VBoxContainer/Armor/Margin/ArmorBar");
		_armorValueLabel = GetNode<Label>("HealthUI/VBoxContainer/Armor/Margin/ArmorBar/ArmorValueLabel");
		_pauseMenu = GetNode<Control>("PauseMenu");
		_resumeButton = GetNode<Button>("PauseMenu/Panel/Margin/VBox/ResumeButton");
		_pauseSettingsButton = GetNode<Button>("PauseMenu/Panel/Margin/VBox/SettingsButton");
		_menuButton = GetNode<Button>("PauseMenu/Panel/Margin/VBox/MenuButton");
		_inGameSettings = GetNode<SettingsUI>("InGameSettings");
		_healthChangedCallable = new Callable(this, nameof(OnHealthChanged));

		_resumeButton.Pressed += OnResumePressed;
		_pauseSettingsButton.Pressed += OnPauseSettingsPressed;
		_menuButton.Pressed += OnMenuPressed;
		_inGameSettings.ReturnToMainMenuOnBack = false;
		_inGameSettings.BackRequested += OnInGameSettingsBackRequested;
		_inGameSettings.EscapeRequested += OnInGameSettingsEscapeRequested;
		_pauseMenu.Visible = false;
		_inGameSettings.Visible = false;

		GetTree().SceneChanged += OnSceneChanged;
		TryBindToLocalPlayer();
	}

	public override void _ExitTree()
	{
		if (GetTree() != null)
			GetTree().SceneChanged -= OnSceneChanged;

		if (_inGameSettings != null)
			_inGameSettings.BackRequested -= OnInGameSettingsBackRequested;
		if (_inGameSettings != null)
			_inGameSettings.EscapeRequested -= OnInGameSettingsEscapeRequested;

		UnbindFromStats();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_inGameSettings != null && _inGameSettings.Visible)
			return;

		if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
		{
			if (GetTree().Paused)
				ResumeGame();
			else
				PauseGame();
		}
	}

	public override void _Process(double delta)
	{
		if (_trackedStats == null || !GodotObject.IsInstanceValid(_trackedStats))
			TryBindToLocalPlayer();
	}

	private void OnSceneChanged()
	{
		TryBindToLocalPlayer();
	}

	private void OnResumePressed()
	{
		ResumeGame();
	}

	private void OnPauseSettingsPressed()
	{
		ShowSettingsMenu();
	}

	private void OnMenuPressed()
	{
		HidePauseOverlay();
		GetTree().Paused = false;
		GetTree().Root.GetNodeOrNull<NetworkManager>("NetworkManager")?.ReturnToMainMenu();
	}

	private void OnInGameSettingsBackRequested()
	{
		ShowPauseMenu();
	}

	private void OnInGameSettingsEscapeRequested()
	{
		OnResumePressed();
	}

	private void TryBindToLocalPlayer()
	{
		if (_trackedStats != null && GodotObject.IsInstanceValid(_trackedStats))
			return;

		var scene = GetTree()?.CurrentScene;
		if (scene == null)
		{
			ShowFallback();
			return;
		}

		var playersRoot = scene.GetNodeOrNull<Node>("Players") ?? scene;
		var player = FindLocalPlayer(playersRoot);
		if (player == null)
		{
			ShowFallback();
			return;
		}

		BindToPlayer(player);
	}

	private PlayerController FindLocalPlayer(Node root)
	{
		foreach (var child in root.GetChildren())
		{
			if (child is PlayerController player && player.IsMultiplayerAuthority())
				return player;

			var nested = FindLocalPlayer(child);
			if (nested != null)
				return nested;
		}

		return null;
	}

	private void BindToPlayer(PlayerController player)
	{
		var stats = player.GetStats();
		if (stats == null)
		{
			ShowFallback();
			return;
		}

		if (_trackedStats != stats)
			UnbindFromStats();

		_trackedStats = stats;
		if (!_trackedStats.IsConnected(nameof(PlayerStats.HealthChanged), _healthChangedCallable))
			_trackedStats.Connect(nameof(PlayerStats.HealthChanged), _healthChangedCallable);

		RefreshHealthDisplay();
	}

	private void PauseGame()
	{
		GetTree().Paused = true;
		ShowPauseMenu();
		Input.MouseMode = Input.MouseModeEnum.Visible;
	}

	private void ResumeGame()
	{
		HidePauseOverlay();
		GetTree().Paused = false;
		Input.MouseMode = Input.MouseModeEnum.Captured;
	}

	private void ShowPauseMenu()
	{
		if (_pauseMenu != null)
			_pauseMenu.Visible = true;

		if (_inGameSettings != null)
			_inGameSettings.Visible = false;

		_resumeButton?.GrabFocus();
	}

	private void ShowSettingsMenu()
	{
		if (_pauseMenu != null)
			_pauseMenu.Visible = false;

		if (_inGameSettings != null)
			_inGameSettings.Visible = true;

		_inGameSettings?.GetNode<Button>("Panel/Margin/VBox/BackButton").GrabFocus();
	}

	private void HidePauseOverlay()
	{
		if (_pauseMenu != null)
			_pauseMenu.Visible = false;

		if (_inGameSettings != null)
			_inGameSettings.Visible = false;
	}

	private void UnbindFromStats()
	{
		if (_trackedStats != null && GodotObject.IsInstanceValid(_trackedStats) && _trackedStats.IsConnected(nameof(PlayerStats.HealthChanged), _healthChangedCallable))
			_trackedStats.Disconnect(nameof(PlayerStats.HealthChanged), _healthChangedCallable);

		_trackedStats = null;
	}

	private void OnHealthChanged(int currentHealth, int maxHealth)
	{
		RefreshHealthDisplay();
	}

	private void RefreshHealthDisplay()
	{
		if (_trackedStats == null || !GodotObject.IsInstanceValid(_trackedStats))
		{
			ShowFallback();
			return;
		}

		var currentHealth = _trackedStats.CurrentHealth;
		var maxHealth = _trackedStats.MaxHealth;
		var armor = Mathf.Max(0, _trackedStats.armor);

		_healthBar.MinValue = 0;
		_healthBar.MaxValue = maxHealth;
		_healthBar.Value = currentHealth;
		_healthValueLabel.Text = $"{currentHealth}/{maxHealth}";

		_armorBar.MinValue = 0;
		_armorBar.MaxValue = maxHealth;
		_armorBar.Value = Mathf.Clamp(armor, 0, maxHealth);
		_armorValueLabel.Text = $"{armor}/{maxHealth}";
	}

	private void ShowFallback()
	{

		_healthBar.Value = 0;
		_healthValueLabel.Text = "--/--";

		_armorBar.Value = 0;
		_armorValueLabel.Text = "--/--";
	}
}
