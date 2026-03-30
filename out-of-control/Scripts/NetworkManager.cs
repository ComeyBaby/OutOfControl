using Godot;
using System;
using System.Collections.Generic;

[GlobalClass]
public partial class NetworkManager : Node
{
	[Signal] public delegate void StatusChangedEventHandler(string status);

	[Export] public int PortBase = 30000;
	[Export] public int MaxClients = 8;
	[Export] public string DefaultRoomCode = "ROOM";
	[Export] public string GameScenePath = "res://Scenes/TestScene.tscn";
	private const string DefaultPlayerName = "";

	private PackedScene _playerScene;
	private Node3D _playerRoot;
	private Node3D _playerSpawnRoot;
	private MultiplayerSpawner _spawner;
	private readonly HashSet<long> _pendingSpawns = new();

	private Dictionary<long, PlayerController> _players = new();
	private readonly Dictionary<long, string> _playerNames = new();

	[Signal] public delegate void PlayersChangedEventHandler();

	private string _hostRoomName = "";
	private int _hostPort = 0;
	private bool _hostHasPassword = false;
	private string _localPlayerName = DefaultPlayerName;
	private bool _returningToMainMenu = false;

	private Dictionary<long, bool> _readyStates = new();
	private readonly HashSet<long> _connectedPeers = new();
	private readonly HashSet<long> _gameSceneReadyPeers = new();
	private bool _waitingForGameSceneReady = false;
	private readonly Dictionary<long, int> _spawnSlots = new();
	private int _nextSpawnSlot = 0;

	public override void _Ready()
	{
		// Make persistent (autoload-like): reparent to root and avoid duplicates.
		// Use deferred remove/add to avoid "parent is busy" errors during scene construction.
		var root = GetTree().Root;
		var existing = root.GetNodeOrNull<Node>("NetworkManager");
		if (existing != null && existing != this)
		{
			CallDeferred("queue_free");
			return;
		}

		if (GetParent() != root)
		{
			var parent = GetParent();
			parent?.CallDeferred("remove_child", this);
			root.CallDeferred("add_child", this);
			// ensure owner is cleared after reparent
			CallDeferred("set_owner", new Variant());
		}
		Name = "NetworkManager";

		_playerScene = GD.Load<PackedScene>("res://Scenes/Player.tscn");
		GetTree().SceneChanged += OnSceneChanged;

		var multiplayer = Multiplayer;
		multiplayer.PeerConnected += OnPeerConnected;
		multiplayer.PeerDisconnected += OnPeerDisconnected;
		multiplayer.ConnectionFailed += OnConnectionFailed;
		multiplayer.ServerDisconnected += OnServerDisconnected;
		multiplayer.ConnectedToServer += OnConnectionSucceeded;
	}

	private void OnSceneChanged()
	{
		_returningToMainMenu = false;
		_playerRoot = null;
		_playerSpawnRoot = null;
		_spawner = null;

		_playerRoot = GetTree().CurrentScene as Node3D;
		if (_playerRoot == null)
			return;

		_playerSpawnRoot = _playerRoot.GetNodeOrNull<Node3D>("Players") ?? _playerRoot;
		_spawner = _playerRoot.GetNodeOrNull<MultiplayerSpawner>("MultiplayerSpawner");
		if (_spawner != null)
		{
			// Configure spawner to replicate custom Player spawns under the spawn root.
			_spawner.Set("spawn_path", _spawner.GetPathTo(_playerSpawnRoot));
			_spawner.Call("set_spawn_function", new Callable(this, nameof(CreatePlayerSpawn)));
			_spawner.Connect("spawned", new Callable(this, nameof(OnSpawnerSpawned)));
			_spawner.Connect("despawned", new Callable(this, nameof(OnSpawnerDespawned)));
		}

		// remove temporary offline player from object path if present
		var startupPlayer = _playerRoot.GetNodeOrNull<Node3D>("Player");
		if (startupPlayer != null)
		{
			startupPlayer.QueueFree();
		}

		if (IsGameSceneActive())
		{
			if (Multiplayer.IsServer())
			{
				MarkGameSceneReady(Multiplayer.GetUniqueId());
			}
			else
			{
				CallDeferred(nameof(NotifyGameSceneReady));
			}
		}
	}

	private bool IsGameSceneActive()
	{
		return GetTree().CurrentScene?.SceneFilePath == GameScenePath;
	}

	private int RoomCodeToPort(string code)
	{
		code = code?.Trim().ToUpper() ?? "";
		if (string.IsNullOrEmpty(code))
			code = DefaultRoomCode;

		int hash = 0;
		foreach (var c in code)
		{
			hash = ((hash << 5) - hash) + c;
			hash &= 0x7fffffff;
		}

		return PortBase + (hash % 1000);
	}

	public void HostRoom(string roomCode)
	{
		HostRoom(roomCode, "");
	}

	public void HostRoom(string roomCode, string password)
	{
		GD.Print($"NetworkManager: HostRoom called with roomCode={roomCode}, password={password}");
		if (string.IsNullOrWhiteSpace(roomCode))
		{
			roomCode = DefaultRoomCode;
		}
		if (string.IsNullOrWhiteSpace(roomCode))
		{
			EmitSignal(nameof(StatusChanged), "Invalid room code");
			GD.Print("NetworkManager: HostRoom failed, invalid room code");
			return;
		}

		// If already in server mode, drop existing peer and allow re-hosting.
		if (Multiplayer.MultiplayerPeer != null)
		{
			GD.Print("NetworkManager: HostRoom starting with existing MultiplayerPeer; resetting.");
			if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer existingPeer)
			{
				existingPeer.Close();
			}
			Multiplayer.MultiplayerPeer = null;
		}

		int port = RoomCodeToPort(roomCode);
		var peer = new ENetMultiplayerPeer();
		// Leave ENet bandwidth unlimited here; transform RPCs need enough headroom
		// to stay smooth when the host is also rendering locally.
		var err = peer.CreateServer(port, MaxClients);
		GD.Print($"NetworkManager: ENet CreateServer called on port={port}, err={err}");
		if (err != Error.Ok)
		{
			EmitSignal(nameof(StatusChanged), $"Host failed, error {err}");
			GD.Print($"NetworkManager: HostRoom failed with error {err}");
			return;
		}

		_hostRoomName = roomCode;
		_hostPort = port;
		_hostHasPassword = !string.IsNullOrEmpty(password);

		Multiplayer.MultiplayerPeer = peer;
		SetLocalPlayerName(_localPlayerName);
		EmitSignal(nameof(StatusChanged), $"Hosting room '{roomCode}' on port {port}. Local peer id {Multiplayer.GetUniqueId()}");
		GetSpawnSlot(Multiplayer.GetUniqueId());

		// We are already in Lobby scene; no need to reload from host call.
		// GetTree().ChangeSceneToFile("res://Scenes/Lobby.tscn");

		if (Multiplayer.GetUniqueId() > 0)
		{
			QueueSpawn(Multiplayer.GetUniqueId());
		}
	}

	public void JoinRoom(string roomCode, string hostIp)
	{
		JoinRoom(roomCode, hostIp, -1);
	}

	public void JoinRoom(string roomCode)
	{
		JoinRoom(roomCode, "");
	}

	public void JoinRoom(string roomCode, string hostIp, int port)
	{
		GD.Print($"NetworkManager: JoinRoom called with roomCode={roomCode}, hostIp={hostIp}, port={port}");
		if (string.IsNullOrWhiteSpace(roomCode))
		{
			roomCode = DefaultRoomCode;
		}

		if (string.IsNullOrEmpty(hostIp))
			hostIp = "127.0.0.1";

		if (port == -1)
			port = RoomCodeToPort(roomCode);

		var peer = new ENetMultiplayerPeer();
		var err = peer.CreateClient(hostIp, port);
		if (err != Error.Ok)
		{
			EmitSignal(nameof(StatusChanged), $"Join failed: {err} (IP={hostIp} port={port})");
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
		EmitSignal(nameof(StatusChanged), $"Connecting to {hostIp}:{port} room {roomCode}...");
		GD.Print($"NetworkManager: JoinRoom connection attempt to {hostIp}:{port}");
	}

	public void ReturnToMainMenu()
	{
		if (_returningToMainMenu)
			return;

		_returningToMainMenu = true;

		Input.MouseMode = Input.MouseModeEnum.Visible;
		CloseMultiplayerPeer();
		ResetMultiplayerState();
		GetTree().CallDeferred("change_scene_to_file", "res://Scenes/MainMenu.tscn");
	}

	public string GetLocalPlayerName()
	{
		return _localPlayerName;
	}

	public string GetPlayerName(long peerId)
	{
		var peer = Multiplayer.MultiplayerPeer;
		if (peer == null)
			return peerId == 0 ? "" : $"Player {peerId}";

		if (peerId == Multiplayer.GetUniqueId() && !string.IsNullOrWhiteSpace(_localPlayerName))
			return _localPlayerName;

		if (_playerNames.TryGetValue(peerId, out var name) && !string.IsNullOrWhiteSpace(name))
			return name;

		return $"Player {peerId}";
	}

	public long[] GetLobbyPeerIds()
	{
		var peer = Multiplayer.MultiplayerPeer;
		if (peer == null)
			return Array.Empty<long>();

		var ids = new HashSet<long>(_connectedPeers);
		var localId = Multiplayer.GetUniqueId();
		if (localId > 0 &&
			(Multiplayer.IsServer() ||
			 (peer != null && peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)))
		{
			ids.Add(localId);
		}

		// Clients do not keep the host in _connectedPeers until the roster snapshot arrives,
		// so include the host explicitly once we're connected.
		if (!Multiplayer.IsServer() &&
			peer != null &&
			peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
			ids.Add(1);

		var list = new List<long>(ids);
		list.Sort();
		return list.ToArray();
	}

	public bool IsPlayerReady(long peerId)
	{
		return _readyStates.TryGetValue(peerId, out var ready) && ready;
	}

	public void SetLocalPlayerName(string name)
	{
		_localPlayerName = SanitizePlayerName(name);
		PublishLocalPlayerName();
	}

	private string SanitizePlayerName(string name)
	{
		var trimmed = name?.Trim() ?? "";
		if (string.IsNullOrWhiteSpace(trimmed))
			return DefaultPlayerName;

		return trimmed;
	}

	private void PublishLocalPlayerName()
	{
		long localId = Multiplayer.GetUniqueId();
		if (localId <= 0)
			return;

		if (Multiplayer.IsServer())
		{
			Rpc(nameof(NotifyPlayerNameChangedRpc), localId, _localPlayerName);
			return;
		}

		var peer = Multiplayer.MultiplayerPeer;
		if (peer == null || peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
			return;

		RpcId(1, nameof(SetPlayerNameRpc), _localPlayerName);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	public void SetPlayerNameRpc(string name)
	{
		if (!Multiplayer.IsServer())
			return;

		int senderId = Multiplayer.GetRemoteSenderId();
		Rpc(nameof(NotifyPlayerNameChangedRpc), senderId, SanitizePlayerName(name));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	public void NotifyPlayerNameChangedRpc(long peerId, string name)
	{
		_playerNames[peerId] = SanitizePlayerName(name);
		UpdatePlayerDisplayName(peerId);
		EmitSignal(nameof(PlayersChanged));
	}

	private void BroadcastLobbyState()
	{
		if (!Multiplayer.IsServer())
			return;

		var peerIds = GetLobbyPeerIds();
		var names = new List<string>(peerIds.Length);
		var readyStates = new List<bool>(peerIds.Length);
		foreach (var peerId in peerIds)
		{
			names.Add(GetPlayerName(peerId));
			readyStates.Add(IsPlayerReady(peerId));
		}

		Rpc(nameof(BeginLobbySyncRpc));
		for (int i = 0; i < peerIds.Length; i++)
		{
			Rpc(nameof(SyncLobbyEntryRpc), peerIds[i], names[i], readyStates[i]);
		}
		Rpc(nameof(EndLobbySyncRpc));
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void BeginLobbySyncRpc()
	{
		_connectedPeers.Clear();
		_playerNames.Clear();
		_readyStates.Clear();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void SyncLobbyEntryRpc(long peerId, string name, bool ready)
	{
		var localId = Multiplayer.GetUniqueId();
		if (peerId != localId)
			_connectedPeers.Add(peerId);

		_playerNames[peerId] = SanitizePlayerName(name);
		_readyStates[peerId] = ready;
		UpdatePlayerDisplayName(peerId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void EndLobbySyncRpc()
	{
		EmitSignal(nameof(PlayersChanged));
	}

	private void NotifyGameSceneReady()
	{
		if (Multiplayer.IsServer())
			return;

		if (!IsGameSceneActive())
			return;

		RpcId(1, nameof(ReportGameSceneReadyRpc), Multiplayer.GetUniqueId());
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	public void ReportGameSceneReadyRpc(long peerId)
	{
		if (!Multiplayer.IsServer())
			return;

		MarkGameSceneReady(peerId);
	}

	private void OnPeerConnected(long id)
	{
		EmitSignal(nameof(StatusChanged), $"Peer connected: {id}");

		if (!Multiplayer.IsServer())
			return;

		_connectedPeers.Add(id);
		GetSpawnSlot(id);
		// Spawn on server and broadcast to everyone
		QueueSpawn(id);

		// default not ready
		_readyStates[id] = false;
		BroadcastLobbyState();
	}

	private void OnPeerDisconnected(long id)
	{
		EmitSignal(nameof(StatusChanged), $"Peer disconnected: {id}");
		_connectedPeers.Remove(id);
		_pendingSpawns.Remove(id);
		_gameSceneReadyPeers.Remove(id);
		_spawnSlots.Remove(id);
		_playerNames.Remove(id);

		if (_players.TryGetValue(id, out var p))
		{
			p.QueueFree();
			_players.Remove(id);
		}

		_readyStates.Remove(id);

		EmitSignal(nameof(PlayersChanged));
		if (Multiplayer.IsServer())
			BroadcastLobbyState();
		TrySpawnPlayersWhenGameSceneReady();
	}

	private void OnConnectionSucceeded()
	{
		EmitSignal(nameof(StatusChanged), $"Connected to server. Local peer id {Multiplayer.GetUniqueId()}");
		SetLocalPlayerName(_localPlayerName);
		// Move client to lobby scene once connected
		GetTree().ChangeSceneToFile("res://Scenes/Lobby.tscn");
	}

	private void OnConnectionFailed()
	{
		EmitSignal(nameof(StatusChanged), "Connection failed.");
	}

	public bool IsHosting()
	{
		return Multiplayer.IsServer() && Multiplayer.MultiplayerPeer != null;
	}

	public string GetRoomName()
	{
		return _hostRoomName;
	}

	public int GetRoomPort()
	{
		return _hostPort;
	}

	public bool GetHasPassword()
	{
		return _hostHasPassword;
	}

	private void OnServerDisconnected()
	{
		EmitSignal(nameof(StatusChanged), "Server disconnected.");
		ResetMultiplayerState();
		Input.MouseMode = Input.MouseModeEnum.Visible;
		if (_returningToMainMenu)
			return;

		GetTree().CallDeferred("change_scene_to_file", "res://Scenes/MainMenu.tscn");
	}

	private void CloseMultiplayerPeer()
	{
		if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enetPeer)
			enetPeer.Close();

		Multiplayer.MultiplayerPeer = null;
	}

	private void ResetMultiplayerState()
	{
		if (Multiplayer.MultiplayerPeer != null)
			Multiplayer.MultiplayerPeer = null;

		// Clean all spawned players and clear any cached lobby/game state.
		foreach (var kv in _players)
			kv.Value.QueueFree();

		_players.Clear();
		_playerNames.Clear();
		_connectedPeers.Clear();
		_pendingSpawns.Clear();
		_gameSceneReadyPeers.Clear();
		_waitingForGameSceneReady = false;
		_spawnSlots.Clear();
		_nextSpawnSlot = 0;
		_readyStates.Clear();
		_hostRoomName = "";
		_hostPort = 0;
		_hostHasPassword = false;
		EmitSignal(nameof(PlayersChanged));
	}

	private void QueueSpawn(long peerId)
	{
		if (!Multiplayer.IsServer())
			return;

		if (_players.ContainsKey(peerId))
			return;

		if (_waitingForGameSceneReady)
		{
			_pendingSpawns.Add(peerId);
			return;
		}

		if (_playerRoot == null)
		{
			_pendingSpawns.Add(peerId);
			return;
		}

		SpawnPlayerNow(peerId);
	}

	private Node CreatePlayerSpawn(Godot.Collections.Dictionary data)
	{
		long peerId = 0;
		if (data != null && data.ContainsKey("peer_id"))
		{
			long.TryParse(data["peer_id"].ToString(), out peerId);
		}

		int spawnSlot = 0;
		if (data != null && data.ContainsKey("spawn_slot"))
		{
			int.TryParse(data["spawn_slot"].ToString(), out spawnSlot);
		}

		var player = _playerScene.Instantiate<PlayerController>();
		player.Name = $"Player_{peerId}";
		player.SetMultiplayerAuthority((int)peerId);
		player.Position = new Vector3(spawnSlot * 2, 0, 0);
		return player;
	}

	private void OnSpawnerSpawned(Node node)
	{
		if (node is not PlayerController player)
			return;

		RegisterPlayer(player);
	}

	private void OnSpawnerDespawned(Node node)
	{
		if (node is not PlayerController player)
			return;

		var peerId = player.GetMultiplayerAuthority();
		if (_players.Remove(peerId))
		{
			_readyStates.Remove(peerId);
			EmitSignal(nameof(PlayersChanged));
		}
	}

	private void SpawnPlayerNow(long peerId)
	{
		if (_spawner == null)
		{
			GD.PrintErr("No MultiplayerSpawner configured");
			return;
		}

		var spawnData = new Godot.Collections.Dictionary
		{
			{ "peer_id", peerId },
			{ "spawn_slot", GetSpawnSlot(peerId) }
		};

		_spawner.Call("spawn", spawnData);

		var spawnedPlayer = GetSpawnedPlayer(peerId);
		if (spawnedPlayer != null)
		{
			RegisterPlayer(spawnedPlayer);
		}
	}

	private void RegisterPlayer(PlayerController player)
	{
		long peerId = player.GetMultiplayerAuthority();
		if (peerId <= 0)
			peerId = GetPeerIdForPlayer(player);

		player.RefreshAuthorityState();

		if (_players.ContainsKey(peerId))
			return;

		_players[peerId] = player;
		_readyStates[peerId] = false;
		player.SetDisplayName(GetPlayerName(peerId));

		var cam = player.GetNodeOrNull<Camera3D>("Head/Camera3D");
		if (cam != null)
		{
			cam.Current = peerId == Multiplayer.GetUniqueId();
		}

		EmitSignal(nameof(PlayersChanged));
		EmitSignal(nameof(StatusChanged), $"Spawned player {peerId}.");
	}

	private void UpdatePlayerDisplayName(long peerId)
	{
		if (_players.TryGetValue(peerId, out var player))
			player.SetDisplayName(GetPlayerName(peerId));
	}

	public PlayerController GetPlayer(long peerId)
	{
		if (_players.TryGetValue(peerId, out var player) && GodotObject.IsInstanceValid(player))
			return player;

		if (_playerRoot != null)
		{
			var found = _playerRoot.GetNodeOrNull<PlayerController>($"Players/Player_{peerId}")
				?? _playerRoot.GetNodeOrNull<PlayerController>($"Player_{peerId}");
			if (found != null)
				return found;
		}

		return null;
	}

	private PlayerController GetSpawnedPlayer(long peerId)
	{
		if (_playerSpawnRoot != null)
			return _playerSpawnRoot.GetNodeOrNull<PlayerController>($"Player_{peerId}");

		return GetPlayer(peerId);
	}

	private long GetPeerIdForPlayer(PlayerController player)
	{
		var name = player.Name.ToString();
		if (name.StartsWith("Player_") && long.TryParse(name.Substring("Player_".Length), out var peerId))
		{
			return peerId;
		}

		return player.GetMultiplayerAuthority();
	}

	private int GetSpawnSlot(long peerId)
	{
		if (_spawnSlots.TryGetValue(peerId, out var existing))
			return existing;

		var slot = _nextSpawnSlot++;
		_spawnSlots[peerId] = slot;
		return slot;
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	public void SetReadyRpc(bool ready)
	{
		if (!Multiplayer.IsServer())
			return;
		int senderId = Multiplayer.GetRemoteSenderId();
		_readyStates[senderId] = ready;
		Rpc(nameof(NotifyReadyChangedRpc), senderId, ready);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	public void NotifyReadyChangedRpc(long peerId, bool ready)
	{
		_readyStates[peerId] = ready;
		EmitSignal(nameof(StatusChanged), $"Player {peerId} ready={ready}");
		EmitSignal(nameof(PlayersChanged));
	}

	public void SetReady(bool ready)
	{
		if (Multiplayer.IsServer())
		{
			int id = Multiplayer.GetUniqueId();
			_readyStates[id] = ready;
			Rpc(nameof(NotifyReadyChangedRpc), id, ready);
		}
		else
		{
			_readyStates[Multiplayer.GetUniqueId()] = ready;
			EmitSignal(nameof(PlayersChanged));
			RpcId(1, nameof(SetReadyRpc), ready);
		}
	}

	public bool IsEveryoneReady()
	{
		if (!_readyStates.TryGetValue(Multiplayer.GetUniqueId(), out var hostReady) || !hostReady)
			return false;

		foreach (var id in _connectedPeers)
		{
			if (!_readyStates.TryGetValue(id, out var r) || !r)
				return false;
		}
		return true;
	}

	public void TryStartGame()
	{
		if (!Multiplayer.IsServer())
			return;
		if (!IsEveryoneReady())
		{
			EmitSignal(nameof(StatusChanged), "Not everyone is ready");
			return;
		}
		_waitingForGameSceneReady = true;
		_pendingSpawns.Clear();
		_pendingSpawns.Add(Multiplayer.GetUniqueId());
		foreach (var id in _connectedPeers)
			_pendingSpawns.Add(id);
		_gameSceneReadyPeers.Clear();
		Rpc(nameof(LoadGameRpc));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	public void LoadGameRpc()
	{
		GetTree().ChangeSceneToFile(GameScenePath);
	}

	private void MarkGameSceneReady(long peerId)
	{
		_gameSceneReadyPeers.Add(peerId);
		TrySpawnPlayersWhenGameSceneReady();
	}

	private void TrySpawnPlayersWhenGameSceneReady()
	{
		if (!Multiplayer.IsServer() || !_waitingForGameSceneReady)
			return;

		if (!_gameSceneReadyPeers.Contains(Multiplayer.GetUniqueId()))
			return;

		foreach (var id in _connectedPeers)
		{
			if (!_gameSceneReadyPeers.Contains(id))
				return;
		}

		_waitingForGameSceneReady = false;

		var spawnIds = new List<long>(_pendingSpawns);
		_pendingSpawns.Clear();
		foreach (var id in spawnIds)
		{
			SpawnPlayerNow(id);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void UpdatePlayerTransformRpc(long peerId, Vector3 pos, Vector3 rot)
	{
		if (peerId == Multiplayer.GetUniqueId())
			return;

		if (!_players.TryGetValue(peerId, out var player))
		{
			// Fallback for late registration (e.g., just spawned by MultiplayerSpawner).
			if (_playerRoot != null)
			{
				player = _playerRoot.GetNodeOrNull<PlayerController>($"Players/Player_{peerId}")
					?? _playerRoot.GetNodeOrNull<PlayerController>($"Player_{peerId}");
				if (player != null)
					_players[peerId] = player;
			}
		}
		if (player == null)
			return;

		player.SetNetworkTransform(pos, rot);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReportTransformRpc(Vector3 pos, Vector3 rot)
	{
		if (!Multiplayer.IsServer())
			return;

		int senderId = Multiplayer.GetRemoteSenderId();
		Rpc(nameof(UpdatePlayerTransformRpc), senderId, pos, rot);
	}
}
