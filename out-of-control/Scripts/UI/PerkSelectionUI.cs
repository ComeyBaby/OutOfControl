using Godot;
using System.Collections.Generic;

public partial class PerkSelectionUI : Control
{
	[Signal] public delegate void PerkChosenEventHandler(PerkDefinition chosenPerk);

	[Export] private int _perkChoicesCount = 3;
	[Export] private PerkDefinition[] _perks = System.Array.Empty<PerkDefinition>();

	[Export] private Label _perk1Title;
	[Export] private RichTextLabel _perk1Description;
	[Export] private Button _perk1Button;
	[Export] private ColorRect _perk1LavaOverlay;

	[Export] private Label _perk2Title;
	[Export] private RichTextLabel _perk2Description;
	[Export] private Button _perk2Button;
	[Export] private ColorRect _perk2LavaOverlay;

	[Export] private Label _perk3Title;
	[Export] private RichTextLabel _perk3Description;
	[Export] private Button _perk3Button;
	[Export] private ColorRect _perk3LavaOverlay;

	private readonly List<PerkDefinition> _runtimePerks = new();
	private PlayerStats _localStats;

	public bool HasAvailablePerks => _runtimePerks.Count > 0;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		MouseFilter = MouseFilterEnum.Stop;

		if (_perk1Button != null)
			_perk1Button.Pressed += OnPickPerk1;
		if (_perk2Button != null)
			_perk2Button.Pressed += OnPickPerk2;
		if (_perk3Button != null)
			_perk3Button.Pressed += OnPickPerk3;

		RefreshPerks();
	}

	public override void _ExitTree()
	{
		if (_perk1Button != null)
			_perk1Button.Pressed -= OnPickPerk1;
		if (_perk2Button != null)
			_perk2Button.Pressed -= OnPickPerk2;
		if (_perk3Button != null)
			_perk3Button.Pressed -= OnPickPerk3;
	}

	public void RefreshPerks()
	{
		_runtimePerks.Clear();

		var source = _perks;
		var weapon = GetLocalWeaponName();
		var offeredPerks = PickPerks(source, Mathf.Max(1, _perkChoicesCount), weapon);
		_runtimePerks.AddRange(offeredPerks);

		ApplyCard(_perk1Title, _perk1Description, _perk1Button, 0);
		ApplyCard(_perk2Title, _perk2Description, _perk2Button, 1);
		ApplyCard(_perk3Title, _perk3Description, _perk3Button, 2);
		ApplyCardOverlayColor(_perk1LavaOverlay, 0);
		ApplyCardOverlayColor(_perk2LavaOverlay, 1);
		ApplyCardOverlayColor(_perk3LavaOverlay, 2);
	}

	public void SetLocalStats(PlayerStats stats)
	{
		_localStats = stats;
	}

	private List<PerkDefinition> PickPerks(PerkDefinition[] source, int count, string weapon)
	{
		var chosen = new List<PerkDefinition>();
		if (source == null || source.Length == 0)
			return chosen;

		var available = new List<PerkDefinition>();
		foreach (var perk in source)
		{
			if (perk != null && perk.IsConfigured && perk.IsAvailableForWeapon(weapon))
				available.Add(perk);
		}

		if (available.Count == 0)
			return chosen;

		var rng = new RandomNumberGenerator();
		rng.Randomize();

		var targetCount = Mathf.Min(count, available.Count);
		while (chosen.Count < targetCount && available.Count > 0)
		{
			var index = PickWeightedIndex(available, rng);
			chosen.Add(available[index]);
			available.RemoveAt(index);
		}

		return chosen;
	}

	private int PickWeightedIndex(List<PerkDefinition> perks, RandomNumberGenerator rng)
	{
		var totalWeight = 0;
		for (var i = 0; i < perks.Count; i++)
			totalWeight += GetRarityWeight(perks[i]);

		if (totalWeight <= 0)
			return rng.RandiRange(0, perks.Count - 1);

		var roll = rng.RandiRange(1, totalWeight);
		var cumulative = 0;
		for (var i = 0; i < perks.Count; i++)
		{
			cumulative += GetRarityWeight(perks[i]);
			if (roll <= cumulative)
				return i;
		}

		return perks.Count - 1;
	}

	private int GetRarityWeight(PerkDefinition perk)
	{
		if (perk == null)
			return 0;

		return perk.Rarity switch
		{
			PerkRarity.Common => 100,
			PerkRarity.Rare => 35,
			PerkRarity.Epic => 12,
			PerkRarity.Legendary => 3,
			_ => 100
		};
	}

	private void ApplyCard(Label titleLabel, RichTextLabel descriptionLabel, Button button, int index)
	{
		var perk = index < _runtimePerks.Count ? _runtimePerks[index] : null;
		var hasPerk = perk != null;

		if (titleLabel != null)
			titleLabel.Text = hasPerk && !string.IsNullOrWhiteSpace(perk.PerkName)
				? perk.PerkName
				: "No perk";

		if (descriptionLabel != null)
		{
			if (!hasPerk)
			{
				descriptionLabel.Text = "No perk available.";
			}
			else
			{
				descriptionLabel.Text = string.IsNullOrWhiteSpace(perk.Description)
					? "No description provided."
					: perk.Description;
			}
		}

		if (button != null)
		{
			button.Disabled = !hasPerk;
			button.Text = hasPerk ? "Choose" : "Unavailable";
		}
	}

	private void ApplyCardOverlayColor(ColorRect overlay, int index)
	{
		if (overlay == null)
			return;

		var perk = index < _runtimePerks.Count ? _runtimePerks[index] : null;
		var material = overlay.Material as ShaderMaterial;
		if (material == null)
			return;

		// Ensure each card has its own shader material instance so rarity colors can differ.
		if (!material.ResourceLocalToScene)
		{
			material = (ShaderMaterial)material.Duplicate();
			material.ResourceLocalToScene = true;
			overlay.Material = material;
		}

		var (dark, bright) = GetRarityGradient(perk?.Rarity);
		material.SetShaderParameter("color_dark", dark);
		material.SetShaderParameter("color_bright", bright);
	}

	private static (Color dark, Color bright) GetRarityGradient(PerkRarity? rarity)
	{
		return rarity switch
		{
			PerkRarity.Rare => (new Color("1e2e66"), new Color("6f8dff80")),
			PerkRarity.Epic => (new Color("3a1b52"), new Color("c871ff80")),
			PerkRarity.Legendary => (new Color("4a2a12"), new Color("ff8a3d99")),
			_ => (new Color("2f2f2f"), new Color("6b6b6b66"))
		};
	}

	private void OnPickPerk1()
	{
		ApplySelectedPerk(0);
	}

	private void OnPickPerk2()
	{
		ApplySelectedPerk(1);
	}

	private void OnPickPerk3()
	{
		ApplySelectedPerk(2);
	}

	private void ApplySelectedPerk(int index)
	{
		if (index < 0 || index >= _runtimePerks.Count)
			return;

		var stats = FindLocalPlayerStats();
		if (stats == null)
		{
			GD.PrintErr("PerkSelectionUI: no local player stats found to apply perk.");
			return;
		}

		var chosenPerk = _runtimePerks[index];
		stats.ApplyPerk(chosenPerk);
		EmitSignal(nameof(PerkChosen), chosenPerk);
	}

	private PlayerStats FindLocalPlayerStats()
	{
		if (_localStats != null && GodotObject.IsInstanceValid(_localStats))
			return _localStats;

		var scene = GetTree()?.CurrentScene;
		if (scene == null)
			return null;

		_localStats = FindLocalPlayerStats(scene);
		return _localStats;
	}

	private string GetLocalWeaponName()
	{
		var stats = FindLocalPlayerStats();
		return stats?.SelectedWeapon;
	}

	private PlayerStats FindLocalPlayerStats(Node root)
	{
		foreach (var child in root.GetChildren())
		{
			if (child is PlayerController player && HasLocalAuthority(player))
				return player.GetStats();

			var nested = FindLocalPlayerStats(child);
			if (nested != null)
				return nested;
		}

		return null;
	}

	private bool HasLocalAuthority(PlayerController player)
	{
		return player != null && (Multiplayer.MultiplayerPeer == null || player.IsMultiplayerAuthority());
	}
}
