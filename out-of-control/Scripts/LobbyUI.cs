using Godot;

public partial class LobbyUI : Control
{
    private NetworkManager _networkManager;
    private LineEdit _playerNameField;

    public override void _Ready()
    {
        // NetworkManager is reparented to root to persist across scenes.
        _networkManager = GetTree().Root.GetNodeOrNull<NetworkManager>("NetworkManager")
            ?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>("NetworkManager");

        GetNode<Button>("Panel/Margin/VBox/Buttons/ReadyButton").Pressed += OnReadyPressed;
        GetNode<Button>("Panel/Margin/VBox/Buttons/StartButton").Pressed += OnStartPressed;
        GetNode<Button>("Panel/Margin/VBox/HostJoinButtons/BackButton").Pressed += OnBackPressed;

        _playerNameField = GetNode<LineEdit>("Panel/Margin/VBox/PlayerName");
        var roomCode = GetNode<LineEdit>("Panel/Margin/VBox/RoomCode");
        var password = GetNode<LineEdit>("Panel/Margin/VBox/Password");
        var hostBtn = GetNode<Button>("Panel/Margin/VBox/HostJoinButtons/HostButton");
        var joinBtn = GetNode<Button>("Panel/Margin/VBox/HostJoinButtons/JoinButton");

        if (_networkManager != null)
        {
            _playerNameField.Text = _networkManager.GetLocalPlayerName();
        }
        _playerNameField.TextChanged += OnPlayerNameChanged;

        hostBtn.Pressed += () =>
        {
            GD.Print($"LobbyUI: Host pressed (room={roomCode.Text}, pass={password.Text})");
            OnHostPressed(roomCode.Text, password.Text);
        };

        joinBtn.Pressed += () =>
        {
            GD.Print($"LobbyUI: Join pressed (room={roomCode.Text}, pass={password.Text})");
            OnJoinPressed(roomCode.Text);
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

    private void OnHostPressed(string room, string password)
    {
        var nm = _networkManager;
        if (nm == null) return;

        nm.SetLocalPlayerName(_playerNameField.Text);
        GD.Print($"LobbyUI: calling NetworkManager.HostRoom({room}, {password})");
        nm.HostRoom(room, password);

        if (!nm.IsHosting())
        {
            GD.Print("LobbyUI: Host failed; skipping ready list setup.");
            return;
        }

        UpdatePlayerList();
    }

    private void OnJoinPressed(string room)
    {
        var nm = _networkManager;
        if (nm == null) return;

        nm.SetLocalPlayerName(_playerNameField.Text);
        GD.Print($"LobbyUI: calling NetworkManager.JoinRoom({room})");
        nm.JoinRoom(room);
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
