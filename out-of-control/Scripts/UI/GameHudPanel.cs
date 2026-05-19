using Godot;

public partial class GameHudPanel : Control
{
	private const string NetworkManagerNodeName = "NetworkManager";
	private const string PerkSelectionSceneDefaultPath = "res://Scenes/UI/PerkSelectionUI.tscn";

	[Export] private ProgressBar _healthBar;
	[Export] private Label _healthValueLabel;
	[Export] private ProgressBar _staminaBar;
	[Export] private Label _staminaValueLabel;
	[Export] private ProgressBar _ammoBar;
	[Export] private Label _ammoValueLabel;
	[Export] private NodePath _crosshairPath = new("Crosshair");
	[Export] private NodePath _pauseMenuPath = new("PauseMenu");
	[Export] private NodePath _perkSelectionPath = new("../../PerkSelection");
	[Export(PropertyHint.File, "*.tscn")] public string PerkSelectionScenePath = PerkSelectionSceneDefaultPath;

	private PlayerController _player;
	private GameCrosshair _crosshair;
	private GamePauseMenu _pauseMenu;
	private NetworkManager _networkManager;
	private RoundManager _roundManager;
	private PlayerStats _trackedStats;
	private Callable _healthChangedCallable;
	private Callable _staminaChangedCallable;
	private Callable _ammoChangedCallable;
	private Callable _playersChangedCallable;
	private Callable _roundChangedCallable;
	private Callable _scoreboardChangedCallable;
	private Callable _perkChosenCallable;
	private Callable _sceneChangedCallable;
	private PerkSelectionUI _perkSelectionUI;
	private Control _perkSelectionRoot;
	private bool _perkSelectionActive;
	private bool _roundPerkSelectionComplete;
	private bool _sceneChangedConnected;
	private RoundPhase _lastRoundPhase = RoundPhase.Lobby;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		_player = FindOwningPlayer();
		if (_player == null || !_player.IsMultiplayerAuthority())
		{
			Visible = false;
			ProcessMode = ProcessModeEnum.Disabled;
			return;
		}

		var parent = GetParent();
		_crosshair = parent?.GetNodeOrNull<GameCrosshair>(_crosshairPath);
		_pauseMenu = parent?.GetNodeOrNull<GamePauseMenu>(_pauseMenuPath);

		_healthChangedCallable = new Callable(this, nameof(OnHealthChanged));
		_staminaChangedCallable = new Callable(this, nameof(OnStaminaChanged));
		_ammoChangedCallable = new Callable(this, nameof(OnAmmoChanged));
		_playersChangedCallable = new Callable(this, nameof(OnPlayersChanged));
		_roundChangedCallable = new Callable(this, nameof(OnRoundChanged));
		_scoreboardChangedCallable = new Callable(this, nameof(OnScoreboardChanged));
		_perkChosenCallable = new Callable(this, nameof(OnPerkChosen));
		_sceneChangedCallable = new Callable(this, nameof(OnSceneChanged));
		EnsurePerkSelectionLoaded();

		TryBindNetworkManager();
		if (GetTree() != null && !GetTree().IsConnected("scene_changed", _sceneChangedCallable))
		{
			GetTree().Connect("scene_changed", _sceneChangedCallable);
			_sceneChangedConnected = true;
		}
		EnsureStatsBound();
	}

	public override void _ExitTree()
	{
		if (_sceneChangedConnected && GetTree() != null &&
			GetTree().IsConnected("scene_changed", _sceneChangedCallable))
			GetTree().Disconnect("scene_changed", _sceneChangedCallable);
		_sceneChangedConnected = false;

		if (_networkManager != null)
		{
			if (_networkManager.IsConnected("PlayersChanged", _playersChangedCallable))
				_networkManager.Disconnect("PlayersChanged", _playersChangedCallable);
		}

		DisconnectRoundManagerSignals();
		DestroyPerkSelection();
		UnbindFromStatsInternal();
	}

	public override void _Input(InputEvent @event)
	{
		if (_player == null || !_player.IsMultiplayerAuthority())
			return;

		if (@event.IsActionPressed("pause"))
			TogglePauseMenu();
	}

	public override void _Process(double delta)
	{
		if (_player == null || !_player.IsMultiplayerAuthority())
			return;

		if (_networkManager == null)
			TryBindNetworkManager();

		EnsureStatsBound();
		RefreshAmmoDisplay();
		TryShowRoundPerkSelection();
	}

	private void ResumeFromPauseMenu()
	{
		if (_player == null || !_player.IsMultiplayerAuthority())
			return;

		_pauseMenu?.HideMenu();
		_player.SetPauseControlsLocked(false);
		GetTree().Paused = false;
		var dead = IsPlayerDead();
		if (!dead)
			_crosshair?.SetReticleVisible(true);
	}

	public void RequestPerkSelectionFromPause()
	{
		if (_player == null || !_player.IsMultiplayerAuthority())
			return;

		TryShowPerkSelection(false);
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

	private void OnSceneChanged()
	{
		HidePerkSelection();
		TryBindNetworkManager();
		EnsureStatsBound();
	}

	private void TryBindNetworkManager()
	{
		var manager = GetTree().Root.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName)
			?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName);

		if (manager == _networkManager)
			return;

		if (_networkManager != null && _networkManager.IsConnected("PlayersChanged", _playersChangedCallable))
			_networkManager.Disconnect("PlayersChanged", _playersChangedCallable);
		DisconnectRoundManagerSignals();

		_networkManager = manager;
		if (_networkManager != null && !_networkManager.IsConnected("PlayersChanged", _playersChangedCallable))
			_networkManager.Connect("PlayersChanged", _playersChangedCallable);

		_roundManager = _networkManager?.GetRoundManager();
		ConnectRoundManagerSignals();
	}

	private void EnsureStatsBound()
	{
		var stats = _player?.GetStats();
		if (stats == null || !GodotObject.IsInstanceValid(stats))
		{
			ShowFallback();
			return;
		}

		if (_trackedStats != stats)
			BindToStats(stats);
	}

	private void TogglePauseMenu()
	{
		if (_pauseMenu == null || !GodotObject.IsInstanceValid(_pauseMenu))
			_pauseMenu = ResolvePauseMenu();

		if (_pauseMenu != null && _pauseMenu.Visible)
		{
			ResumeFromPauseMenu();
			return;
		}

		PauseGame();
	}

	private void PauseGame()
	{
		if (_pauseMenu == null || !GodotObject.IsInstanceValid(_pauseMenu))
			_pauseMenu = ResolvePauseMenu();
		if (_pauseMenu == null)
			return;

		_player?.SetPauseControlsLocked(true);
		_crosshair?.SetReticleVisible(false);
		_pauseMenu?.ShowMenu();
	}

	private GamePauseMenu ResolvePauseMenu()
	{
		var local = GetNodeOrNull<GamePauseMenu>(_pauseMenuPath);
		if (local != null)
			return local;

		var parent = GetParent();
		var fromParent = parent?.GetNodeOrNull<GamePauseMenu>(_pauseMenuPath);
		if (fromParent != null)
			return fromParent;

		var player = _player ?? FindOwningPlayer();
		var fromPlayerPath = player?.GetNodeOrNull<GamePauseMenu>(_pauseMenuPath);
		if (fromPlayerPath != null)
			return fromPlayerPath;

		var byName = player?.GetNodeOrNull<GamePauseMenu>("PauseMenu");
		if (byName != null)
			return byName;

		return player != null ? FindPauseMenuRecursive(player) : null;
	}

	private static GamePauseMenu FindPauseMenuRecursive(Node root)
	{
		foreach (var child in root.GetChildren())
		{
			if (child is GamePauseMenu menu)
				return menu;

			if (child is Node childNode)
			{
				var nested = FindPauseMenuRecursive(childNode);
				if (nested != null)
					return nested;
			}
		}

		return null;
	}

	private void OnPlayersChanged()
	{
		RefreshAmmoDisplay();
	}

	private void OnRoundChanged()
	{
		var phase = _roundManager?.Phase ?? RoundPhase.Lobby;
		if (phase != _lastRoundPhase)
		{
			if (phase == RoundPhase.PerkSelection)
				_roundPerkSelectionComplete = false;
			else if (_lastRoundPhase == RoundPhase.PerkSelection)
			{
				HidePerkSelection();
				GetTree().Paused = false;
			}

			_lastRoundPhase = phase;
		}

		TryShowRoundPerkSelection();
	}

	private void OnScoreboardChanged()
	{
		RefreshAmmoDisplay();
	}

	private void ConnectRoundManagerSignals()
	{
		if (_roundManager == null)
			return;

		if (!_roundManager.IsConnected(nameof(RoundManager.RoundChanged), _roundChangedCallable))
			_roundManager.Connect(nameof(RoundManager.RoundChanged), _roundChangedCallable);
		if (!_roundManager.IsConnected(nameof(RoundManager.ScoreboardChanged), _scoreboardChangedCallable))
			_roundManager.Connect(nameof(RoundManager.ScoreboardChanged), _scoreboardChangedCallable);
	}

	private void DisconnectRoundManagerSignals()
	{
		if (_roundManager == null)
			return;

		if (_roundManager.IsConnected(nameof(RoundManager.RoundChanged), _roundChangedCallable))
			_roundManager.Disconnect(nameof(RoundManager.RoundChanged), _roundChangedCallable);
		if (_roundManager.IsConnected(nameof(RoundManager.ScoreboardChanged), _scoreboardChangedCallable))
			_roundManager.Disconnect(nameof(RoundManager.ScoreboardChanged), _scoreboardChangedCallable);
	}

	private void BindToStats(PlayerStats stats)
	{
		if (_trackedStats == stats)
			return;

		UnbindFromStatsInternal();
		_trackedStats = stats;
		if (_trackedStats == null || !GodotObject.IsInstanceValid(_trackedStats))
			return;

		if (!_trackedStats.IsConnected(nameof(PlayerStats.HealthChanged), _healthChangedCallable))
			_trackedStats.Connect(nameof(PlayerStats.HealthChanged), _healthChangedCallable);
		if (!_trackedStats.IsConnected(nameof(PlayerStats.StaminaChanged), _staminaChangedCallable))
			_trackedStats.Connect(nameof(PlayerStats.StaminaChanged), _staminaChangedCallable);
		if (!_trackedStats.IsConnected(nameof(PlayerStats.AmmoChanged), _ammoChangedCallable))
			_trackedStats.Connect(nameof(PlayerStats.AmmoChanged), _ammoChangedCallable);

		RefreshHealthDisplay();
		RefreshStaminaDisplay();
		RefreshAmmoDisplay();
		TryShowRoundPerkSelection();
	}

	private void UnbindFromStatsInternal()
	{
		if (_trackedStats != null && GodotObject.IsInstanceValid(_trackedStats))
		{
			if (_trackedStats.IsConnected(nameof(PlayerStats.HealthChanged), _healthChangedCallable))
				_trackedStats.Disconnect(nameof(PlayerStats.HealthChanged), _healthChangedCallable);
			if (_trackedStats.IsConnected(nameof(PlayerStats.StaminaChanged), _staminaChangedCallable))
				_trackedStats.Disconnect(nameof(PlayerStats.StaminaChanged), _staminaChangedCallable);
			if (_trackedStats.IsConnected(nameof(PlayerStats.AmmoChanged), _ammoChangedCallable))
				_trackedStats.Disconnect(nameof(PlayerStats.AmmoChanged), _ammoChangedCallable);
		}

		_trackedStats = null;
	}

	private void TryShowRoundPerkSelection()
	{
		if (_networkManager == null || !_networkManager.IsRoundWaitingForPerks() || _roundPerkSelectionComplete)
			return;

		TryShowPerkSelection(true);
	}

	private void TryShowPerkSelection(bool requiredByRound = false)
	{
		if (_perkSelectionActive)
			return;

		if (requiredByRound && _trackedStats == null)
			return;

		if (!string.Equals(GetTree()?.CurrentScene?.SceneFilePath, _networkManager?.GameScenePath, System.StringComparison.OrdinalIgnoreCase))
			return;

		if (string.IsNullOrWhiteSpace(PerkSelectionScenePath))
			return;

		EnsurePerkSelectionLoaded();
		if (_perkSelectionRoot == null || !GodotObject.IsInstanceValid(_perkSelectionRoot))
			return;

		if (_perkSelectionUI != null)
		{
			_perkSelectionUI.SetLocalStats(_trackedStats);
			_perkSelectionUI.RefreshPerks();
			if (!_perkSelectionUI.HasAvailablePerks)
			{
				HidePerkSelection();
				if (requiredByRound)
					CompleteRoundPerkSelection();
				return;
			}
		}

		// Control input order is tree-order based, not z-index. Keep the perk UI
		// as the last sibling so it reliably receives clicks on top of HUD layers.
		var overlayParent = _perkSelectionRoot.GetParent();
		if (overlayParent != null)
			overlayParent.MoveChild(_perkSelectionRoot, overlayParent.GetChildCount() - 1);

		_perkSelectionRoot.MoveToFront();
		_perkSelectionRoot.Visible = true;
		_perkSelectionActive = true;
		_player?.SetPauseControlsLocked(true);
		GetTree().Paused = true;
	}

	private void OnPerkChosen()
	{
		HidePerkSelection();
		if (_networkManager != null && _networkManager.IsRoundWaitingForPerks())
			CompleteRoundPerkSelection();
		_player?.SetPauseControlsLocked(false);
		GetTree().Paused = false;
	}

	private void CompleteRoundPerkSelection()
	{
		if (_roundPerkSelectionComplete)
			return;

		_roundPerkSelectionComplete = true;
		_networkManager?.NotifyLocalPerkSelectionComplete();
	}

	private void DestroyPerkSelection()
	{
		if (_perkSelectionUI != null && GodotObject.IsInstanceValid(_perkSelectionUI) &&
			_perkSelectionUI.IsConnected(nameof(PerkSelectionUI.PerkChosen), _perkChosenCallable))
			_perkSelectionUI.Disconnect(nameof(PerkSelectionUI.PerkChosen), _perkChosenCallable);

		if (_perkSelectionRoot != null && GodotObject.IsInstanceValid(_perkSelectionRoot))
			_perkSelectionRoot.QueueFree();

		_perkSelectionUI = null;
		_perkSelectionRoot = null;
		_perkSelectionActive = false;
	}

	private void HidePerkSelection()
	{
		if (_perkSelectionRoot != null && GodotObject.IsInstanceValid(_perkSelectionRoot))
			_perkSelectionRoot.Visible = false;

		_perkSelectionActive = false;
		_player?.SetPauseControlsLocked(false);
	}

	private void EnsurePerkSelectionLoaded()
	{
		if (_perkSelectionRoot != null && GodotObject.IsInstanceValid(_perkSelectionRoot))
			return;

		if (TryBindPerkSelectionFromScene())
			return;

		var packedScene = GD.Load<PackedScene>(PerkSelectionScenePath);
		if (packedScene == null)
		{
			GD.PrintErr($"GameHudPanel: failed to load perk selection scene at {PerkSelectionScenePath}");
			return;
		}

		var instance = packedScene.Instantiate<Control>();
		instance.ProcessMode = ProcessModeEnum.Always;
		instance.MouseFilter = Control.MouseFilterEnum.Stop;
		instance.Visible = false;

		_perkSelectionRoot = instance;
		_perkSelectionUI = instance as PerkSelectionUI;

		var overlayParent = GetParent() ?? this;
		overlayParent.AddChild(instance);

		if (_perkSelectionUI != null &&
			!_perkSelectionUI.IsConnected(nameof(PerkSelectionUI.PerkChosen), _perkChosenCallable))
			_perkSelectionUI.Connect(nameof(PerkSelectionUI.PerkChosen), _perkChosenCallable);
	}

	private bool TryBindPerkSelectionFromScene()
	{
		var local = GetNodeOrNull<Control>(_perkSelectionPath);
		if (local == null)
			local = GetParent()?.GetNodeOrNull<Control>(_perkSelectionPath);
		if (local == null)
			local = _player?.GetNodeOrNull<Control>(_perkSelectionPath);
		if (local == null)
			local = _player?.GetNodeOrNull<Control>("PerkSelection");

		if (local == null || !GodotObject.IsInstanceValid(local))
			return false;

		_perkSelectionRoot = local;
		_perkSelectionUI = local as PerkSelectionUI;
		_perkSelectionRoot.ProcessMode = ProcessModeEnum.Always;
		_perkSelectionRoot.MouseFilter = Control.MouseFilterEnum.Stop;
		_perkSelectionRoot.MoveToFront();
		_perkSelectionRoot.Visible = false;

		if (_perkSelectionUI != null &&
			!_perkSelectionUI.IsConnected(nameof(PerkSelectionUI.PerkChosen), _perkChosenCallable))
			_perkSelectionUI.Connect(nameof(PerkSelectionUI.PerkChosen), _perkChosenCallable);

		return true;
	}

	private bool IsPlayerDead()
	{
		return _trackedStats != null && GodotObject.IsInstanceValid(_trackedStats) && _trackedStats.CurrentHealth <= 0;
	}

	private void OnHealthChanged(float currentHealth, float maxHealth)
	{
		RefreshHealthDisplay();
	}

	private void OnStaminaChanged(float currentStamina, float maxStamina)
	{
		RefreshStaminaDisplay();
	}

	private void OnAmmoChanged(int currentAmmo, int maxAmmo, bool isReloading, float reloadRemaining)
	{
		RefreshAmmoDisplay();
	}

	public void RefreshHealthDisplay()
	{
		if (_trackedStats == null || !GodotObject.IsInstanceValid(_trackedStats))
		{
			ShowFallback();
			return;
		}

		var currentHealth = _trackedStats.CurrentHealth;
		var maxHealth = _trackedStats.MaxHealth;
		var displayCurrentHealth = currentHealth.ToString("0.##");
		var displayMaxHealth = maxHealth.ToString("0.##");

		if (_healthBar != null)
		{
			_healthBar.MinValue = 0;
			_healthBar.MaxValue = maxHealth;
			_healthBar.Value = currentHealth;
		}

		if (_healthValueLabel != null)
			_healthValueLabel.Text = $"{displayCurrentHealth}/{displayMaxHealth}";
	}

	public void RefreshStaminaDisplay()
	{
		if (_trackedStats == null || !GodotObject.IsInstanceValid(_trackedStats))
		{
			ShowFallback();
			return;
		}

		var currentStamina = _trackedStats.CurrentStamina;
		var maxStamina = _trackedStats.MaxStamina;
		var displayStamina = Mathf.RoundToInt(currentStamina);
		var displayMaxStamina = Mathf.RoundToInt(maxStamina);

		if (_staminaBar != null)
		{
			_staminaBar.MinValue = 0;
			_staminaBar.MaxValue = maxStamina;
			_staminaBar.Value = currentStamina;
		}

		if (_staminaValueLabel != null)
			_staminaValueLabel.Text = $"{displayStamina}/{displayMaxStamina}";
	}

	public void RefreshAmmoDisplay()
	{
		if (_ammoBar == null || _ammoValueLabel == null)
			return;

		if (_trackedStats == null || !GodotObject.IsInstanceValid(_trackedStats))
		{
			_ammoBar.MinValue = 0;
			_ammoBar.MaxValue = 1;
			_ammoBar.Value = 0;
			_ammoValueLabel.Text = "--/--";
			return;
		}

		if (!_trackedStats.HasAmmoSystem)
		{
			_ammoBar.MinValue = 0;
			_ammoBar.MaxValue = 1;
			_ammoBar.Value = 1;
			_ammoValueLabel.Text = "INF";
			return;
		}

		var currentAmmo = _trackedStats.CurrentAmmo;
		var maxAmmo = _trackedStats.MaxAmmo;
		var reloadRemaining = _trackedStats.ReloadRemaining;

		_ammoBar.MinValue = 0;
		_ammoBar.MaxValue = maxAmmo;
		_ammoBar.Value = Mathf.Clamp(currentAmmo, 0, maxAmmo);

		if (_trackedStats.IsReloading)
			_ammoValueLabel.Text = $"{currentAmmo}/{maxAmmo}  R:{reloadRemaining:0.0}s";
		else
			_ammoValueLabel.Text = $"{currentAmmo}/{maxAmmo}";
	}

	public void ShowFallback()
	{
		if (_healthBar != null)
			_healthBar.Value = 0;
		if (_healthValueLabel != null)
			_healthValueLabel.Text = "--/--";
		if (_staminaBar != null)
			_staminaBar.Value = 0;
		if (_staminaValueLabel != null)
			_staminaValueLabel.Text = "--/--";
	}
}
