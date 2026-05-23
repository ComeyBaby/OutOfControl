using Godot;

public static class SettingsOverlayNavigation
{
	public static bool TryNavigate(Node source, string scenePath)
	{
		if (string.IsNullOrWhiteSpace(scenePath))
			return false;

		var host = FindOverlayHost(source);
		if (host == null)
			return false;

		host.NavigateSettings(scenePath);
		return true;
	}

	public static bool TryClose(Node source)
	{
		var host = FindOverlayHost(source);
		if (host == null)
			return false;

		host.CloseSettingsOverlay();
		return true;
	}

	private static ISettingsOverlayHost FindOverlayHost(Node source)
	{
		for (Node current = source; current != null; current = current.GetParent())
		{
			if (current is ISettingsOverlayHost host)
				return host;
		}

		return null;
	}
}
