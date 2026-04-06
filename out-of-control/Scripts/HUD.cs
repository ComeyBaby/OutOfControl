using Godot;
using System.Collections.Generic;

public partial class HUD : CanvasLayer
{
	private ProgressBar _healthBar;
	private Label _healthValueLabel;
	private ProgressBar _staminaBar;
	private Label _staminaValueLabel;
	private ProgressBar _armorBar;
	private Label _armorValueLabel;
	private Control _crosshair;
	private Control _pauseMenu;
	private Control _spectatePanel;
	private Button _resumeButton;
	private Button _menuButton;
	private Button _spectatePrevButton;
	private Button _spectateNextButton;
	private NetworkManager _networkManager;
	private PlayerController _trackedPlayer;
	private PlayerStats _trackedStats;
	private Callable _healthChangedCallable;
	private Callable _staminaChangedCallable;
	private bool _spectating;
	private long _spectateTargetPeerId = -1;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		TryBindNetworkManager();
		_healthBar = GetNode<ProgressBar>("EdgeUI/LeftMeters/Health/Margin/HealthBar");
		_healthValueLabel = GetNode<Label>("EdgeUI/LeftMeters/Health/Margin/HealthBar/HealthValueLabel");
		_staminaBar = GetNode<ProgressBar>("EdgeUI/RightMeters/Stamina/Margin/StaminaBar");
		_staminaValueLabel = GetNode<Label>("EdgeUI/RightMeters/Stamina/Margin/StaminaBar/StaminaValueLabel");
		_armorBar = GetNode<ProgressBar>("EdgeUI/LeftMeters/Armor/Margin/ArmorBar");
		_armorValueLabel = GetNode<Label>("EdgeUI/LeftMeters/Armor/Margin/ArmorBar/ArmorValueLabel");
		_crosshair = GetNode<Control>("Crosshair");
		_pauseMenu = GetNode<Control>("PauseMenu");
		_spectatePanel = GetNode<Control>("EdgeUI/SpectatePanel");
		_resumeButton = GetNode<Button>("PauseMenu/Panel/Margin/VBox/ResumeButton");
		_menuButton = GetNode<Button>("PauseMenu/Panel/Margin/VBox/MenuButton");
		_spectatePrevButton = GetNode<Button>("EdgeUI/SpectatePanel/Margin/Controls/PrevButton");
		_spectateNextButton = GetNode<Button>("EdgeUI/SpectatePanel/Margin/Controls/NextButton");

		_healthChangedCallable = new Callable(this, nameof(OnHealthChanged));
		_staminaChangedCallable = new Callable(this, nameof(OnStaminaChanged));

		_resumeButton.Pressed += OnResumePressed;
		_menuButton.Pressed += OnMenuPressed;
		_spectatePrevButton.Pressed += OnSpectatePreviousPressed;
		_spectateNextButton.Pressed += OnSpectateNextPressed;
		_pauseMenu.Visible = false;
		_spectatePanel.Visible = false;

		GetTree().SceneChanged += OnSceneChanged;
		TryBindToLocalPlayer();
	}

	public override void _ExitTree()
	{
		if (GetTree() != null)
			GetTree().SceneChanged -= OnSceneChanged;

		if (_networkManager != null)
		{
			var playersChangedCallable = new Callable(this, nameof(OnPlayersChanged));
			if (_networkManager.IsConnected("PlayersChanged", playersChangedCallable))
				_networkManager.Disconnect("PlayersChanged", playersChangedCallable);
		}

		UnbindFromStats();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (Input.IsActionJustPressed("pause"))
		{
			TogglePauseMenu();
		}
	}

	public override void _Process(double delta)
	{
		if (_networkManager == null)
			TryBindNetworkManager();

		if (_trackedStats == null || !GodotObject.IsInstanceValid(_trackedStats))
			TryBindToLocalPlayer();

		if (_spectating)
		{
			var target = GetSpectatePlayer(_spectateTargetPeerId);
			if (target == null)
				RefreshSpectateTargets();
		}
	}

	private void OnSceneChanged()
	{
		TryBindNetworkManager();
		TryBindToLocalPlayer();
	}

	private void TryBindNetworkManager()
	{
		var manager = GetTree().Root.GetNodeOrNull<NetworkManager>("NetworkManager")
			?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>("NetworkManager");

		if (manager == _networkManager)
			return;

		var playersChangedCallable = new Callable(this, nameof(OnPlayersChanged));
		if (_networkManager != null)
		{
			if (_networkManager.IsConnected("PlayersChanged", playersChangedCallable))
				_networkManager.Disconnect("PlayersChanged", playersChangedCallable);
		}

		_networkManager = manager;
		if (_networkManager != null && !_networkManager.IsConnected("PlayersChanged", playersChangedCallable))
			_networkManager.Connect("PlayersChanged", playersChangedCallable);
	}

	private void OnResumePressed()
	{
		ResumeGame();
	}

	private void TogglePauseMenu()
	{
		if (_pauseMenu != null && _pauseMenu.Visible)
		{
			ResumeGame();
			return;
		}

		PauseGame();
	}

	private void OnMenuPressed()
	{
		HidePauseOverlay();
		GetTree().Paused = false;
		GetTree().Root.GetNodeOrNull<NetworkManager>("NetworkManager")?.ReturnToMainMenu();
	}

	private void OnPlayersChanged()
	{
		RefreshSpectateTargets();
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
			_trackedPlayer = null;
			ShowFallback();
			return;
		}

		BindToPlayer(player);
	}

	private PlayerController FindLocalPlayer(Node root)
	{
		foreach (var child in root.GetChildren())
		{
			if (child is PlayerController player && HasLocalAuthority(player))
				return player;

			var nested = FindLocalPlayer(child);
			if (nested != null)
				return nested;
		}

		return null;
	}

	private bool HasLocalAuthority(PlayerController player)
	{
		return player != null && (Multiplayer.MultiplayerPeer == null || player.IsMultiplayerAuthority());
	}

	private void BindToPlayer(PlayerController player)
	{
		_trackedPlayer = player;
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
		if (!_trackedStats.IsConnected(nameof(PlayerStats.StaminaChanged), _staminaChangedCallable))
			_trackedStats.Connect(nameof(PlayerStats.StaminaChanged), _staminaChangedCallable);

		RefreshHealthDisplay();
		RefreshStaminaDisplay();
		RefreshSpectateTargets();
		if (_trackedStats.CurrentHealth <= 0)
			EnterSpectateMode();
	}

	private void EnterSpectateMode()
	{
		if (_trackedPlayer != null)
			_trackedPlayer.SetControlsEnabled(false);

		if (_spectating)
			return;

		_spectating = true;
		SetCrosshairVisible(false);

		if (_spectatePanel != null)
			_spectatePanel.Visible = true;

		Input.MouseMode = Input.MouseModeEnum.Visible;
		RefreshSpectateTargets();
	}

	private void OnSpectatePreviousPressed()
	{
		SelectSpectateTarget(-1);
	}

	private void OnSpectateNextPressed()
	{
		SelectSpectateTarget(1);
	}

	private void SelectSpectateTarget(int step)
	{
		var targets = GetSpectateTargets();
		if (targets.Count == 0)
			return;

		var currentIndex = targets.IndexOf(_spectateTargetPeerId);
		if (currentIndex < 0)
			currentIndex = 0;

		var nextIndex = (currentIndex + step) % targets.Count;
		if (nextIndex < 0)
			nextIndex += targets.Count;

		SetSpectateTarget(targets[nextIndex]);
	}

	private void SetSpectateTarget(long peerId)
	{
		var player = GetSpectatePlayer(peerId);
		if (player == null)
			return;

		_spectateTargetPeerId = peerId;
		var camera = player.GetViewCamera();
		if (camera != null)
			camera.Current = true;
	}

	private void RefreshSpectateTargets()
	{
		var targets = GetSpectateTargets();
		var hasTargets = targets.Count > 0;
		if (_spectatePrevButton != null)
			_spectatePrevButton.Disabled = !hasTargets;
		if (_spectateNextButton != null)
			_spectateNextButton.Disabled = !hasTargets;

		if (targets.Count == 0)
		{
			_spectateTargetPeerId = -1;
			return;
		}

		if (!_spectating)
			return;

		if (_spectateTargetPeerId >= 0 && targets.Contains(_spectateTargetPeerId))
			return;

		SetSpectateTarget(targets[0]);
	}

	private List<long> GetSpectateTargets()
	{
		var targets = new List<long>();
		if (_networkManager != null)
		{
			foreach (var peerId in _networkManager.GetSpawnedPlayerIds())
			{
				if (peerId == 0)
					continue;

				if (_trackedPlayer != null && GodotObject.IsInstanceValid(_trackedPlayer) && peerId == _trackedPlayer.GetMultiplayerAuthority())
					continue;

				var player = _networkManager.GetPlayer(peerId);
				if (player != null)
					targets.Add(peerId);
			}
		}

		targets.Sort();
		return targets;
	}

	private PlayerController GetSpectatePlayer(long peerId)
	{
		if (_networkManager == null)
			return null;

		var player = _networkManager.GetPlayer(peerId);
		if (player == null || !GodotObject.IsInstanceValid(player))
			return null;

		return player;
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
		Input.MouseMode = _spectating ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
	}

	private void ShowPauseMenu()
	{
		SetCrosshairVisible(false);

		if (_pauseMenu != null)
			_pauseMenu.Visible = true;

		_resumeButton?.GrabFocus();
	}

	private void HidePauseOverlay()
	{
		if (_pauseMenu != null)
			_pauseMenu.Visible = false;

		if (!_spectating)
			SetCrosshairVisible(true);
	}

	private void SetCrosshairVisible(bool visible)
	{
		if (_crosshair != null)
			_crosshair.Visible = visible;
	}

	private void UnbindFromStats()
	{
		if (_trackedStats != null && GodotObject.IsInstanceValid(_trackedStats) && _trackedStats.IsConnected(nameof(PlayerStats.HealthChanged), _healthChangedCallable))
			_trackedStats.Disconnect(nameof(PlayerStats.HealthChanged), _healthChangedCallable);
		if (_trackedStats != null && GodotObject.IsInstanceValid(_trackedStats) && _trackedStats.IsConnected(nameof(PlayerStats.StaminaChanged), _staminaChangedCallable))
			_trackedStats.Disconnect(nameof(PlayerStats.StaminaChanged), _staminaChangedCallable);

		_trackedStats = null;
	}

	private void OnHealthChanged(int currentHealth, int maxHealth)
	{
		RefreshHealthDisplay();
		if (currentHealth <= 0)
			EnterSpectateMode();
	}

	private void OnStaminaChanged(float currentStamina, float maxStamina)
	{
		RefreshStaminaDisplay();
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

	private void RefreshStaminaDisplay()
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

		_staminaBar.MinValue = 0;
		_staminaBar.MaxValue = maxStamina;
		_staminaBar.Value = currentStamina;
		_staminaValueLabel.Text = $"{displayStamina}/{displayMaxStamina}";
	}

	private void ShowFallback()
	{

		_healthBar.Value = 0;
		_healthValueLabel.Text = "--/--";
		_staminaBar.Value = 0;
		_staminaValueLabel.Text = "--/--";

		_armorBar.Value = 0;
		_armorValueLabel.Text = "--/--";
	}
}
