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
			return "0:00";

		return FormatDuration(_roundManager.PhaseRemaining);
	}

	private static string FormatDuration(float secondsRemaining)
	{
		var totalSeconds = Mathf.Max(0, Mathf.CeilToInt(secondsRemaining));
		var minutes = totalSeconds / 60;
		var seconds = totalSeconds % 60;
		return $"{minutes}:{seconds:00}";
	}

}
