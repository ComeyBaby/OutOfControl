using Godot;

public static class UiNavigationHelper
{
	public static void FocusControl(Control control)
	{
		if (control == null || !GodotObject.IsInstanceValid(control))
			return;

		if (!control.IsInsideTree())
			return;

		control.GrabFocus();
	}
}
