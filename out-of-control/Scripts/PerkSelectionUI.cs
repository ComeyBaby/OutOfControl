using Godot;
using System.Collections.Generic;

public partial class PerkSelectionUI : Control
{
	[Signal] public delegate void PerkChosenEventHandler();

	[Export] private int _perkChoicesCount = 3;
	[Export] private PerkDefinition[] _perks = System.Array.Empty<PerkDefinition>();

	[Export] private Label _perk1Title;
	[Export] private RichTextLabel _perk1Description;
	[Export] private Button _perk1Button;

	[Export] private Label _perk2Title;
	[Export] private RichTextLabel _perk2Description;
	[Export] private Button _perk2Button;

	[Export] private Label _perk3Title;
	[Export] private RichTextLabel _perk3Description;
	[Export] private Button _perk3Button;

	private readonly List<PerkDefinition> _runtimePerks = new();

	public bool HasAvailablePerks => _runtimePerks.Count > 0;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

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
				? $"{perk.PerkName} [{perk.Rarity}]"
				: "No perk";

		if (descriptionLabel != null)
		{
			if (!hasPerk)
			{
				descriptionLabel.Text = "No perk available.";
			}
			else
			{
				var description = string.IsNullOrWhiteSpace(perk.Description)
					? "No description provided."
					: perk.Description;
				var weaponText = GetWeaponRestrictionText(perk);
				descriptionLabel.Text = string.IsNullOrWhiteSpace(weaponText)
					? description
					: $"{description}\n\nWeapons: {weaponText}";
			}
		}

		if (button != null)
		{
			button.Disabled = !hasPerk;
			button.Text = hasPerk ? "Choose" : "Unavailable";
		}
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

		stats.ApplyPerk(_runtimePerks[index]);
		EmitSignal(nameof(PerkChosen));
	}

	private PlayerStats FindLocalPlayerStats()
	{
		var scene = GetTree()?.CurrentScene;
		if (scene == null)
			return null;

		return FindLocalPlayerStats(scene);
	}

	private string GetLocalWeaponName()
	{
		var stats = FindLocalPlayerStats();
		return stats?.SelectedWeapon;
	}

	private string GetWeaponRestrictionText(PerkDefinition perk)
	{
		if (perk == null)
			return "";

		if (perk.AllowedWeapons == PerkWeaponRestriction.All)
			return "";

		var weapons = new List<string>();
		if ((perk.AllowedWeapons & PerkWeaponRestriction.Assault) != 0)
			weapons.Add("Assault");
		if ((perk.AllowedWeapons & PerkWeaponRestriction.Sniper) != 0)
			weapons.Add("Sniper");
		if ((perk.AllowedWeapons & PerkWeaponRestriction.Fists) != 0)
			weapons.Add("Fists");
		if ((perk.AllowedWeapons & PerkWeaponRestriction.Sword) != 0)
			weapons.Add("Sword");
		if ((perk.AllowedWeapons & PerkWeaponRestriction.Staff) != 0)
			weapons.Add("Staff");

		return string.Join(", ", weapons);
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
