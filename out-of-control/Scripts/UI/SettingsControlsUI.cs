using Godot;
using System.Collections.Generic;

public partial class SettingsControlsUI : Control
{
	[Export(PropertyHint.File, "*.tscn")] public string SettingsScenePath = "res://Scenes/UI/Settings.tscn";
	[Export] private VBoxContainer _actionsContainer;
	[Export] private HSlider _lookSensitivitySlider;
	[Export] private Label _lookSensitivityValueLabel;
	[Export] private Button _resetDefaultsButton;
	[Export] private Button _backButton;

	private readonly Dictionary<string, Button> _actionButtons = new();
	private string _pendingAction = "";
	private bool _captureNextInput = false;
	private bool _awaitMouseReleaseAfterBegin = false;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		InitSensitivitySlider();
		BuildActionList();
		_resetDefaultsButton.Pressed += OnResetDefaultsPressed;
		_backButton.Pressed += OnBackPressed;
	}

	private void InitSensitivitySlider()
	{
		if (_lookSensitivitySlider == null)
			return;

		_lookSensitivitySlider.MinValue = 0.25f;
		_lookSensitivitySlider.MaxValue = 3.0f;
		_lookSensitivitySlider.Step = 0.05f;
		_lookSensitivitySlider.Value = GameSettings.LookSensitivity;
		_lookSensitivitySlider.ValueChanged += OnLookSensitivityChanged;
		UpdateLookSensitivityLabel((float)_lookSensitivitySlider.Value);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_captureNextInput && !string.IsNullOrWhiteSpace(_pendingAction))
		{
			if (@event is InputEventKey key && key.Pressed && !key.Echo)
			{
				GameSettings.SetActionBinding(_pendingAction, (InputEvent)key.Duplicate());
				GameSettings.Save();
				_captureNextInput = false;
				_pendingAction = "";
				RefreshActionLabels();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (@event is InputEventMouseButton mouse && mouse.Pressed)
			{
				if (_awaitMouseReleaseAfterBegin)
					return;

				GameSettings.SetActionBinding(_pendingAction, (InputEvent)mouse.Duplicate());
				GameSettings.Save();
				_captureNextInput = false;
				_pendingAction = "";
				RefreshActionLabels();
				GetViewport().SetInputAsHandled();
				return;
			}

			if (@event is InputEventMouseButton mouseRelease && !mouseRelease.Pressed)
			{
				_awaitMouseReleaseAfterBegin = false;
			}
		}

		if (Input.IsActionJustPressed("pause"))
			OnBackPressed();
	}

	private void BuildActionList()
	{
		foreach (Node child in _actionsContainer.GetChildren())
			child.QueueFree();
		_actionButtons.Clear();

		foreach (var action in GameSettings.GetBindableActions())
		{
			if (!InputMap.HasAction(action))
				continue;

			var row = new HBoxContainer();
			row.SizeFlagsHorizontal = SizeFlags.ExpandFill;

			var label = new Label();
			label.Text = ToFriendlyActionName(action);
			label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			row.AddChild(label);

			var bindButton = new Button();
			bindButton.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			var capturedAction = action;
			bindButton.Pressed += () => BeginRebind(capturedAction);
			row.AddChild(bindButton);

			_actionsContainer.AddChild(row);
			_actionButtons[action] = bindButton;
		}

		RefreshActionLabels();
	}

	private void BeginRebind(string action)
	{
		_pendingAction = action;
		_captureNextInput = true;
		_awaitMouseReleaseAfterBegin = true;
		RefreshActionLabels();
	}

	private void RefreshActionLabels()
	{
		foreach (var kv in _actionButtons)
		{
			if (kv.Value == null)
				continue;

			if (_captureNextInput && _pendingAction == kv.Key)
			{
				kv.Value.Text = "Press any key...";
				continue;
			}

			var eventBinding = GameSettings.GetPrimaryBinding(kv.Key);
			kv.Value.Text = eventBinding?.AsText() ?? "Unbound";
		}
	}

	private static string ToFriendlyActionName(string action)
	{
		if (string.IsNullOrWhiteSpace(action))
			return "";

		var words = action.Replace("_", " ").Split(" ");
		for (int i = 0; i < words.Length; i++)
		{
			if (string.IsNullOrWhiteSpace(words[i]))
				continue;

			var lower = words[i].ToLowerInvariant();
			words[i] = char.ToUpperInvariant(lower[0]) + lower.Substring(1);
		}

		return string.Join(" ", words);
	}

	private void OnBackPressed()
	{
		if (TryNavigateInOverlay(SettingsScenePath))
			return;

		GetTree().ChangeSceneToFile(SettingsScenePath);
	}

	private void OnResetDefaultsPressed()
	{
		GameSettings.ResetBindingsToDefaults();
		GameSettings.SetLookSensitivity(1.0f);
		GameSettings.Save();
		_captureNextInput = false;
		_pendingAction = "";
		_awaitMouseReleaseAfterBegin = false;
		if (_lookSensitivitySlider != null)
			_lookSensitivitySlider.Value = GameSettings.LookSensitivity;
		UpdateLookSensitivityLabel(GameSettings.LookSensitivity);
		RefreshActionLabels();
	}

	private void OnLookSensitivityChanged(double value)
	{
		GameSettings.SetLookSensitivity((float)value);
		GameSettings.Save();
		UpdateLookSensitivityLabel((float)value);
	}

	private void UpdateLookSensitivityLabel(float value)
	{
		if (_lookSensitivityValueLabel != null)
			_lookSensitivityValueLabel.Text = $"{value:0.00}x";
	}

	private bool TryNavigateInOverlay(string scenePath)
	{
		for (Node n = this; n != null; n = n.GetParent())
		{
			if (n is ISettingsOverlayHost host)
			{
				host.NavigateSettings(scenePath);
				return true;
			}
		}

		return false;
	}
}
