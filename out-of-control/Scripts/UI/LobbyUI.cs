using Godot;

public partial class LobbyUI : Control
{
    private const string NetworkManagerNodeName = "NetworkManager";

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
    [Export(PropertyHint.File, "*.tscn")] public string MainMenuScenePath;

    private NetworkManager _networkManager;
    private Callable _statusChangedCallable;
    private Callable _playersChangedCallable;
    private string _lastStatus = "Host or join a room to begin.";
    private string _displayedHostIp = "";
    private string _displayedRoomCode = "";

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

        if (_networkManager != null)
        {
            _playerNameField.Text = _networkManager.GetLocalPlayerName();
        }

        _playerNameField.TextChanged += OnPlayerNameChanged;
        _weaponDropdown.ItemSelected += OnWeaponSelected;
        SyncWeaponDropdownSelection();

        _hostButton.Pressed += () => OnHostPressed(_roomCodeField.Text);
        _joinButton.Pressed += () => OnJoinPressed(_roomCodeField.Text, _hostAddressField.Text);

        if (_networkManager != null)
        {
            if (!_networkManager.IsConnected("StatusChanged", _statusChangedCallable))
                _networkManager.Connect("StatusChanged", _statusChangedCallable);
            if (!_networkManager.IsConnected("PlayersChanged", _playersChangedCallable))
                _networkManager.Connect("PlayersChanged", _playersChangedCallable);
        }

        UpdatePlayerList();
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
        int itemIndex = (int)index;
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
        if (_weaponDropdown == null)
            return;

        var targetWeapon = _networkManager?.GetLocalWeapon();
        if (string.IsNullOrWhiteSpace(targetWeapon))
            targetWeapon = _weaponDropdown.GetItemText(1);

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
        if (nm == null || !nm.HasMultiplayerPeer()) return;

        int id = (int)nm.GetLocalPeerIdOrZero();
        if (id <= 0)
            return;
        bool currently = nm.IsPlayerReady(id);
        nm.SetReady(!currently);
        UpdatePlayerList();
    }

    private void OnStartPressed()
    {
        var nm = _networkManager;
        if (nm == null || !nm.HasMultiplayerPeer()) return;

        nm.TryStartGame();
    }

    private void OnHostPressed(string room)
    {
        var nm = _networkManager;
        if (nm == null) return;

        nm.SetLocalPlayerName(_playerNameField.Text);
        nm.HostRoom(room);

        if (!nm.IsHosting())
        {
            SetHostedInfo("", "");
            return;
        }

        SetHostedInfo(nm.GetShareableHostAddress(), nm.DefaultRoomCode);
        UpdatePlayerList();
    }

    private void OnJoinPressed(string room, string hostAddress)
    {
        var nm = _networkManager;
        if (nm == null) return;

        nm.SetLocalPlayerName(_playerNameField.Text);
        nm.JoinRoom(room, hostAddress);
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
        if (_statusLabel != null)
            _statusLabel.Text = status;

        if (_networkManager != null)
        {
            if (_networkManager.IsHosting())
                SetHostedInfo(_networkManager.GetShareableHostAddress(), _displayedRoomCode);
            else
                SetHostedInfo("", "");
        }

        UpdatePlayerList();
    }

    private void SetHostedInfo(string hostIp, string roomCode)
    {
        _displayedHostIp = hostIp ?? "";
        _displayedRoomCode = roomCode ?? "";

        bool isHosting = _networkManager != null && _networkManager.IsHosting();

        if (_hostAddressField != null)
        {
            _hostAddressField.Text = _displayedHostIp;
            _hostAddressField.Editable = !isHosting;
            if (isHosting)
            {
                _hostAddressField.TooltipText = "Click to copy";
                _hostAddressField.MouseDefaultCursorShape = CursorShape.PointingHand;
            }
            else
            {
                _hostAddressField.TooltipText = "";
                _hostAddressField.MouseDefaultCursorShape = CursorShape.Ibeam;
            }
        }

        if (_roomCodeField != null)
        {
            _roomCodeField.Text = _displayedRoomCode;
            _roomCodeField.Editable = !isHosting;
            if (isHosting)
            {
                _roomCodeField.TooltipText = "Click to copy";
                _roomCodeField.MouseDefaultCursorShape = CursorShape.PointingHand;
            }
            else
            {
                _roomCodeField.TooltipText = "";
                _roomCodeField.MouseDefaultCursorShape = CursorShape.Ibeam;
            }
        }
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
        if (field == null || _networkManager == null || !_networkManager.IsHosting())
            return;
        if (string.IsNullOrWhiteSpace(value))
            return;
        if (inputEvent is not InputEventMouseButton mouseButton ||
            mouseButton.ButtonIndex != MouseButton.Left ||
            !mouseButton.Pressed)
            return;

        DisplayServer.ClipboardSet(value);
        field.AcceptEvent();
    }

    private void UpdatePlayerList()
    {
        if (_playerList == null || !GodotObject.IsInstanceValid(_playerList))
            return;

        for (int i = _playerList.GetChildCount() - 1; i >= 0; i--)
        {
            var child = _playerList.GetChild(i) as Node;
            child?.QueueFree();
        }

        if (_networkManager == null)
            return;

        var hasPeer = _networkManager.HasMultiplayerPeer();
        if (_readyButton != null)
        {
            _readyButton.Disabled = !hasPeer;
            if (hasPeer)
            {
                var localId = _networkManager.GetLocalPeerIdOrZero();
                _readyButton.Text = localId > 0 && _networkManager.IsPlayerReady(localId) ? "Unready" : "Ready";
            }
        }

        if (_startButton != null)
        {
            var isHost = _networkManager.IsHosting();
            _startButton.Disabled = !isHost || !_networkManager.IsEveryoneReady();
            _startButton.Text = isHost ? "Start" : "Host Starts";
        }

        if (_statusLabel != null && string.IsNullOrWhiteSpace(_statusLabel.Text))
            _statusLabel.Text = _lastStatus;

        foreach (var peerId in _networkManager.GetLobbyPeerIds())
        {
            var lbl = new Label();
            var displayName = _networkManager.GetPlayerName(peerId);
            var weapon = _networkManager.GetPlayerWeapon(peerId);
            lbl.Text = $"{displayName} - {weapon} - {(_networkManager.IsPlayerReady(peerId) ? "Ready" : "Not Ready")}";
            _playerList.AddChild(lbl);
        }
    }
}
