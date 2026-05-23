using Godot;
using System;
using System.Text;

public partial class RoundLeaderboardUI : Control
{
	private const string NetworkManagerNodeName = "NetworkManager";
	private const float RefreshIntervalSeconds = 0.2f;
	private const float RebindIntervalSeconds = 1.0f;

	[Export] private NodePath _titlePath = new("Panel/Margin/Content/Title");
	[Export] private NodePath _subtitlePath = new("Panel/Margin/Content/SubTitle");
	[Export] private NodePath _rowsPath = new("Panel/Margin/Content/Rows");
	[Export] private NodePath _rowTemplatePath = new("Panel/Margin/Content/Rows/HBoxContainer2");

	private Label _title;
	private Label _subTitle;
	private VBoxContainer _rows;
	private HBoxContainer _rowTemplate;
	private NetworkManager _networkManager;
	private RoundManager _roundManager;
	private float _refreshAccumulator = 0.0f;
	private float _rebindAccumulator = 0.0f;
	private string _lastRowsSignature = "";
	private string _lastTitle = "";
	private string _lastSubTitle = "";

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_title = GetNodeOrNull<Label>(_titlePath);
		_subTitle = GetNodeOrNull<Label>(_subtitlePath);
		_rows = GetNodeOrNull<VBoxContainer>(_rowsPath);
		_rowTemplate = GetNodeOrNull<HBoxContainer>(_rowTemplatePath);
		if (_rowTemplate != null)
			_rowTemplate.Visible = false;

		BindRoundManager();
		Refresh();
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
			Refresh();
		}
	}

	private void BindRoundManager()
	{
		_networkManager = GetTree().Root.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName)
			?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName);
		_roundManager = _networkManager?.GetRoundManager();
	}

	private void Refresh()
	{
		if (_roundManager == null || _networkManager == null)
		{
			Visible = false;
			return;
		}

		var phase = _roundManager.Phase;
		var show = phase == RoundPhase.RoundOver || phase == RoundPhase.MatchOver;
		if (!show)
		{
			Visible = false;
			return;
		}

		Visible = true;
		UpdateHeader(phase);
		UpdateRows(phase);
	}

	private void UpdateHeader(RoundPhase phase)
	{
		if (_title == null || _subTitle == null)
			return;

		var title = phase == RoundPhase.MatchOver
			? "Final Leaderboard"
			: $"Round {_roundManager.CurrentRound}/{_roundManager.MaxRounds} Complete";

		var countdown = Mathf.Max(0, Mathf.CeilToInt(_roundManager.PhaseRemaining));
		var subTitle = phase == RoundPhase.MatchOver
			? $"Returning to lobby in {countdown}s"
			: $"Next round in {countdown}s";

		if (_lastTitle != title)
		{
			_lastTitle = title;
			_title.Text = title;
		}

		if (_lastSubTitle != subTitle)
		{
			_lastSubTitle = subTitle;
			_subTitle.Text = subTitle;
		}
	}

	private void UpdateRows(RoundPhase phase)
	{
		if (_rows == null || _rowTemplate == null)
			return;

		var ids = _roundManager.GetKnownPeerIdsSnapshot();
		Array.Sort(ids, ComparePeers);
		var signature = BuildRowsSignature(ids, phase);
		if (_lastRowsSignature == signature)
			return;

		_lastRowsSignature = signature;
		RebuildRows(ids, phase);
	}

	private string BuildRowsSignature(long[] ids, RoundPhase phase)
	{
		var sb = new StringBuilder();
		sb.Append((int)phase).Append('|').Append(_roundManager.MatchWinnerPeerId).Append('|');
		for (int i = 0; i < ids.Length; i++)
		{
			var id = ids[i];
			sb.Append(id)
				.Append(':')
				.Append(_networkManager.GetPlayerName(id))
				.Append(':')
				.Append(_roundManager.GetWins(id))
				.Append(';');
		}
		return sb.ToString();
	}

	private void RebuildRows(long[] ids, RoundPhase phase)
	{
		for (int i = _rows.GetChildCount() - 1; i >= 0; i--)
		{
			var child = _rows.GetChild(i);
			if (child == _rowTemplate)
				continue;
			child.QueueFree();
		}

		var matchWinnerId = _roundManager.MatchWinnerPeerId;
		for (int i = 0; i < ids.Length; i++)
		{
			if (_rowTemplate.Duplicate() is not HBoxContainer row)
				continue;

			row.Visible = true;
			row.Name = $"HBoxContainer2_{i + 1}";
			_rows.AddChild(row);
			_rows.MoveChild(row, _rows.GetChildCount() - 1);

			var nameLabel = row.GetNodeOrNull<Label>("Name");
			var winsLabel = row.GetNodeOrNull<Label>("Wins");
			if (nameLabel == null || winsLabel == null)
				continue;

			var peerId = ids[i];
			var playerName = _networkManager.GetPlayerName(peerId);
			if (string.IsNullOrWhiteSpace(playerName))
				playerName = $"Player {peerId}";

			if (phase == RoundPhase.MatchOver && matchWinnerId > 0 && peerId == matchWinnerId)
				playerName = $"{playerName} (Winner)";

			nameLabel.Text = playerName;
			winsLabel.Text = _roundManager.GetWins(peerId).ToString();
		}
	}

	private int ComparePeers(long a, long b)
	{
		var winsCmp = _roundManager.GetWins(b).CompareTo(_roundManager.GetWins(a));
		if (winsCmp != 0)
			return winsCmp;

		var killsCmp = _roundManager.GetKills(b).CompareTo(_roundManager.GetKills(a));
		if (killsCmp != 0)
			return killsCmp;

		var deathsCmp = _roundManager.GetDeaths(a).CompareTo(_roundManager.GetDeaths(b));
		if (deathsCmp != 0)
			return deathsCmp;

		return a.CompareTo(b);
	}
}
