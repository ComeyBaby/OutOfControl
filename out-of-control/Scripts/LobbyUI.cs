using Godot;

public partial class LobbyUI : Control
{
    private NetworkManager _networkManager;
    private LineEdit _playerNameField;
    private LineEdit _hostAddressField;
    private LineEdit _roomCodeField;
    private Label _hostIpLabel;

    public override void _Ready()
    {
        // NetworkManager is reparented to root to persist across scenes.
        _networkManager = GetTree().Root.GetNodeOrNull<NetworkManager>("NetworkManager")
            ?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>("NetworkManager");

        GetNode<Button>("Panel/Margin/VBox/Buttons/ReadyButton").Pressed += OnReadyPressed;
        GetNode<Button>("Panel/Margin/VBox/Buttons/StartButton").Pressed += OnStartPressed;
        GetNode<Button>("Panel/Margin/VBox/HostJoinButtons/BackButton").Pressed += OnBackPressed;

        _playerNameField = GetNode<LineEdit>("Panel/Margin/VBox/PlayerName");
        _hostAddressField = GetNode<LineEdit>("Panel/Margin/VBox/HostAddress");
        _hostIpLabel = GetNode<Label>("Panel/Margin/VBox/HostIpLabel");
        _roomCodeField = GetNode<LineEdit>("Panel/Margin/VBox/RoomCode");
        var hostBtn = GetNode<Button>("Panel/Margin/VBox/HostJoinButtons/HostButton");
        var joinBtn = GetNode<Button>("Panel/Margin/VBox/HostJoinButtons/JoinButton");

        if (_networkManager != null)
        {
            _playerNameField.Text = _networkManager.GetLocalPlayerName();
        }

        _hostIpLabel.Text = "Host IP: not hosting";
        _playerNameField.TextChanged += OnPlayerNameChanged;

        hostBtn.Pressed += () =>
        {
            GD.Print($"LobbyUI: Host pressed (room={_roomCodeField.Text})");
            OnHostPressed(_roomCodeField.Text);
        };

        joinBtn.Pressed += () =>
        {
            GD.Print($"LobbyUI: Join pressed (room={_roomCodeField.Text}, host={_hostAddressField.Text})");
            OnJoinPressed(_roomCodeField.Text, _hostAddressField.Text);
        };

        if (_networkManager != null)
        {
            _networkManager.Connect("StatusChanged", new Callable(this, nameof(OnStatusChanged)));
            _networkManager.Connect("PlayersChanged", new Callable(this, nameof(UpdatePlayerList)));
        }

        UpdatePlayerList();
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
        GD.Print($"LobbyUI: calling NetworkManager.HostRoom({room})");
        nm.HostRoom(room);

        if (!nm.IsHosting())
        {
            _hostIpLabel.Text = "Host IP: not hosting";
            GD.Print("LobbyUI: Host failed; skipping ready list setup.");
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
        GD.Print($"LobbyUI: calling NetworkManager.JoinRoom({room}, {hostAddress})");
        nm.JoinRoom(room, hostAddress);
    }

    private void OnBackPressed()
    {
        var nm = _networkManager ?? GetTree().Root.GetNodeOrNull<NetworkManager>("NetworkManager");
        if (nm != null)
        {
            nm.ReturnToMainMenu();
            return;
        }

        GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
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
        var list = GetNode<VBoxContainer>("Panel/Margin/VBox/PlayerList");
        for (int i = list.GetChildCount() - 1; i >= 0; i--)
        {
            var child = list.GetChild(i) as Node;
            child?.QueueFree();
        }

        if (_networkManager == null)
            return;

        foreach (var peerId in _networkManager.GetLobbyPeerIds())
        {
            var lbl = new Label();
            var displayName = _networkManager.GetPlayerName(peerId);
            lbl.Text = $"{displayName} - {(_networkManager.IsPlayerReady(peerId) ? "Ready" : "Not Ready")}";
            list.AddChild(lbl);
        }
    }
}
