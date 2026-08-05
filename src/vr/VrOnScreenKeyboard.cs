using Godot;

namespace Underworld;

/// <summary>
/// Simple on-screen keyboard for VR character-name entry (laser-pointer friendly).
/// </summary>
public static class VrOnScreenKeyboard
{
	static Control _root;

	public static bool IsVisible => _root != null && GodotObject.IsInstanceValid(_root);

	public static void Show(CanvasLayer parent)
	{
		if (!VrController.IsActive || !uwsettings.instance.VrBootFull || parent == null)
		{
			return;
		}

		Hide();

		_root = new Panel
		{
			Name = "VrOnScreenKeyboard",
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_root.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		_root.OffsetTop = -230f;
		_root.OffsetBottom = 0f;
		_root.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.05f, 0.08f, 0.92f),
		});

		var layout = new VBoxContainer
		{
			Position = new Vector2(16f, 8f),
			Size = new Vector2(1248f, 214f),
		};
		_root.AddChild(layout);

		AddKeyRow(layout, "QWERTYUIOP");
		AddKeyRow(layout, "ASDFGHJKL");
		AddKeyRow(layout, "ZXCVBNM");
		AddActionRow(layout);
		parent.AddChild(_root);
	}

	public static void Hide()
	{
		if (_root != null && GodotObject.IsInstanceValid(_root))
		{
			_root.QueueFree();
		}

		_root = null;
	}

	static void AddKeyRow(Container parent, string keys)
	{
		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		parent.AddChild(row);
		foreach (var ch in keys)
		{
			row.AddChild(MakeKeyButton(ch.ToString()));
		}
	}

	static void AddActionRow(Container parent)
	{
		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		parent.AddChild(row);
		row.AddChild(MakeActionButton("Space", () => chargen.AppendNameChar(" "), 180f));
		row.AddChild(MakeActionButton("Back", chargen.BackspaceNameChar, 120f));
		row.AddChild(MakeActionButton("Done", chargen.SubmitNameInput, 140f));
	}

	static Button MakeKeyButton(string label)
	{
		var button = MakeActionButton(label, () => chargen.AppendNameChar(label.ToLower()), 56f);
		return button;
	}

	static Button MakeActionButton(string label, System.Action action, float width)
	{
		var button = new Button
		{
			Text = label,
			CustomMinimumSize = new Vector2(width, 44f),
			FocusMode = Control.FocusModeEnum.None,
		};
		button.Pressed += () => action();
		return button;
	}
}
