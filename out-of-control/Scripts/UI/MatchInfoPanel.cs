using Godot;

public partial class MatchInfoPanel : Control
{
	private const string NetworkManagerNodeName = "NetworkManager";
	private const float RefreshIntervalSeconds = 0.2f;
	private const float RebindIntervalSeconds = 1.0f;

	[Export] private NodePath _labelPath = new("Panel/Margin/Label");

	private Label _label;
	private NetworkManager _networkManager;
	private RoundManager _roundManager;
	private string _lastText = "";
	private float _refreshAccumulator = 0.0f;
	private float _rebindAccumulator = 0.0f;

	public override void _Ready()
	{
		_label = GetNodeOrNull<Label>(_labelPath);
		BindRoundManager();
		RefreshText();
	}

	public override void _Process(double delta)
	{
		_refreshAccumulator += (float)delta;
		_rebindAccumulator += (float)delta;

		if ((_roundManager == null || !GodotObject.IsInstanceValid(_roundManager)) &&
			_rebindAccumulator >= RebindIntervalSeconds)
		{
			_rebindAccumulator = 0.0f;
			BindRoundManager();
		}

		if (_refreshAccumulator >= RefreshIntervalSeconds)
		{
			_refreshAccumulator = 0.0f;
			RefreshText();
		}
	}

	private void BindRoundManager()
	{
		_networkManager = GetTree().Root.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName)
			?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName);
		_roundManager = _networkManager?.GetRoundManager();
	}

	private void RefreshText()
	{
		if (_label == null)
			return;

		var text = BuildText();
		if (text == _lastText)
			return;

		_lastText = text;
		_label.Text = text;
	}

	private string BuildText()
	{
		if (_roundManager == null)
			return "Waiting for match info";

		var header = _roundManager.Phase switch
		{
			RoundPhase.PerkSelection => "Choose a perk",
			RoundPhase.Countdown => $"Round starts in {FormatDuration(_roundManager.PhaseRemaining)}",
			RoundPhase.Playing => $"Fight - {FormatDuration(_roundManager.PhaseRemaining)} remaining",
			RoundPhase.RoundOver => $"Winner: {GetWinnerName()} - Next round in {FormatDuration(_roundManager.PhaseRemaining)}",
			RoundPhase.ReturningToLobby => $"Reloading map - {FormatDuration(_roundManager.PhaseRemaining)}",
			_ => "Waiting for players"
		};

		var aliveCount = _roundManager.GetAliveCount();
		var participantCount = _roundManager.GetParticipantCount();
		var announcement = string.IsNullOrWhiteSpace(_roundManager.Announcement) ? "" : _roundManager.Announcement;
		var leader = GetLeaderSummary();
		var actionHint = GetActionHint();
		return $"{header}\n{announcement}\nAlive: {aliveCount}/{participantCount} | Leader: {leader}\n{actionHint}";
	}

	private string GetWinnerName()
	{
		var winnerPeerId = _roundManager.WinnerPeerId;
		if (winnerPeerId <= 0 || _networkManager == null)
			return "No winner";

		return _networkManager.GetPlayerName(winnerPeerId);
	}

	private static string FormatDuration(float secondsRemaining)
	{
		var totalSeconds = Mathf.Max(0, Mathf.CeilToInt(secondsRemaining));
		var minutes = totalSeconds / 60;
		var seconds = totalSeconds % 60;
		return $"{minutes}:{seconds:00}";
	}

	private string GetLeaderSummary()
	{
		if (_roundManager == null || _networkManager == null)
			return "N/A";

		var ids = _networkManager.GetSpawnedPlayerIds();
		if (ids.Length == 0)
			return "N/A";

		long bestId = -1;
		int bestKills = int.MinValue;
		int bestDeaths = int.MaxValue;
		foreach (var peerId in ids)
		{
			var kills = _roundManager.GetKills(peerId);
			var deaths = _roundManager.GetDeaths(peerId);
			if (kills > bestKills || (kills == bestKills && deaths < bestDeaths))
			{
				bestId = peerId;
				bestKills = kills;
				bestDeaths = deaths;
			}
		}

		if (bestId <= 0)
			return "N/A";

		return $"{_networkManager.GetPlayerName(bestId)} ({bestKills}/{bestDeaths})";
	}

	private string GetActionHint()
	{
		if (_roundManager == null)
			return "";

		return _roundManager.Phase switch
		{
			RoundPhase.PerkSelection => "Next: Pick a perk to ready up.",
			RoundPhase.Countdown => "Next: Get in position.",
			RoundPhase.Playing => "Next: Eliminate opponents or survive the timer.",
			RoundPhase.RoundOver => "Next: Review results and prepare for next round.",
			_ => "Next: Wait for players."
		};
	}
}
