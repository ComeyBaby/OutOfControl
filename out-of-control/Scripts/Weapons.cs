using System;

public static class Weapons
{
	public const string Assault = "Assault";
	public const string Sniper = "Sniper";
	public const string Fists = "Fists";
	public const string Sword = "Sword";

	public static string Normalize(string weapon)
	{
		if (string.IsNullOrWhiteSpace(weapon))
			return Assault;

		return weapon.Trim() switch
		{
			Assault => Assault,
			Sniper => Sniper,
			Fists => Fists,
			Sword => Sword,
			_ => Assault
		};
	}

	public static bool IsMelee(string weapon)
	{
		var normalized = Normalize(weapon);
		return normalized == Fists || normalized == Sword;
	}

	public static bool Equals(string a, string b)
	{
		return string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);
	}
}
