using Godot;
using System.Collections.Generic;

public partial class GameAudio : Node
{
	private const string NetworkManagerNodeName = "NetworkManager";
	private const int DefaultSfxPoolSize = 16;
	private readonly Dictionary<string, AudioStream> _clips = new();
	private readonly HashSet<ulong> _hookedButtons = new();
	private readonly RandomNumberGenerator _rng = new();
	private readonly List<AudioStreamPlayer> _sfxPlayers = new();
	private int _nextSfxPlayerIndex = 0;

	private AudioStreamPlayer _musicPlayer;
	private NetworkManager _networkManager;
	private RoundManager _roundManager;
	private Callable _roundChangedCallable;
	private Callable _combatFeedbackCallable;
	private RoundPhase _lastRoundPhase = RoundPhase.Lobby;
	private int _lastGameplayMusicIndex = -1;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_rng.Randomize();

		_musicPlayer = new AudioStreamPlayer
		{
			Name = "Music",
			Bus = "Music",
			Autoplay = false
		};
		AddChild(_musicPlayer);
		_musicPlayer.Finished += () =>
		{
			if (_musicPlayer.Stream != null)
				_musicPlayer.Play();
		};
		InitializeSfxPool(DefaultSfxPoolSize);

		LoadClips();
		PlaySfx("Startup", bus: "SFX");
		BindSignals();
		HookUiSoundsForCurrentScene();
		UpdateMusicForCurrentScene(forceRestart: true);
		GetTree().SceneChanged += OnSceneChanged;
	}

	public override void _ExitTree()
	{
		if (GetTree() != null)
			GetTree().SceneChanged -= OnSceneChanged;

		if (_roundManager != null && _roundManager.IsConnected(nameof(RoundManager.RoundChanged), _roundChangedCallable))
			_roundManager.Disconnect(nameof(RoundManager.RoundChanged), _roundChangedCallable);
		if (_networkManager != null && _networkManager.IsConnected("CombatFeedback", _combatFeedbackCallable))
			_networkManager.Disconnect("CombatFeedback", _combatFeedbackCallable);
	}

	public static GameAudio Get(Node context)
	{
		return context?.GetTree()?.Root?.GetNodeOrNull<GameAudio>("GameAudio");
	}

	public static void PlayReady(Node context) => Get(context)?.PlaySfx("Ready", bus: "SFX");
	public static void PlayHit(Node context) => Get(context)?.PlaySfx("Hit", bus: "SFX");
	public static void PlayDie(Node context) => Get(context)?.PlaySfx("Die", bus: "SFX");
	public static void PlayUiAccent(Node context) => Get(context)?.PlaySfx("AssortedGUI", bus: "SFX");
	public static void PlayUiHover(Node context) => Get(context)?.PlaySfx("Hover", bus: "SFX");
	public static void PlayUiClick(Node context) => Get(context)?.PlaySfx("Shutter", bus: "SFX");

	private void OnSceneChanged()
	{
		_hookedButtons.Clear();
		BindSignals();
		HookUiSoundsForCurrentScene();
		UpdateMusicForCurrentScene(forceRestart: true);
	}

	private void BindSignals()
	{
		_networkManager = GetTree().Root.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName)
			?? GetTree().CurrentScene?.GetNodeOrNull<NetworkManager>(NetworkManagerNodeName);
		_roundManager = _networkManager?.GetRoundManager();

		_roundChangedCallable = new Callable(this, nameof(OnRoundChanged));
		if (_roundManager != null && !_roundManager.IsConnected(nameof(RoundManager.RoundChanged), _roundChangedCallable))
			_roundManager.Connect(nameof(RoundManager.RoundChanged), _roundChangedCallable);

		_combatFeedbackCallable = new Callable(this, nameof(OnCombatFeedback));
		if (_networkManager != null && !_networkManager.IsConnected("CombatFeedback", _combatFeedbackCallable))
			_networkManager.Connect("CombatFeedback", _combatFeedbackCallable);

		if (_roundManager != null)
			_lastRoundPhase = _roundManager.Phase;
	}

	private void OnCombatFeedback(string message, bool hit, bool killed, bool headshot)
	{
		if (hit)
			PlaySfx("Hit", bus: "SFX");
	}

	private void OnRoundChanged()
	{
		if (_roundManager == null)
			return;

		var phase = _roundManager.Phase;
		if (phase == _lastRoundPhase)
			return;

		if (phase == RoundPhase.Countdown)
			PlaySfx("Ready", bus: "SFX");
		else if (phase == RoundPhase.Playing)
			PlaySfx("GameStart", bus: "SFX");
		else if (phase == RoundPhase.RoundOver)
			PlaySfx("NextMatchTransition", bus: "SFX");

		_lastRoundPhase = phase;
	}

	private void HookUiSoundsForCurrentScene()
	{
		var scene = GetTree().CurrentScene;
		if (scene == null)
			return;

		HookUiSoundsRecursive(scene);
	}

	private void HookUiSoundsRecursive(Node node)
	{
		if (node is BaseButton button)
		{
			var id = button.GetInstanceId();
			if (_hookedButtons.Add(id))
			{
				button.MouseEntered += () => PlaySfx("Hover", bus: "SFX");
				button.Pressed += () => PlaySfx("Shutter", bus: "SFX");
			}
		}

		for (int i = 0; i < node.GetChildCount(); i++)
			HookUiSoundsRecursive(node.GetChild(i));
	}

	private void UpdateMusicForCurrentScene(bool forceRestart)
	{
		var path = GetTree().CurrentScene?.SceneFilePath ?? "";
		if (path.StartsWith("res://Scenes/UI/Lobby", System.StringComparison.OrdinalIgnoreCase))
		{
			PlayMusic("LobbyBG", forceRestart);
			return;
		}

		if (path.StartsWith("res://Scenes/Levels/", System.StringComparison.OrdinalIgnoreCase))
		{
			var gameplayTracks = new[] { "GameBG1", "GameBG2" };
			var index = _rng.RandiRange(0, gameplayTracks.Length - 1);
			if (index == _lastGameplayMusicIndex)
				index = (index + 1) % gameplayTracks.Length;
			_lastGameplayMusicIndex = index;
			PlayMusic(gameplayTracks[index], forceRestart);
		}
	}

	private void PlayMusic(string key, bool forceRestart)
	{
		if (!_clips.TryGetValue(key, out var stream) || stream == null || _musicPlayer == null)
			return;

		if (!forceRestart && _musicPlayer.Stream == stream && _musicPlayer.Playing)
			return;

		_musicPlayer.Stream = stream;
		_musicPlayer.Play();
	}

	private void PlaySfx(string key, string bus = "SFX")
	{
		if (!_clips.TryGetValue(key, out var stream) || stream == null)
			return;

		if (_sfxPlayers.Count == 0)
			InitializeSfxPool(DefaultSfxPoolSize);

		var player = _sfxPlayers[_nextSfxPlayerIndex];
		_nextSfxPlayerIndex = (_nextSfxPlayerIndex + 1) % _sfxPlayers.Count;
		player.Stream = stream;
		player.Bus = bus;
		player.VolumeDb = 0.0f;
		player.Play();
	}

	private void InitializeSfxPool(int requestedSize)
	{
		var targetSize = Mathf.Max(4, requestedSize);
		for (int i = _sfxPlayers.Count; i < targetSize; i++)
		{
			var player = new AudioStreamPlayer
			{
				Name = $"Sfx_{i}",
				Autoplay = false
			};
			AddChild(player);
			_sfxPlayers.Add(player);
		}
	}

	private void LoadClips()
	{
		// FLAC import is not available in this project/runtime, so map the accent
		// key to an available short GUI clip.
		_clips["AssortedGUI"] = GD.Load<AudioStream>("res://Assets/Audio/Ready.wav");
		_clips["Die"] = GD.Load<AudioStream>("res://Assets/Audio/Die.mp3");
		_clips["GameBG1"] = GD.Load<AudioStream>("res://Assets/Audio/GameBG1.wav");
		_clips["GameBG2"] = GD.Load<AudioStream>("res://Assets/Audio/GameBG2.mp3");
		_clips["GameStart"] = GD.Load<AudioStream>("res://Assets/Audio/GameStart.wav");
		_clips["Hit"] = GD.Load<AudioStream>("res://Assets/Audio/Hit.wav");
		_clips["Hover"] = GD.Load<AudioStream>("res://Assets/Audio/Hover.wav");
		_clips["LobbyBG"] = GD.Load<AudioStream>("res://Assets/Audio/LobbyBG.wav");
		_clips["NextMatchTransition"] = GD.Load<AudioStream>("res://Assets/Audio/NextMatchTransition.wav");
		_clips["Ready"] = GD.Load<AudioStream>("res://Assets/Audio/Ready.wav");
		_clips["Shutter"] = GD.Load<AudioStream>("res://Assets/Audio/Shutter.mp3");
		_clips["Startup"] = GD.Load<AudioStream>("res://Assets/Audio/Startup.wav");
	}
}
