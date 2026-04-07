using Godot;

public partial class LobbyUI : Control
{
    private const string NetworkManagerNodeName = "NetworkManager";

    [Export] private Button _readyButton;
    [Export] private Button _startButton;
    [Export] private Button _backButton;
    [Export] private LineEdit _playerNameField;
    [Export] private LineEdit _hostAddressField;
    [Export] private Label _hostIpLabel;
    [Export] private LineEdit _roomCodeField;
    [Export] private OptionButton _weaponDropdown;
    [Export] private Button _hostButton;
    [Export] private Button _joinButton;
    [Export] private VBoxContainer _playerList;
    [Export(PropertyHint.File, "*.tscn")] public string MainMenuScenePath;

    private NetworkManager _networkManager;

    public override void _Ready()
    {
        _networkManager = GetTree().Root.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName)
            ?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName);

        _readyButton.Pressed += OnReadyPressed;
        _startButton.Pressed += OnStartPressed;
        _backButton.Pressed += OnBackPressed;

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
            _networkManager.Connect("StatusChanged", new Callable(this, nameof(OnStatusChanged)));
            _networkManager.Connect("PlayersChanged", new Callable(this, nameof(UpdatePlayerList)));
        }

        UpdatePlayerList();
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
        if (nm == null) return;

        int id = (int)Multiplayer.GetUniqueId();
        bool currently = nm.IsPlayerReady(id);
        nm.SetReady(!currently);
        UpdatePlayerList();
    }

    private void OnStartPressed()
    {
        var nm = _networkManager;
        if (nm == null) return;

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
            _hostIpLabel.Text = "Host IP: not hosting";
            return;
        }

        _hostIpLabel.Text = $"Host IP: {nm.GetShareableHostAddress()}";
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
        if (_networkManager != null && _hostIpLabel != null)
        {
            if (_networkManager.IsHosting())
                _hostIpLabel.Text = $"Host IP: {_networkManager.GetShareableHostAddress()}";
            else
                _hostIpLabel.Text = "Host IP: not hosting";
        }

        UpdatePlayerList();
    }

    private void UpdatePlayerList()
    {
        for (int i = _playerList.GetChildCount() - 1; i >= 0; i--)
        {
            var child = _playerList.GetChild(i) as Node;
            child?.QueueFree();
        }

        if (_networkManager == null)
            return;

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
