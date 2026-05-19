using Godot;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

[GlobalClass]
public partial class NetworkManager : Node
{
	public enum LobbyUiRole
	{
		Offline,
		Connecting,
		Hosting,
		Client
	}

	public readonly struct LobbyUiState
	{
		public LobbyUiRole Role { get; }
		public bool CanHost { get; }
		public bool CanJoin { get; }
		public bool CanToggleReady { get; }
		public bool CanStartMatch { get; }
		public bool CanEditConnectionFields { get; }
		public bool ShowCopyToClipboardOnConnectionFields { get; }
		public string HintText { get; }

		public LobbyUiState(
			LobbyUiRole role,
			bool canHost,
			bool canJoin,
			bool canToggleReady,
			bool canStartMatch,
			bool canEditConnectionFields,
			bool showCopyToClipboardOnConnectionFields,
			string hintText)
		{
			Role = role;
			CanHost = canHost;
			CanJoin = canJoin;
			CanToggleReady = canToggleReady;
			CanStartMatch = canStartMatch;
			CanEditConnectionFields = canEditConnectionFields;
			ShowCopyToClipboardOnConnectionFields = showCopyToClipboardOnConnectionFields;
			HintText = hintText ?? "";
		}
	}

	[Signal] public delegate void StatusChangedEventHandler(string status);
	[Signal] public delegate void CombatFeedbackEventHandler(string message, bool hit, bool killed);
	private const string NetworkManagerNodeName = "NetworkManager";
	private const string CombatFeedbackSignalName = "CombatFeedback";

	[Export] public int PortBase = 30000;
	[Export] public int MaxClients = 8;
	[Export] public string DefaultRoomCode = "ROOM";
	[Export(PropertyHint.File, "*.tscn")] public string GameScenePath = "res://Scenes/Levels/Classic.tscn";
	[Export(PropertyHint.File, "*.tscn")] public string PlayerScenePath = "res://Scenes/Player.tscn";
	[Export(PropertyHint.File, "*.tscn")] public string LobbyScenePath = "res://Scenes/UI/Lobby.tscn";
	[Export(PropertyHint.File, "*.tscn")] public string MainMenuScenePath = "res://Scenes/UI/MainMenu.tscn";
	[Export] private string _playerNodePrefix = "Player_";
	private const string DefaultPlayerName = "";
	private const string DefaultWeapon = "Assault";

	private PackedScene _playerScene;
	private Node3D _playerRoot;
	private Node3D _playerSpawnRoot;
	private Node3D _spawnPointRoot;
	private MultiplayerSpawner _spawner;
	private Node3D _startupPlayer;
	private readonly HashSet<long> _pendingSpawns = new();
	private RoundManager _roundManager;

	private Dictionary<long, PlayerController> _players = new();
	private readonly Dictionary<long, string> _playerNames = new();
	private readonly Dictionary<long, string> _playerWeapons = new();

	[Signal] public delegate void PlayersChangedEventHandler();
	private string _localPlayerName = DefaultPlayerName;
	private string _localWeapon = DefaultWeapon;
	private bool _returningToMainMenu = false;

	private Dictionary<long, bool> _readyStates = new();
	private readonly HashSet<long> _connectedPeers = new();
	private readonly HashSet<long> _gameSceneReadyPeers = new();
	private bool _waitingForGameSceneReady = false;
	private readonly Dictionary<long, int> _spawnSlots = new();
	private int _nextSpawnSlot = 0;
	private bool _isChangingScene = false;

	private bool HasActivePeer()
	{
		var peer = Multiplayer.MultiplayerPeer;
		return peer != null && peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected;
	}

	private bool IsServerActive()
	{
		return HasActivePeer() && Multiplayer.IsServer();
	}

	private long GetLocalPeerIdSafe()
	{
		return HasActivePeer() ? Multiplayer.GetUniqueId() : 0;
	}

	public long GetLocalPeerIdOrZero()
	{
		return GetLocalPeerIdSafe();
	}

	public override void _Ready()
	{
		var root = GetTree().Root;
		var existing = root.GetNodeOrNull<Node>(NetworkManagerNodeName);
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
			CallDeferred("set_owner", new Variant());
		}
		Name = NetworkManagerNodeName;

		_roundManager = GetNodeOrNull<RoundManager>("RoundManager");
		if (_roundManager == null)
		{
			_roundManager = new RoundManager { Name = "RoundManager" };
			AddChild(_roundManager);
		}
		_roundManager.Initialize(this);

		_playerScene = GD.Load<PackedScene>(PlayerScenePath);
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
		_isChangingScene = false;
		_returningToMainMenu = false;
		_playerRoot = GetTree().CurrentScene as Node3D;
		_playerSpawnRoot = null;
		_spawnPointRoot = null;
		if (_playerRoot == null)
		{
			_roundManager?.ResetToLobby();
			return;
		}

		_playerSpawnRoot = _playerRoot.GetNodeOrNull<Node3D>("Players") ?? _playerRoot;
		_spawnPointRoot = _playerRoot.GetNodeOrNull<Node3D>("SpawnPoints");
		_spawner = _playerRoot.GetNodeOrNull<MultiplayerSpawner>("MultiplayerSpawner");
		if (_spawner != null)
		{
			_spawner.Set("spawn_path", _spawner.GetPathTo(_playerSpawnRoot));
			_spawner.Call("set_spawn_function", new Callable(this, nameof(CreatePlayerSpawn)));
			_spawner.Connect("spawned", new Callable(this, nameof(OnSpawnerSpawned)));
			_spawner.Connect("despawned", new Callable(this, nameof(OnSpawnerDespawned)));
		}

		_startupPlayer = _playerRoot.GetNodeOrNull<Node3D>("Player");
		if (_startupPlayer != null)
		{
			_startupPlayer.QueueFree();
		}

		if (IsGameSceneActive())
		{
			if (IsServerActive())
			{
				MarkGameSceneReady(GetLocalPeerIdSafe());
			}
			else if (HasActivePeer())
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
		roomCode = GenerateRoomCode();
		DefaultRoomCode = roomCode;

		if (Multiplayer.MultiplayerPeer != null)
		{
			if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer existingPeer)
			{
				existingPeer.Close();
			}
			Multiplayer.MultiplayerPeer = null;
		}

		int port = RoomCodeToPort(roomCode);
		var peer = new ENetMultiplayerPeer();
		var err = peer.CreateServer(port, MaxClients);
		if (err != Error.Ok)
		{
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
		SetLocalPlayerName(_localPlayerName);
		SetLocalWeapon(_localWeapon);
		EmitSignal(nameof(StatusChanged), "");
		var localId = GetLocalPeerIdSafe();
		GetSpawnSlot(localId);
		if (localId > 0)
		{
			QueueSpawn(localId);
		}
	}

	private static string GenerateRoomCode()
	{
		const string letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
		Span<char> code = stackalloc char[4];
		for (int i = 0; i < code.Length; i++)
			code[i] = letters[Random.Shared.Next(letters.Length)];
		return new string(code);
	}

	public void JoinRoom(string roomCode, string hostIp)
	{
		JoinRoom(roomCode, hostIp, -1);
	}

	public void JoinRoom(string roomCode, string hostIp, int port)
	{
		if (string.IsNullOrWhiteSpace(roomCode))
			return;
		roomCode = roomCode.Trim().ToUpperInvariant();

		if (string.IsNullOrEmpty(hostIp))
			return;

		if (port == -1)
			port = RoomCodeToPort(roomCode);

		var peer = new ENetMultiplayerPeer();
		var err = peer.CreateClient(hostIp, port);
		if (err != Error.Ok)
		{
			return;
		}

		Multiplayer.MultiplayerPeer = peer;
	}

	public void ReturnToMainMenu()
	{
		if (_returningToMainMenu)
			return;

		_returningToMainMenu = true;

		CloseMultiplayerPeer();
		ResetMultiplayerState();
		GetTree().CallDeferred("change_scene_to_file", MainMenuScenePath);
	}

	public string GetLocalPlayerName()
	{
		return _localPlayerName;
	}

	public string GetLocalWeapon()
	{
		return _localWeapon;
	}

	public string GetPlayerName(long peerId)
	{
		var peer = Multiplayer.MultiplayerPeer;
		if (peer == null)
			return peerId == 0 ? "" : $"Player {peerId}";

		if (peerId == GetLocalPeerIdSafe() && !string.IsNullOrWhiteSpace(_localPlayerName))
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
		var localId = GetLocalPeerIdSafe();
		if (localId > 0 &&
			(IsServerActive() ||
			 (peer != null && peer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)))
		{
			ids.Add(localId);
		}

		if (!IsServerActive() &&
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

	public bool HasMultiplayerPeer()
	{
		return HasActivePeer();
	}

	public LobbyUiState GetLobbyUiState()
	{
		var peer = Multiplayer.MultiplayerPeer;
		var isOfflinePeer = peer is OfflineMultiplayerPeer;
		var connectionStatus = isOfflinePeer
			? MultiplayerPeer.ConnectionStatus.Disconnected
			: peer?.GetConnectionStatus() ?? MultiplayerPeer.ConnectionStatus.Disconnected;
		var hasConnectedPeer = !isOfflinePeer && connectionStatus == MultiplayerPeer.ConnectionStatus.Connected;
		var isConnecting = !isOfflinePeer && connectionStatus == MultiplayerPeer.ConnectionStatus.Connecting;
		var isHost = hasConnectedPeer && Multiplayer.IsServer();
		var isClient = hasConnectedPeer && !isHost;

		var role = LobbyUiRole.Offline;
		if (isConnecting)
			role = LobbyUiRole.Connecting;
		else if (isHost)
			role = LobbyUiRole.Hosting;
		else if (isClient)
			role = LobbyUiRole.Client;

		var canEditConnectionFields = !hasConnectedPeer && !isConnecting;
		var canHost = canEditConnectionFields;
		var canJoin = canEditConnectionFields;
		var canToggleReady = hasConnectedPeer;
		var canStartMatch = isHost && IsEveryoneReady();
		var showCopyOnConnectionFields = isHost;

		var hintText = role switch
		{
			LobbyUiRole.Connecting => "Connecting...",
			LobbyUiRole.Hosting => "Hosting. Share your IP and room code, then press Start when everyone is ready.",
			LobbyUiRole.Client => "Connected. Choose your loadout and press Ready.",
			_ => "Host or join a room to begin."
		};

		return new LobbyUiState(
			role,
			canHost,
			canJoin,
			canToggleReady,
			canStartMatch,
			canEditConnectionFields,
			showCopyOnConnectionFields,
			hintText);
	}

	public RoundManager GetRoundManager()
	{
		return _roundManager;
	}

	public bool IsRoundAcceptingPlayerInput()
	{
		return _roundManager == null
			|| _roundManager.Phase == RoundPhase.Lobby
			|| _roundManager.Phase == RoundPhase.Playing
			|| _roundManager.Phase == RoundPhase.RoundOver;
	}

	public bool IsRoundWaitingForPerks()
	{
		return _roundManager != null && _roundManager.Phase == RoundPhase.PerkSelection;
	}

	public void SetLocalPlayerName(string name)
	{
		_localPlayerName = SanitizePlayerName(name);
		PublishLocalPlayerName();
	}

	public void SetLocalWeapon(string weapon)
	{
		_localWeapon = string.IsNullOrWhiteSpace(weapon) ? DefaultWeapon : weapon;
		PublishLocalWeapon();
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
		var peer = Multiplayer.MultiplayerPeer;
		if (peer == null)
			return;

		long localId = GetLocalPeerIdSafe();
		if (localId <= 0)
			return;

		if (IsServerActive())
		{
			Rpc(nameof(NotifyPlayerNameChangedRpc), localId, _localPlayerName);
			return;
		}

		if (peer == null || peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
			return;

		RpcId(1, nameof(SetPlayerNameRpc), _localPlayerName);
	}

	private void PublishLocalWeapon()
	{
		var peer = Multiplayer.MultiplayerPeer;
		if (peer == null)
			return;

		long localId = GetLocalPeerIdSafe();
		if (localId <= 0)
			return;

		if (IsServerActive())
		{
			Rpc(nameof(NotifyWeaponChangedRpc), localId, _localWeapon);
			BroadcastLobbyState();
			return;
		}

		if (peer == null || peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
			return;

		RpcId(1, nameof(SetWeaponRpc), _localWeapon);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	public void SetPlayerNameRpc(string name)
	{
		if (!IsServerActive())
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

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	public void SetWeaponRpc(string weapon)
	{
		if (!IsServerActive())
			return;

		int senderId = Multiplayer.GetRemoteSenderId();
		Rpc(nameof(NotifyWeaponChangedRpc), senderId, string.IsNullOrWhiteSpace(weapon) ? DefaultWeapon : weapon);
		BroadcastLobbyState();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	public void NotifyWeaponChangedRpc(long peerId, string weapon)
	{
		_playerWeapons[peerId] = string.IsNullOrWhiteSpace(weapon) ? DefaultWeapon : weapon;
		EmitSignal(nameof(PlayersChanged));
	}

	private void BroadcastLobbyState()
	{
		if (!IsServerActive())
			return;

		var peerIds = GetLobbyPeerIds();
		var names = new List<string>(peerIds.Length);
		var readyStates = new List<bool>(peerIds.Length);
		var weapons = new List<string>(peerIds.Length);
		foreach (var peerId in peerIds)
		{
			names.Add(GetPlayerName(peerId));
			readyStates.Add(IsPlayerReady(peerId));
			weapons.Add(GetPlayerWeapon(peerId));
		}

		Rpc(nameof(BeginLobbySyncRpc));
		for (int i = 0; i < peerIds.Length; i++)
		{
			Rpc(nameof(SyncLobbyEntryRpc), peerIds[i], names[i], readyStates[i], weapons[i]);
		}
		Rpc(nameof(EndLobbySyncRpc));
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void BeginLobbySyncRpc()
	{
		_connectedPeers.Clear();
		_playerNames.Clear();
		_playerWeapons.Clear();
		_readyStates.Clear();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void SyncLobbyEntryRpc(long peerId, string name, bool ready, string weapon)
	{
		var localId = GetLocalPeerIdSafe();
		if (peerId != localId)
			_connectedPeers.Add(peerId);

		_playerNames[peerId] = SanitizePlayerName(name);
		_readyStates[peerId] = ready;
		_playerWeapons[peerId] = string.IsNullOrWhiteSpace(weapon) ? DefaultWeapon : weapon;
		UpdatePlayerDisplayName(peerId);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void EndLobbySyncRpc()
	{
		EmitSignal(nameof(PlayersChanged));
	}

	private void NotifyGameSceneReady()
	{
		if (IsServerActive())
			return;

		if (!IsGameSceneActive())
			return;

		var localId = GetLocalPeerIdSafe();
		if (localId <= 0)
			return;
		RpcId(1, nameof(ReportGameSceneReadyRpc), localId);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	public void ReportGameSceneReadyRpc(long peerId)
	{
		if (!IsServerActive())
			return;

		MarkGameSceneReady(peerId);
	}

	private void OnPeerConnected(long id)
	{
		EmitSignal(nameof(StatusChanged), $"Peer connected: {id}");

		if (!IsServerActive())
			return;

		_connectedPeers.Add(id);
		GetSpawnSlot(id);
		QueueSpawn(id);

		_readyStates[id] = false;
		BroadcastLobbyState();
	}

	private void OnPeerDisconnected(long id)
	{
		var isServer = IsServerActive();

		EmitSignal(nameof(StatusChanged), $"Peer disconnected: {id}");
		_connectedPeers.Remove(id);
		_pendingSpawns.Remove(id);
		_gameSceneReadyPeers.Remove(id);
		_spawnSlots.Remove(id);
		_playerNames.Remove(id);
		_playerWeapons.Remove(id);

		if (_players.TryGetValue(id, out var p))
		{
			if (isServer && GodotObject.IsInstanceValid(p) && !_isChangingScene)
			{
				p.QueueFree();
				_players.Remove(id);
			}
		}
		_roundManager?.RemovePlayer(id);

		_readyStates.Remove(id);

		EmitSignal(nameof(PlayersChanged));
		if (isServer)
			BroadcastLobbyState();
		TrySpawnPlayersWhenGameSceneReady();
	}

	private void OnConnectionSucceeded()
	{
		EmitSignal(nameof(StatusChanged), "");
		SetLocalPlayerName(_localPlayerName);
		SetLocalWeapon(_localWeapon);
		GetTree().ChangeSceneToFile(LobbyScenePath);
	}

	private void OnConnectionFailed()
	{
		EmitSignal(nameof(StatusChanged), "Connection failed.");
	}

	public bool IsHosting()
	{
		return IsServerActive();
	}

	public string GetShareableHostAddress()
	{
		return GetBestLocalAddress();
	}

	private void OnServerDisconnected()
	{
		EmitSignal(nameof(StatusChanged), "Server disconnected.");
		ResetMultiplayerState();
		if (_returningToMainMenu)
			return;

		GetTree().CallDeferred("change_scene_to_file", MainMenuScenePath);
	}

	private string GetBestLocalAddress()
	{
		try
		{
			foreach (var address in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
			{
				if (address.AddressFamily != AddressFamily.InterNetwork)
					continue;

				if (IPAddress.IsLoopback(address))
					continue;

				var text = address.ToString();
				if (text.StartsWith("169.254.", StringComparison.Ordinal))
					continue;

				return text;
			}
		}
		catch (Exception e)
		{
			GD.PrintErr($"NetworkManager: failed to detect local IP address: {e.Message}");
		}

		return "127.0.0.1";
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

		foreach (var kv in _players)
			kv.Value.QueueFree();

		_players.Clear();
		_playerNames.Clear();
		_playerWeapons.Clear();
		_connectedPeers.Clear();
		_pendingSpawns.Clear();
		_gameSceneReadyPeers.Clear();
		_waitingForGameSceneReady = false;
		_spawnSlots.Clear();
		_nextSpawnSlot = 0;
		_readyStates.Clear();
		_roundManager?.ResetToLobby();
		EmitSignal(nameof(PlayersChanged));
	}

	private void QueueSpawn(long peerId)
	{
		if (!IsServerActive())
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

		if (_playerScene == null)
		{
			GD.PrintErr($"NetworkManager: PlayerScene is null. Path: {PlayerScenePath}");
			return null;
		}

		var spawnedNode = _playerScene.Instantiate();
		if (spawnedNode is not PlayerController player)
		{
			var nodeType = spawnedNode?.GetType().Name ?? "null";
			var godotClass = spawnedNode?.GetClass() ?? "null";
			GD.PrintErr($"NetworkManager: Player scene root is not PlayerController. Path: {PlayerScenePath}, C# type: {nodeType}, Godot class: {godotClass}");
			spawnedNode?.QueueFree();
			return null;
		}

		player.Name = $"Player_{peerId}";
		player.SetMultiplayerAuthority((int)peerId);
		player.Position = GetSpawnPosition(spawnSlot);
		var stats = player.GetStats();
		if (stats != null)
			stats.SetWeapon(GetPlayerWeapon(peerId));
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
		_playerWeapons.TryAdd(peerId, DefaultWeapon);
		player.SetDisplayName(GetPlayerName(peerId));

		var cam = player.GetViewCamera();
		if (cam != null)
		{
			cam.Current = peerId == GetLocalPeerIdSafe();
		}

		EmitSignal(nameof(PlayersChanged));
		EmitSignal(nameof(StatusChanged), $"Spawned player {peerId}.");
	}

	private void UpdatePlayerDisplayName(long peerId)
	{
		if (_players.TryGetValue(peerId, out var player))
			player.SetDisplayName(GetPlayerName(peerId));
	}

	public string GetPlayerWeapon(long peerId)
	{
		if (peerId == GetLocalPeerIdSafe() && !string.IsNullOrWhiteSpace(_localWeapon))
			return _localWeapon;

		if (_playerWeapons.TryGetValue(peerId, out var weapon) && !string.IsNullOrWhiteSpace(weapon))
			return weapon;

		return DefaultWeapon;
	}

	public string GetPlayerWeaponClass(long peerId)
	{
		return GetPlayerWeapon(peerId);
	}

	public PlayerController GetPlayer(long peerId)
	{
		if (_players.TryGetValue(peerId, out var player) && GodotObject.IsInstanceValid(player))
			return player;

		if (_playerRoot != null)
		{
			var found = _playerRoot.GetNodeOrNull<PlayerController>($"Players/{_playerNodePrefix}{peerId}")
				?? _playerRoot.GetNodeOrNull<PlayerController>($"{_playerNodePrefix}{peerId}");
			if (found != null)
				return found;
		}

		return null;
	}

	public long[] GetSpawnedPlayerIds()
	{
		if (_players.Count == 0)
			return Array.Empty<long>();

		var ids = new List<long>();
		foreach (var kv in _players)
		{
			if (kv.Value != null && GodotObject.IsInstanceValid(kv.Value))
				ids.Add(kv.Key);
		}

		ids.Sort();
		return ids.ToArray();
	}

	private PlayerController GetSpawnedPlayer(long peerId)
	{
		if (_playerSpawnRoot != null)
			return _playerSpawnRoot.GetNodeOrNull<PlayerController>($"{_playerNodePrefix}{peerId}");

		return GetPlayer(peerId);
	}

	private long GetPeerIdForPlayer(PlayerController player)
	{
		var name = player.Name.ToString();
		if (name.StartsWith(_playerNodePrefix) && long.TryParse(name.Substring(_playerNodePrefix.Length), out var peerId))
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
		if (!IsServerActive())
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
		var peer = Multiplayer.MultiplayerPeer;
		if (peer == null || peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected)
			return;

		if (IsServerActive())
		{
			int id = (int)GetLocalPeerIdSafe();
			_readyStates[id] = ready;
			Rpc(nameof(NotifyReadyChangedRpc), id, ready);
		}
		else
		{
			_readyStates[GetLocalPeerIdSafe()] = ready;
			EmitSignal(nameof(PlayersChanged));
			RpcId(1, nameof(SetReadyRpc), ready);
		}
	}

	public bool IsEveryoneReady()
	{
		if (!_readyStates.TryGetValue(GetLocalPeerIdSafe(), out var hostReady) || !hostReady)
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
		if (!IsHosting())
			return;
		if (!IsEveryoneReady())
		{
			EmitSignal(nameof(StatusChanged), "Not everyone is ready");
			return;
		}
		_waitingForGameSceneReady = true;
		_pendingSpawns.Clear();
		_pendingSpawns.Add(GetLocalPeerIdSafe());
		foreach (var id in _connectedPeers)
			_pendingSpawns.Add(id);
		_gameSceneReadyPeers.Clear();
		Rpc(nameof(LoadGameRpc));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true)]
	public void LoadGameRpc()
	{
		PrepareForSceneChange();
		GetTree().ChangeSceneToFile(GameScenePath);
	}

	private void MarkGameSceneReady(long peerId)
	{
		_gameSceneReadyPeers.Add(peerId);
		TrySpawnPlayersWhenGameSceneReady();
	}

	private void TrySpawnPlayersWhenGameSceneReady()
	{
		if (!IsServerActive() || !_waitingForGameSceneReady)
			return;

		var localId = GetLocalPeerIdSafe();
		if (!_gameSceneReadyPeers.Contains(localId))
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
		_roundManager?.StartRoundForPlayers(GetSpawnedPlayerIds());
	}

	private Vector3 GetSpawnPosition(int spawnSlot)
	{
		if (_spawnPointRoot != null && _spawnPointRoot.GetChildCount() > 0)
		{
			var index = Mathf.PosMod(spawnSlot, _spawnPointRoot.GetChildCount());
			if (_spawnPointRoot.GetChild(index) is Node3D spawnPoint)
				return spawnPoint.Position;
		}

		return new Vector3(spawnSlot * 2, 0, 0);
	}

	public void ReportPlayerEliminated(long attackerPeerId, long victimPeerId)
	{
		if (!IsServerActive())
			return;

		_roundManager?.NotifyElimination(attackerPeerId, victimPeerId);
	}

	public void SendCombatFeedback(long targetPeerId, string message, bool hit, bool killed)
	{
		if (targetPeerId <= 0 || targetPeerId == GetLocalPeerIdSafe())
		{
			EmitSignal(CombatFeedbackSignalName, message, hit, killed);
			return;
		}

		RpcId(targetPeerId, nameof(NotifyCombatFeedbackRpc), message, hit, killed);
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void NotifyCombatFeedbackRpc(string message, bool hit, bool killed)
	{
		EmitSignal(CombatFeedbackSignalName, message, hit, killed);
	}

	public void NotifyLocalPerkSelectionComplete()
	{
		if (Multiplayer.MultiplayerPeer == null || _roundManager == null)
			return;

		if (IsServerActive())
		{
			_roundManager.SetPerkSelectionReady(GetLocalPeerIdSafe());
			return;
		}

		RpcId(1, nameof(SetPerkSelectionReadyRpc));
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
	public void SetPerkSelectionReadyRpc()
	{
		if (!IsServerActive())
			return;

		_roundManager?.SetPerkSelectionReady(Multiplayer.GetRemoteSenderId());
	}

	public void RestartGameFromRoundManager()
	{
		if (!IsHosting())
			return;

		_waitingForGameSceneReady = true;
		_pendingSpawns.Clear();
		_pendingSpawns.Add(GetLocalPeerIdSafe());
		foreach (var id in _connectedPeers)
			_pendingSpawns.Add(id);
		_gameSceneReadyPeers.Clear();
		_players.Clear();
		Rpc(nameof(LoadGameRpc));
	}

	public void ReturnEveryoneToLobbyFromRound()
	{
		if (!IsHosting())
			return;

		_roundManager?.ResetToLobby();
		_waitingForGameSceneReady = false;
		_pendingSpawns.Clear();
		Rpc(nameof(LoadLobbyRpc));
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true)]
	public void LoadLobbyRpc()
	{
		PrepareForSceneChange();
		GetTree().ChangeSceneToFile(LobbyScenePath);
	}

	private void PrepareForSceneChange()
	{
		_isChangingScene = true;

		if (_spawner != null && GodotObject.IsInstanceValid(_spawner))
		{
			if (_spawner.IsConnected("spawned", new Callable(this, nameof(OnSpawnerSpawned))))
				_spawner.Disconnect("spawned", new Callable(this, nameof(OnSpawnerSpawned)));
			if (_spawner.IsConnected("despawned", new Callable(this, nameof(OnSpawnerDespawned))))
				_spawner.Disconnect("despawned", new Callable(this, nameof(OnSpawnerDespawned)));
			// Intentionally do not clear spawn_path here. LoadGameRpc/LoadLobbyRpc always call
			// ChangeSceneToFile next; clearing spawn_path first triggers an extra replication
			// despawn pass that can race scene teardown and yield on_despawn_receive ERR_UNAUTHORIZED.
		}

		_spawner = null;
		_playerRoot = null;
		_playerSpawnRoot = null;
		_spawnPointRoot = null;
		_startupPlayer = null;
		_players.Clear();
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void UpdatePlayerTransformRpc(long peerId, Vector3 pos, Vector3 rot)
	{
		if (peerId == GetLocalPeerIdSafe())
			return;

		if (!_players.TryGetValue(peerId, out var player))
		{
			if (_playerRoot != null)
			{
				player = _playerRoot.GetNodeOrNull<PlayerController>($"Players/{_playerNodePrefix}{peerId}")
					?? _playerRoot.GetNodeOrNull<PlayerController>($"{_playerNodePrefix}{peerId}");
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
		if (!IsServerActive())
			return;

		int senderId = Multiplayer.GetRemoteSenderId();
		Rpc(nameof(UpdatePlayerTransformRpc), senderId, pos, rot);
	}
}
