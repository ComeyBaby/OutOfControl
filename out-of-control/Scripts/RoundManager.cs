using Godot;
using System.Collections.Generic;

public enum RoundPhase
{
	Lobby,
	PerkSelection,
	Countdown,
	Playing,
	RoundOver,
	ReturningToLobby
}

public partial class RoundManager : Node
{
	[Signal] public delegate void RoundChangedEventHandler();
	[Signal] public delegate void ScoreboardChangedEventHandler();

	[Export] public float CountdownSeconds = 3.0f;
	[Export] public float RoundSeconds = 180.0f;
	[Export] public float ReturnToLobbySeconds = 8.0f;
	[Export] public float SnapshotBroadcastIntervalSeconds = 0.15f;

	private NetworkManager _networkManager;
	private readonly Dictionary<long, int> _kills = new();
	private readonly Dictionary<long, int> _deaths = new();
	private readonly Dictionary<long, bool> _alive = new();
	private readonly Dictionary<long, bool> _perkSelectionReady = new();
	private readonly HashSet<long> _roundParticipants = new();
	private RoundPhase _phase = RoundPhase.Lobby;
	private float _phaseRemaining = 0.0f;
	private float _snapshotBroadcastAccumulator = 0.0f;
	private long _winnerPeerId = -1;
	private string _announcement = "Waiting for players";

	public RoundPhase Phase => _phase;
	public float PhaseRemaining => _phaseRemaining;
	public long WinnerPeerId => _winnerPeerId;
	public string Announcement => _announcement;

	public void Initialize(NetworkManager networkManager)
	{
		_networkManager = networkManager;
		ProcessMode = ProcessModeEnum.Always;
	}

	public override void _Process(double delta)
	{
		if (_networkManager == null || !_networkManager.IsHosting())
			return;

		if (_phase != RoundPhase.Countdown && _phase != RoundPhase.Playing && _phase != RoundPhase.RoundOver && _phase != RoundPhase.ReturningToLobby)
			return;

		_phaseRemaining = Mathf.Max(0.0f, _phaseRemaining - (float)delta);
		_snapshotBroadcastAccumulator += (float)delta;
		switch (_phase)
		{
			case RoundPhase.Countdown:
				BroadcastRoundSnapshotIfDue();
				if (_phaseRemaining <= 0.0f)
					BeginPlaying();
				break;
			case RoundPhase.Playing:
				BroadcastRoundSnapshotIfDue();
				if (_phaseRemaining <= 0.0f)
					EndRound(GetLeader(), "Time is up");
				break;
			case RoundPhase.RoundOver:
				BroadcastRoundSnapshotIfDue();
				if (_phaseRemaining <= 0.0f)
				{
					_phase = RoundPhase.ReturningToLobby;
					_phaseRemaining = 1.0f;
					_snapshotBroadcastAccumulator = 0.0f;
					BroadcastRoundSnapshot();
				}
				break;
			case RoundPhase.ReturningToLobby:
				if (_phaseRemaining <= 0.0f)
					_networkManager.RestartGameFromRoundManager();
				break;
		}
	}

	public void ResetToLobby()
	{
		_phase = RoundPhase.Lobby;
		_phaseRemaining = 0.0f;
		_winnerPeerId = -1;
		_announcement = "Waiting for players";
		_perkSelectionReady.Clear();
		_roundParticipants.Clear();
		EmitAllChanged();
	}

	public void StartRoundForPlayers(IEnumerable<long> peerIds)
	{
		if (_networkManager == null || !_networkManager.IsHosting())
			return;

		_kills.Clear();
		_deaths.Clear();
		_alive.Clear();
		_perkSelectionReady.Clear();
		_roundParticipants.Clear();

		foreach (var peerId in peerIds)
		{
			_kills[peerId] = 0;
			_deaths[peerId] = 0;
			_alive[peerId] = true;
			_perkSelectionReady[peerId] = false;
			_roundParticipants.Add(peerId);
		}

		_winnerPeerId = -1;
		_phase = RoundPhase.PerkSelection;
		_phaseRemaining = 0.0f;
		UpdateAnnouncement("Choose a perk");
		BroadcastRoundSnapshot();
	}

	public void NotifyElimination(long attackerPeerId, long victimPeerId)
	{
		if (_networkManager == null || !_networkManager.IsHosting() || _phase != RoundPhase.Playing)
			return;

		EnsurePlayer(victimPeerId);
		_alive[victimPeerId] = false;
		_deaths[victimPeerId] = GetDeaths(victimPeerId) + 1;

		if (attackerPeerId > 0 && attackerPeerId != victimPeerId)
		{
			EnsurePlayer(attackerPeerId);
			_kills[attackerPeerId] = GetKills(attackerPeerId) + 1;
		}

		var attackerName = attackerPeerId > 0 ? _networkManager.GetPlayerName(attackerPeerId) : "The arena";
		var victimName = _networkManager.GetPlayerName(victimPeerId);
		UpdateAnnouncement($"{attackerName} eliminated {victimName}");
		BroadcastRoundSnapshot();
		CheckWinCondition();
	}

	public void RemovePlayer(long peerId)
	{
		_alive.Remove(peerId);
		_perkSelectionReady.Remove(peerId);
		_roundParticipants.Remove(peerId);

		if (_networkManager != null && _networkManager.IsHosting())
		{
			BroadcastRoundSnapshot();
			if (_phase == RoundPhase.PerkSelection && IsEveryonePerkSelectionReady())
				BeginCountdown();
			CheckWinCondition();
		}
	}

	public void AddLateJoinSpectator(long peerId)
	{
		if (_networkManager == null || !_networkManager.IsHosting() || peerId <= 0)
			return;

		_kills.TryAdd(peerId, 0);
		_deaths.TryAdd(peerId, 0);
		_alive[peerId] = false;
		_perkSelectionReady[peerId] = false;
		BroadcastRoundSnapshot();
	}

	public void SetPerkSelectionReady(long peerId)
	{
		if (_networkManager == null || !_networkManager.IsHosting() || _phase != RoundPhase.PerkSelection)
			return;
		if (!_roundParticipants.Contains(peerId))
			return;

		EnsurePlayer(peerId);
		_perkSelectionReady[peerId] = true;
		UpdateAnnouncement($"{_networkManager.GetPlayerName(peerId)} picked a perk");
		BroadcastRoundSnapshot();

		if (IsEveryonePerkSelectionReady())
			BeginCountdown();
	}

	public int GetKills(long peerId)
	{
		return _kills.TryGetValue(peerId, out var kills) ? kills : 0;
	}

	public int GetDeaths(long peerId)
	{
		return _deaths.TryGetValue(peerId, out var deaths) ? deaths : 0;
	}

	public bool IsAlive(long peerId)
	{
		return _alive.TryGetValue(peerId, out var alive) && alive;
	}

	private void BeginPlaying()
	{
		_phase = RoundPhase.Playing;
		_phaseRemaining = RoundSeconds;
		UpdateAnnouncement("Fight");
		BroadcastRoundSnapshot();
		CheckWinCondition();
	}

	private void BeginCountdown()
	{
		_phase = RoundPhase.Countdown;
		_phaseRemaining = CountdownSeconds;
		BroadcastRoundSnapshot();
	}

	private void CheckWinCondition()
	{
		if (_phase != RoundPhase.Playing)
			return;

		var alivePlayers = new List<long>();
		foreach (var peerId in _roundParticipants)
		{
			if (IsAlive(peerId))
				alivePlayers.Add(peerId);
		}

		if (alivePlayers.Count <= 1)
		{
			var winner = alivePlayers.Count == 1 ? alivePlayers[0] : GetLeader();
			EndRound(winner, winner > 0 ? $"{_networkManager.GetPlayerName(winner)} wins" : "No winner");
		}
	}

	private void EndRound(long winnerPeerId, string message)
	{
		_phase = RoundPhase.RoundOver;
		_phaseRemaining = ReturnToLobbySeconds;
		_winnerPeerId = winnerPeerId;

		UpdateAnnouncement(message);
		BroadcastRoundSnapshot();
	}

	private bool IsEveryonePerkSelectionReady()
	{
		var ids = new List<long>(_roundParticipants);
		if (ids.Count == 0)
			return false;

		foreach (var peerId in ids)
		{
			if (!_perkSelectionReady.TryGetValue(peerId, out var ready) || !ready)
				return false;
		}

		return true;
	}

	private long GetLeader()
	{
		long leader = -1;
		var bestKills = int.MinValue;
		var bestDeaths = int.MaxValue;
		foreach (var peerId in _roundParticipants)
		{
			var kills = GetKills(peerId);
			var deaths = GetDeaths(peerId);
			if (kills > bestKills || (kills == bestKills && deaths < bestDeaths))
			{
				leader = peerId;
				bestKills = kills;
				bestDeaths = deaths;
			}
		}

		return leader;
	}

	private List<long> GetKnownPeerIds()
	{
		var ids = new HashSet<long>();
		foreach (var peerId in _roundParticipants)
			ids.Add(peerId);
		foreach (var peerId in _networkManager.GetSpawnedPlayerIds())
			ids.Add(peerId);
		foreach (var peerId in _kills.Keys)
			ids.Add(peerId);
		foreach (var peerId in _deaths.Keys)
			ids.Add(peerId);

		var list = new List<long>(ids);
		list.Sort();
		return list;
	}

	private void EnsurePlayer(long peerId)
	{
		if (peerId <= 0)
			return;

		_kills.TryAdd(peerId, 0);
		_deaths.TryAdd(peerId, 0);
		if (_phase == RoundPhase.Playing || _phase == RoundPhase.Countdown || _phase == RoundPhase.RoundOver)
			_alive.TryAdd(peerId, false);
		else
			_alive.TryAdd(peerId, true);
		_perkSelectionReady.TryAdd(peerId, false);
	}

	public int GetAliveCount()
	{
		var count = 0;
		foreach (var peerId in _roundParticipants)
		{
			if (IsAlive(peerId))
				count++;
		}
		return count;
	}

	public int GetParticipantCount()
	{
		return _roundParticipants.Count;
	}

	private void UpdateAnnouncement(string message)
	{
		if (_announcement == message)
			return;

		_announcement = message;
	}

	private void BroadcastRoundSnapshot()
	{
		var ids = GetKnownPeerIds();
		var kills = new Godot.Collections.Array<int>();
		var deaths = new Godot.Collections.Array<int>();
		var alive = new Godot.Collections.Array<bool>();
		var peerIds = new Godot.Collections.Array<long>();

		foreach (var peerId in ids)
		{
			peerIds.Add(peerId);
			kills.Add(GetKills(peerId));
			deaths.Add(GetDeaths(peerId));
			alive.Add(IsAlive(peerId));
		}

		Rpc(nameof(ApplyRoundSnapshotRpc), (int)_phase, _phaseRemaining, _winnerPeerId, _announcement, peerIds, kills, deaths, alive);
	}

	private void BroadcastRoundSnapshotIfDue()
	{
		var interval = Mathf.Max(0.05f, SnapshotBroadcastIntervalSeconds);
		if (_snapshotBroadcastAccumulator < interval)
			return;

		_snapshotBroadcastAccumulator = 0.0f;
		BroadcastRoundSnapshot();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void ApplyRoundSnapshotRpc(int phase, float phaseRemaining, long winnerPeerId, string announcement, Godot.Collections.Array<long> peerIds, Godot.Collections.Array<int> kills, Godot.Collections.Array<int> deaths, Godot.Collections.Array<bool> alive)
	{
		_phase = (RoundPhase)phase;
		_phaseRemaining = phaseRemaining;
		_winnerPeerId = winnerPeerId;
		_announcement = announcement;

		_kills.Clear();
		_deaths.Clear();
		_alive.Clear();
		_roundParticipants.Clear();

		for (int i = 0; i < peerIds.Count; i++)
		{
			var peerId = peerIds[i];
			_roundParticipants.Add(peerId);
			_kills[peerId] = i < kills.Count ? kills[i] : 0;
			_deaths[peerId] = i < deaths.Count ? deaths[i] : 0;
			_alive[peerId] = i < alive.Count && alive[i];
		}

		EmitAllChanged();
	}

	private void EmitAllChanged()
	{
		EmitSignal(nameof(RoundChanged));
		EmitSignal(nameof(ScoreboardChanged));
	}
}
