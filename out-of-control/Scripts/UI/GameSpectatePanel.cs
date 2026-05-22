using Godot;
using System.Collections.Generic;

public partial class GameSpectatePanel : Control
{
	private const string NetworkManagerNodeName = "NetworkManager";
	private const float BindPollIntervalSeconds = 0.5f;

	[Export] private Button _prevButton;
	[Export] private Button _nextButton;
	[Export] private Label _statusLabel;
	[Export] private NodePath _crosshairPath = new("Crosshair");

	private PlayerController _player;
	private GameCrosshair _crosshair;
	private NetworkManager _networkManager;
	private PlayerStats _trackedStats;
	private Callable _playersChangedCallable;
	private Callable _healthChangedCallable;
	private Callable _sceneChangedCallable;
	private long _spectateTargetPeerId = -1;
	private bool _sceneChangedConnected;
	private float _bindPollAccumulator = 0.0f;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		Visible = false;

		_player = FindOwningPlayer();
		if (_player == null || !_player.IsMultiplayerAuthority())
		{
			ProcessMode = ProcessModeEnum.Disabled;
			return;
		}

		_crosshair = GetParent()?.GetNodeOrNull<GameCrosshair>(_crosshairPath);
		_playersChangedCallable = new Callable(this, nameof(OnPlayersChanged));
		_healthChangedCallable = new Callable(this, nameof(OnHealthChanged));
		_sceneChangedCallable = new Callable(this, nameof(OnSceneChanged));

		if (_prevButton != null)
			_prevButton.Pressed += OnPreviousPressed;
		if (_nextButton != null)
			_nextButton.Pressed += OnNextPressed;

		TryBindNetworkManager();
		EnsureStatsBound();
		if (GetTree() != null && !GetTree().IsConnected("scene_changed", _sceneChangedCallable))
		{
			GetTree().Connect("scene_changed", _sceneChangedCallable);
			_sceneChangedConnected = true;
		}
	}

	public override void _ExitTree()
	{
		if (_sceneChangedConnected && GetTree() != null &&
			GetTree().IsConnected("scene_changed", _sceneChangedCallable))
			GetTree().Disconnect("scene_changed", _sceneChangedCallable);
		_sceneChangedConnected = false;

		if (_networkManager != null && _networkManager.IsConnected("PlayersChanged", _playersChangedCallable))
			_networkManager.Disconnect("PlayersChanged", _playersChangedCallable);

		UnbindFromStats();
	}

	public override void _Process(double delta)
	{
		if (_player == null || !_player.IsMultiplayerAuthority())
			return;

		_bindPollAccumulator += (float)delta;
		if (_bindPollAccumulator < BindPollIntervalSeconds)
			return;

		_bindPollAccumulator = 0.0f;
		if (_networkManager == null || !GodotObject.IsInstanceValid(_networkManager))
			TryBindNetworkManager();
		EnsureStatsBound();
	}

	private void OnSceneChanged()
	{
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

		_networkManager = manager;
		if (_networkManager != null && !_networkManager.IsConnected("PlayersChanged", _playersChangedCallable))
			_networkManager.Connect("PlayersChanged", _playersChangedCallable);
	}

	private void EnsureStatsBound()
	{
		var stats = _player?.GetStats();
		if (stats == null || !GodotObject.IsInstanceValid(stats))
		{
			UnbindFromStats();
			return;
		}

		if (_trackedStats == stats)
			return;

		UnbindFromStats();
		_trackedStats = stats;
		if (!_trackedStats.IsConnected(nameof(PlayerStats.HealthChanged), _healthChangedCallable))
			_trackedStats.Connect(nameof(PlayerStats.HealthChanged), _healthChangedCallable);
	}

	private void UnbindFromStats()
	{
		if (_trackedStats != null && GodotObject.IsInstanceValid(_trackedStats) &&
			_trackedStats.IsConnected(nameof(PlayerStats.HealthChanged), _healthChangedCallable))
			_trackedStats.Disconnect(nameof(PlayerStats.HealthChanged), _healthChangedCallable);
		_trackedStats = null;
	}

	private void OnHealthChanged(float currentHealth, float maxHealth)
	{
		RefreshSpectatePanel();
	}

	private void OnPlayersChanged()
	{
		RefreshSpectatePanel();
		RefreshSpectateTargets();
	}

	private bool IsPlayerDead()
	{
		return _trackedStats != null && GodotObject.IsInstanceValid(_trackedStats) && _trackedStats.CurrentHealth <= 0;
	}

	private void RefreshSpectatePanel()
	{
		if (!IsPlayerDead())
		{
			if (_player != null)
			{
				_player.SetControlsEnabled(true);
				var ownCamera = _player.GetViewCamera();
				if (ownCamera != null)
					ownCamera.Current = true;
			}

			_crosshair?.SetReticleVisible(true);
			if (_statusLabel != null)
				_statusLabel.Text = "";
			Visible = false;
			_spectateTargetPeerId = -1;
			return;
		}

		if (_player != null)
		{
			_player.SetControlsEnabled(false);
			var ownCamera = _player.GetViewCamera();
			if (ownCamera != null)
				ownCamera.Current = false;
		}

		_crosshair?.SetReticleVisible(false);
		Visible = true;
		RefreshSpectateTargets();
		UpdateStatusText();
	}

	private void OnPreviousPressed()
	{
		SelectSpectateTarget(-1);
	}

	private void OnNextPressed()
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

	private void RefreshSpectateTargets()
	{
		var targets = GetSpectateTargets();
		var hasTargets = targets.Count > 0;
		if (_prevButton != null)
			_prevButton.Disabled = !hasTargets;
		if (_nextButton != null)
			_nextButton.Disabled = !hasTargets;

		if (!hasTargets)
		{
			_spectateTargetPeerId = -1;
			return;
		}

		if (_spectateTargetPeerId >= 0 && targets.Contains(_spectateTargetPeerId))
			return;

		SetSpectateTarget(targets[0]);
	}

	private void SetSpectateTarget(long peerId)
	{
		if (_networkManager == null)
			return;

		var player = _networkManager.GetPlayer(peerId);
		if (player == null || !GodotObject.IsInstanceValid(player))
			return;

		_spectateTargetPeerId = peerId;
		var camera = player.GetViewCamera();
		if (camera != null)
			camera.Current = true;
		UpdateStatusText();
	}

	private void UpdateStatusText()
	{
		if (_statusLabel == null)
			return;

		if (_networkManager == null || _spectateTargetPeerId <= 0)
		{
			_statusLabel.Text = "Spectating - waiting for a live player.";
			return;
		}

		var targetName = _networkManager.GetPlayerName(_spectateTargetPeerId);
		_statusLabel.Text = $"Spectating {targetName}. You will respawn next round.";
	}

	private List<long> GetSpectateTargets()
	{
		var targets = new List<long>();
		if (_networkManager != null && _player != null)
		{
			foreach (var peerId in _networkManager.GetSpawnedPlayerIds())
			{
				if (peerId == 0 || peerId == _player.GetMultiplayerAuthority())
					continue;

				var player = _networkManager.GetPlayer(peerId);
				if (player != null)
					targets.Add(peerId);
			}
		}

		targets.Sort();
		return targets;
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
