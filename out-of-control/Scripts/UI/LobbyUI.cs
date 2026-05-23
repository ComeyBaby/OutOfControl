using Godot;

public partial class LobbyUI : Control
{
	private const string NetworkManagerNodeName = "NetworkManager";
	private const string DefaultStatus = "Host or join a room to begin.";

	[Export] private Button _readyButton;
	[Export] private Button _startButton;
	[Export] private Button _backButton;
	[Export] private LineEdit _playerNameField;
	[Export] private LineEdit _hostAddressField;
	[Export] private Label _statusLabel;
	[Export] private LineEdit _roomCodeField;
	[Export] private OptionButton _weaponDropdown;
	[Export] private Button _hostButton;
	[Export] private Button _joinButton;
	[Export] private VBoxContainer _playerList;
	[Export(PropertyHint.File, "*.tscn")] public string MainMenuScenePath = "res://Scenes/UI/MainMenu.tscn";

	private NetworkManager _networkManager;
	private Callable _statusChangedCallable;
	private Callable _playersChangedCallable;
	private string _lastStatus = DefaultStatus;
	private string _displayedHostIp = "";
	private string _displayedRoomCode = "";
	private string CurrentPlayerName => _playerNameField?.Text ?? "";
	private string CurrentRoomCode => _roomCodeField?.Text ?? "";
	private string CurrentHostAddress => _hostAddressField?.Text ?? "";

	public override void _Ready()
	{
		_networkManager = GetTree().Root.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName)
			?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName);
		_statusChangedCallable = new Callable(this, nameof(OnStatusChanged));
		_playersChangedCallable = new Callable(this, nameof(UpdatePlayerList));

		_readyButton.Pressed += OnReadyPressed;
		_startButton.Pressed += OnStartPressed;
		_backButton.Pressed += OnBackPressed;
		if (_hostAddressField != null)
			_hostAddressField.GuiInput += OnHostAddressFieldGuiInput;
		if (_roomCodeField != null)
			_roomCodeField.GuiInput += OnRoomCodeFieldGuiInput;

		if (_networkManager != null && _playerNameField != null)
			_playerNameField.Text = _networkManager.GetLocalPlayerName();

		_playerNameField.TextChanged += OnPlayerNameChanged;
		_weaponDropdown.ItemSelected += OnWeaponSelected;
		SyncWeaponDropdownSelection();

		_hostButton.Pressed += () => OnHostPressed(CurrentRoomCode);
		_joinButton.Pressed += () => OnJoinPressed(CurrentRoomCode, CurrentHostAddress);

		if (_networkManager != null)
		{
			if (!_networkManager.IsConnected("StatusChanged", _statusChangedCallable))
				_networkManager.Connect("StatusChanged", _statusChangedCallable);
			if (!_networkManager.IsConnected("PlayersChanged", _playersChangedCallable))
				_networkManager.Connect("PlayersChanged", _playersChangedCallable);
		}

		UpdatePlayerList();
		UiNavigationHelper.FocusControl(_hostButton);
	}

	public override void _ExitTree()
	{
		if (_networkManager == null)
			return;

		if (_networkManager.IsConnected("StatusChanged", _statusChangedCallable))
			_networkManager.Disconnect("StatusChanged", _statusChangedCallable);
		if (_networkManager.IsConnected("PlayersChanged", _playersChangedCallable))
			_networkManager.Disconnect("PlayersChanged", _playersChangedCallable);
	}

	private void OnWeaponSelected(long index)
	{
		var itemIndex = (int)index;
		if (itemIndex < 0 || itemIndex >= _weaponDropdown.GetItemCount())
			return;

		var weapon = _weaponDropdown.GetItemText(itemIndex);
		if (string.IsNullOrWhiteSpace(weapon))
			return;

		_networkManager?.SetLocalWeapon(weapon);
		UpdatePlayerList();
	}

	private void SyncWeaponDropdownSelection()
	{
		if (_weaponDropdown == null || _weaponDropdown.GetItemCount() == 0)
			return;

		var targetWeapon = _networkManager?.GetLocalWeapon();
		if (string.IsNullOrWhiteSpace(targetWeapon))
			targetWeapon = _weaponDropdown.GetItemText(Mathf.Min(1, _weaponDropdown.GetItemCount() - 1));

		for (int i = 0; i < _weaponDropdown.GetItemCount(); i++)
		{
			if (_weaponDropdown.GetItemText(i) == targetWeapon)
			{
				_weaponDropdown.Selected = i;
				return;
			}
		}
	}

	private void OnReadyPressed()
	{
		var nm = _networkManager;
		if (nm == null || !nm.HasMultiplayerPeer())
			return;

		var localId = nm.GetLocalPeerIdOrZero();
		if (localId <= 0)
			return;

		var currentlyReady = nm.IsPlayerReady(localId);
		nm.SetReady(!currentlyReady);
		GameAudio.PlayReady(this);
		UpdatePlayerList();
	}

	private void OnStartPressed()
	{
		var nm = _networkManager;
		if (nm == null || !nm.HasMultiplayerPeer())
			return;

		nm.TryStartGame();
	}

	private void OnHostPressed(string roomCode)
	{
		var nm = _networkManager;
		if (nm == null)
			return;

		nm.SetLocalPlayerName(CurrentPlayerName);
		nm.HostRoom(roomCode);

		if (!nm.IsHosting())
		{
			ApplyLobbyUiState();
			return;
		}

		UpdatePlayerList();
	}

	private void OnJoinPressed(string roomCode, string hostAddress)
	{
		var nm = _networkManager;
		if (nm == null)
			return;

		nm.SetLocalPlayerName(CurrentPlayerName);
		nm.JoinRoom(roomCode, hostAddress);
		ApplyLobbyUiState();
	}

	private void OnBackPressed()
	{
		if (_networkManager != null)
		{
			_networkManager.ReturnToMainMenu();
			return;
		}

		GetTree().ChangeSceneToFile(MainMenuScenePath);
	}

	private void OnPlayerNameChanged(string text)
	{
		_networkManager?.SetLocalPlayerName(text);
		UpdatePlayerList();
	}

	private void OnStatusChanged(string status)
	{
		_lastStatus = status;
		UpdatePlayerList();
	}

	private void SetHostedInfo(string hostIp, string roomCode, bool connectionFieldsEditable, bool copyEnabled)
	{
		_displayedHostIp = hostIp ?? "";
		_displayedRoomCode = roomCode ?? "";

		if (_hostAddressField != null)
			ApplySharedFieldState(_hostAddressField, _displayedHostIp, connectionFieldsEditable, copyEnabled);
		if (_roomCodeField != null)
			ApplySharedFieldState(_roomCodeField, _displayedRoomCode, connectionFieldsEditable, copyEnabled);
	}

	private static void ApplySharedFieldState(LineEdit field, string text, bool editable, bool copyEnabled)
	{
		field.Text = text ?? "";
		field.Editable = editable;
		field.TooltipText = copyEnabled ? "Click to copy" : "";
		field.MouseDefaultCursorShape = copyEnabled ? CursorShape.PointingHand : CursorShape.Ibeam;
	}

	private void OnHostAddressFieldGuiInput(InputEvent inputEvent)
	{
		TryCopyHostedShareField(inputEvent, _hostAddressField, _displayedHostIp);
	}

	private void OnRoomCodeFieldGuiInput(InputEvent inputEvent)
	{
		TryCopyHostedShareField(inputEvent, _roomCodeField, _displayedRoomCode);
	}

	private void TryCopyHostedShareField(InputEvent inputEvent, LineEdit field, string value)
	{
		if (field == null || _networkManager == null)
			return;

		var state = _networkManager.GetLobbyUiState();
		if (!state.ShowCopyToClipboardOnConnectionFields || string.IsNullOrWhiteSpace(value))
			return;
		if (inputEvent is not InputEventMouseButton mouseButton
			|| mouseButton.ButtonIndex != MouseButton.Left
			|| !mouseButton.Pressed)
			return;

		DisplayServer.ClipboardSet(value);
		field.AcceptEvent();
	}

	private void UpdatePlayerList()
	{
		if (_playerList == null || !GodotObject.IsInstanceValid(_playerList))
			return;

		for (int i = _playerList.GetChildCount() - 1; i >= 0; i--)
			_playerList.GetChild(i)?.QueueFree();

		if (_networkManager == null)
			return;

		ApplyLobbyUiState();
		foreach (var peerId in _networkManager.GetLobbyPeerIds())
		{
			var entry = new Label();
			var displayName = _networkManager.GetPlayerName(peerId);
			var weapon = _networkManager.GetPlayerWeapon(peerId);
			var readiness = _networkManager.IsPlayerReady(peerId) ? "Ready" : "Not Ready";
			entry.Text = $"{displayName} - {weapon} - {readiness}";
			_playerList.AddChild(entry);
		}
	}

	private void ApplyLobbyUiState()
	{
		if (_networkManager == null)
			return;

		var state = _networkManager.GetLobbyUiState();
		if (_hostButton != null)
			_hostButton.Disabled = !state.CanHost;
		if (_joinButton != null)
			_joinButton.Disabled = !state.CanJoin;
		if (_weaponDropdown != null)
			_weaponDropdown.Disabled = state.Role == NetworkManager.LobbyUiRole.Connecting;

		if (_readyButton != null)
		{
			_readyButton.Disabled = !state.CanToggleReady;
			if (!state.CanToggleReady)
			{
				_readyButton.Text = "Ready";
			}
			else
			{
				var localId = _networkManager.GetLocalPeerIdOrZero();
				_readyButton.Text = localId > 0 && _networkManager.IsPlayerReady(localId)
					? "Unready"
					: "Ready";
			}
		}

		if (_startButton != null)
		{
			_startButton.Disabled = !state.CanStartMatch;
			_startButton.Text = state.Role == NetworkManager.LobbyUiRole.Hosting
				? "Start"
				: "Host Starts";
		}

		if (state.Role == NetworkManager.LobbyUiRole.Hosting)
		{
			SetHostedInfo(
				_networkManager.GetShareableHostAddress(),
				_networkManager.DefaultRoomCode,
				state.CanEditConnectionFields,
				state.ShowCopyToClipboardOnConnectionFields);
		}
		else
		{
			var currentHost = _hostAddressField?.Text ?? _displayedHostIp;
			var currentRoom = _roomCodeField?.Text ?? _displayedRoomCode;
			SetHostedInfo(currentHost, currentRoom, state.CanEditConnectionFields, state.ShowCopyToClipboardOnConnectionFields);
		}

		if (_statusLabel != null)
			_statusLabel.Text = string.IsNullOrWhiteSpace(_lastStatus) ? state.HintText : _lastStatus;
	}
}
