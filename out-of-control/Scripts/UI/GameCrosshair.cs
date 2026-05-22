using Godot;

public partial class GameCrosshair : Control
{
	private const string NetworkManagerNodeName = "NetworkManager";
	private const string CombatFeedbackSignalName = "CombatFeedback";

	[Export] private Label _combatFeedbackLabel;

	private float _combatFeedbackRemaining;
	private NetworkManager _networkManager;
	private Callable _combatFeedbackCallable;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		if (_combatFeedbackLabel == null)
			_combatFeedbackLabel = GetNodeOrNull<Label>("CombatFeedback");

		var player = FindOwningPlayer();
		if (player == null || !player.IsMultiplayerAuthority())
		{
			Visible = false;
			ProcessMode = ProcessModeEnum.Disabled;
			return;
		}

		_combatFeedbackCallable = new Callable(this, nameof(OnCombatFeedback));
		TryBindNetworkManager();
		GetTree().SceneChanged += OnSceneChanged;
	}

	public override void _ExitTree()
	{
		if (GetTree() != null)
			GetTree().SceneChanged -= OnSceneChanged;

		if (_networkManager != null &&
			_networkManager.IsConnected(CombatFeedbackSignalName, _combatFeedbackCallable))
			_networkManager.Disconnect(CombatFeedbackSignalName, _combatFeedbackCallable);
	}

	public override void _Process(double delta)
	{
		TickFeedbackDecay(delta);
	}

	public void SetReticleVisible(bool visible)
	{
		Visible = visible;
	}

	private void OnCombatFeedback(string message, bool hit, bool killed, bool headshot)
	{
		if (_combatFeedbackLabel == null)
			return;

		var prefix = headshot ? "HEADSHOT - " : "";
		_combatFeedbackLabel.Text = killed ? $"{prefix}{message.ToUpperInvariant()}" : $"{prefix}{message}";
		_combatFeedbackLabel.Modulate = killed
			? new Color(1f, 0.5f, 0.5f, 1f)
			: headshot
				? new Color(1f, 0.88f, 0.35f, 1f)
				: new Color(1f, 1f, 1f, 1f);
		_combatFeedbackLabel.Visible = true;
		_combatFeedbackRemaining = killed ? 1.4f : (headshot ? 0.75f : 0.35f);
	}

	private void TickFeedbackDecay(double delta)
	{
		if (_combatFeedbackRemaining <= 0.0f || _combatFeedbackLabel == null)
			return;

		_combatFeedbackRemaining = Mathf.Max(0.0f, _combatFeedbackRemaining - (float)delta);
		if (_combatFeedbackRemaining <= 0.0f)
			_combatFeedbackLabel.Visible = false;
	}

	private void OnSceneChanged()
	{
		TryBindNetworkManager();
	}

	private void TryBindNetworkManager()
	{
		var manager = GetTree().Root.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName)
			?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName);

		if (manager == _networkManager)
			return;

		if (_networkManager != null &&
			_networkManager.IsConnected(CombatFeedbackSignalName, _combatFeedbackCallable))
			_networkManager.Disconnect(CombatFeedbackSignalName, _combatFeedbackCallable);

		_networkManager = manager;
		if (_networkManager != null &&
			!_networkManager.IsConnected(CombatFeedbackSignalName, _combatFeedbackCallable))
			_networkManager.Connect(CombatFeedbackSignalName, _combatFeedbackCallable);
	}

	private PlayerController FindOwningPlayer()
	{
		for (Node n = GetParent(); n != null; n = n.GetParent())
		{
			if (n is PlayerController pc)
				return pc;
		}

		return null;
	}
}
