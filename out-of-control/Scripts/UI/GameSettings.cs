using Godot;
using System;
using System.Collections.Generic;

public static class GameSettings
{
	private const string SettingsPath = "user://settings.cfg";
	private const string AudioSection = "audio";
	private const string VideoSection = "video";
	private const string InputSection = "input";
	private const string ControlsSection = "controls";
	private const float MinUiScale = 0.75f;
	private const float MaxUiScale = 4.0f;
	private const float MinLookSensitivity = 0.25f;
	private const float MaxLookSensitivity = 3.0f;

	private static readonly Dictionary<string, float> _busVolumes = new();
	private static readonly Dictionary<string, InputEvent> _bindings = new();
	private static readonly Dictionary<string, InputEvent> _defaultBindings = new();
	private static bool _defaultsInitialized = false;
	private static bool _fullscreen = false;
	private static bool _showFps = false;
	private static bool _vSyncEnabled = false;
	private static float _uiScale = 1.0f;
	private static float _lookSensitivity = 1.0f;

	public static float UiScale => _uiScale;
	public static bool Fullscreen => _fullscreen;
	public static bool ShowFps => _showFps;
	public static bool VSyncEnabled => _vSyncEnabled;
	public static float LookSensitivity => _lookSensitivity;

	public static void LoadAndApply()
	{
		EnsureDefaultBindingsInitialized();
		_busVolumes.Clear();
		_bindings.Clear();
		_fullscreen = false;
		_showFps = false;
		_vSyncEnabled = false;
		_uiScale = 1.0f;
		_lookSensitivity = 1.0f;

		var config = new ConfigFile();
		if (config.Load(SettingsPath) != Error.Ok)
		{
			ApplyRuntimeSettings();
			return;
		}

		_busVolumes["Master"] = ConvertToFloat(config.GetValue(AudioSection, "master_db", 0.0f));
		_busVolumes["SFX"] = ConvertToFloat(config.GetValue(AudioSection, "sfx_db", 0.0f));
		_busVolumes["Music"] = ConvertToFloat(config.GetValue(AudioSection, "music_db", 0.0f));

		_fullscreen = (bool)config.GetValue(VideoSection, "fullscreen", false);
		_showFps = (bool)config.GetValue(VideoSection, "show_fps", false);
		_vSyncEnabled = (bool)config.GetValue(VideoSection, "vsync_enabled", false);
		_uiScale = Mathf.Clamp(ConvertToFloat(config.GetValue(VideoSection, "ui_scale", 1.0f)), MinUiScale, MaxUiScale);
		_lookSensitivity = Mathf.Clamp(ConvertToFloat(config.GetValue(ControlsSection, "look_sensitivity", 1.0f)), MinLookSensitivity, MaxLookSensitivity);

		if (config.HasSection(InputSection))
		{
			foreach (var actionObj in config.GetSectionKeys(InputSection))
			{
				var action = actionObj?.ToString() ?? "";
				if (string.IsNullOrWhiteSpace(action))
					continue;

				var encoded = config.GetValue(InputSection, action, "").ToString();
				var inputEvent = ParseInputEvent(encoded);
				if (inputEvent != null)
					_bindings[action] = inputEvent;
			}
		}

		ApplyRuntimeSettings();
	}

	public static void SetBusVolume(string busName, float db)
	{
		_busVolumes[busName] = db;
		ApplyBusVolume(busName, db);
	}

	public static float GetBusVolume(string busName, float fallbackDb = 0.0f)
	{
		if (_busVolumes.TryGetValue(busName, out var db))
			return db;

		var index = AudioServer.GetBusIndex(busName);
		if (index >= 0)
			return AudioServer.GetBusVolumeDb(index);

		return fallbackDb;
	}

	public static void SetFullscreen(bool fullscreen)
	{
		_fullscreen = fullscreen;
		DisplayServer.WindowSetMode(fullscreen
			? DisplayServer.WindowMode.Fullscreen
			: DisplayServer.WindowMode.Windowed);
	}

	public static void SetUiScale(float scale)
	{
		_uiScale = Mathf.Clamp(scale, MinUiScale, MaxUiScale);
		var root = Engine.GetMainLoop() as SceneTree;
		root?.Root?.Set("content_scale_factor", _uiScale);
	}

	public static void SetShowFps(bool showFps)
	{
		_showFps = showFps;
	}

	public static void SetVSyncEnabled(bool enabled)
	{
		_vSyncEnabled = enabled;
		DisplayServer.WindowSetVsyncMode(enabled
			? DisplayServer.VSyncMode.Enabled
			: DisplayServer.VSyncMode.Disabled);
	}

	public static void SetActionBinding(string action, InputEvent inputEvent)
	{
		if (string.IsNullOrWhiteSpace(action) || inputEvent == null || !InputMap.HasAction(action))
			return;

		InputMap.ActionEraseEvents(action);
		InputMap.ActionAddEvent(action, inputEvent);
		_bindings[action] = inputEvent;
	}

	public static void SetLookSensitivity(float sensitivity)
	{
		_lookSensitivity = Mathf.Clamp(sensitivity, MinLookSensitivity, MaxLookSensitivity);
	}

	public static void ResetBindingsToDefaults()
	{
		EnsureDefaultBindingsInitialized();

		foreach (var action in GetBindableActions())
		{
			if (!InputMap.HasAction(action))
				continue;

			InputMap.ActionEraseEvents(action);
			if (_defaultBindings.TryGetValue(action, out var binding) && binding != null)
			{
				var duplicate = (InputEvent)binding.Duplicate();
				InputMap.ActionAddEvent(action, duplicate);
				_bindings[action] = duplicate;
			}
			else
			{
				_bindings.Remove(action);
			}
		}
	}

	public static InputEvent GetPrimaryBinding(string action)
	{
		if (_bindings.TryGetValue(action, out var binding))
			return binding;

		if (!InputMap.HasAction(action))
			return null;

		var events = InputMap.ActionGetEvents(action);
		return events.Count > 0 ? events[0] : null;
	}

	public static void Save()
	{
		var config = new ConfigFile();

		config.SetValue(AudioSection, "master_db", GetBusVolume("Master", 0.0f));
		config.SetValue(AudioSection, "sfx_db", GetBusVolume("SFX", 0.0f));
		config.SetValue(AudioSection, "music_db", GetBusVolume("Music", 0.0f));

		config.SetValue(VideoSection, "fullscreen", _fullscreen);
		config.SetValue(VideoSection, "show_fps", _showFps);
		config.SetValue(VideoSection, "vsync_enabled", _vSyncEnabled);
		config.SetValue(VideoSection, "ui_scale", _uiScale);
		config.SetValue(ControlsSection, "look_sensitivity", _lookSensitivity);

		foreach (var kv in _bindings)
			config.SetValue(InputSection, kv.Key, EncodeInputEvent(kv.Value));

		config.Save(SettingsPath);
	}

	public static IEnumerable<string> GetBindableActions()
	{
		return new[]
		{
			"move_left",
			"move_right",
			"move_forward",
			"move_backward",
			"jump",
			"sprint",
			"freefly",
			"shoot",
			"reload",
			"pause"
		};
	}

	private static void ApplyRuntimeSettings()
	{
		EnsureDefaultBindingsInitialized();
		ApplyBusVolume("Master", GetBusVolume("Master", 0.0f));
		ApplyBusVolume("SFX", GetBusVolume("SFX", 0.0f));
		ApplyBusVolume("Music", GetBusVolume("Music", 0.0f));
		SetFullscreen(_fullscreen);
		SetVSyncEnabled(_vSyncEnabled);
		SetUiScale(_uiScale);

		foreach (var kv in _bindings)
		{
			if (!InputMap.HasAction(kv.Key))
				continue;

			InputMap.ActionEraseEvents(kv.Key);
			InputMap.ActionAddEvent(kv.Key, kv.Value);
		}
	}

	private static void ApplyBusVolume(string busName, float db)
	{
		var bus = AudioServer.GetBusIndex(busName);
		if (bus >= 0)
			AudioServer.SetBusVolumeDb(bus, db);
	}

	private static float ConvertToFloat(Variant value)
	{
		return value.VariantType switch
		{
			Variant.Type.Float => (float)value,
			Variant.Type.Int => (int)value,
			Variant.Type.String => float.TryParse(value.ToString(), out var parsed) ? parsed : 0.0f,
			_ => 0.0f
		};
	}

	private static string EncodeInputEvent(InputEvent inputEvent)
	{
		if (inputEvent is InputEventKey key)
			return $"key:{(int)key.Keycode}";

		if (inputEvent is InputEventMouseButton mouse)
			return $"mouse:{(int)mouse.ButtonIndex}";

		return "";
	}

	private static InputEvent ParseInputEvent(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
			return null;

		var parts = text.Split(':', 2);
		if (parts.Length == 2)
		{
			var kind = parts[0].Trim().ToLowerInvariant();
			if (int.TryParse(parts[1], out var code))
			{
				if (kind == "key")
				{
					return new InputEventKey
					{
						Keycode = (Key)code
					};
				}

				if (kind == "mouse")
				{
					return new InputEventMouseButton
					{
						ButtonIndex = (MouseButton)code,
						Pressed = true
					};
				}
			}
		}

		// Backward compatibility for older saves that stored text labels.
		// Only support mouse labels directly; skip key text parsing to avoid heavy enum scans.
		if (text.Contains("Mouse Left", StringComparison.OrdinalIgnoreCase))
		{
			return new InputEventMouseButton
			{
				ButtonIndex = MouseButton.Left,
				Pressed = true
			};
		}
		if (text.Contains("Mouse Right", StringComparison.OrdinalIgnoreCase))
		{
			return new InputEventMouseButton
			{
				ButtonIndex = MouseButton.Right,
				Pressed = true
			};
		}
		if (text.Contains("Mouse Middle", StringComparison.OrdinalIgnoreCase))
		{
			return new InputEventMouseButton
			{
				ButtonIndex = MouseButton.Middle,
				Pressed = true
			};
		}

		return null;
	}

	private static void EnsureDefaultBindingsInitialized()
	{
		if (_defaultsInitialized)
			return;

		_defaultBindings.Clear();
		foreach (var action in GetBindableActions())
		{
			if (!InputMap.HasAction(action))
				continue;

			var events = InputMap.ActionGetEvents(action);
			if (events.Count > 0 && events[0] != null)
				_defaultBindings[action] = (InputEvent)events[0].Duplicate();
		}

		_defaultsInitialized = true;
	}
}
