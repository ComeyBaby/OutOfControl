using Godot;

public partial class MatchInfoPanel : Control
{
	private const string NetworkManagerNodeName = "NetworkManager";

	[Export] private NodePath _labelPath = new("Panel/Margin/Label");

	private Label _label;
	private NetworkManager _networkManager;
	private RoundManager _roundManager;
	private string _lastText = "";

	public override void _Ready()
	{
		_label = GetNodeOrNull<Label>(_labelPath);
		BindRoundManager();
		RefreshText();
	}

	public override void _Process(double delta)
	{
		if (_roundManager == null || !GodotObject.IsInstanceValid(_roundManager))
			BindRoundManager();

		RefreshText();
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

		switch (_roundManager.Phase)
		{
			case RoundPhase.PerkSelection:
				return "Waiting for players to pick perks";
			case RoundPhase.Countdown:
				return $"Game starts in {FormatDuration(_roundManager.PhaseRemaining)}";
			case RoundPhase.Playing:
				return $"Match ends in {FormatDuration(_roundManager.PhaseRemaining)}";
			case RoundPhase.RoundOver:
			{
				var winnerName = GetWinnerName();
				return $"Winner: {winnerName} | Next game in {FormatDuration(_roundManager.PhaseRemaining)}";
			}
			case RoundPhase.ReturningToLobby:
				return $"Next game in {FormatDuration(_roundManager.PhaseRemaining)}";
			case RoundPhase.Lobby:
			default:
				return "Waiting for players";
		}
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
}
