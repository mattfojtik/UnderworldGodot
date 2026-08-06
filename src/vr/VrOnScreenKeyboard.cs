using System.Collections.Generic;
using Godot;

namespace Underworld;

/// <summary>
/// Simple on-screen keyboard for VR character-name entry (laser-pointer friendly).
/// </summary>
public static class VrOnScreenKeyboard
{
	static Control _root;
	static bool _shiftActive;
	static Button _shiftButton;
	static readonly List<(Button Button, char Lower, char Upper)> _letterKeys = new();

	public static bool IsVisible => _root != null && GodotObject.IsInstanceValid(_root);

	public static void Show(CanvasLayer parent)
	{
		if (!VrController.IsActive || !uwsettings.instance.VrBootFull || parent == null)
		{
			return;
		}

		Hide();

		_shiftActive = false;
		_letterKeys.Clear();

		_root = new Panel
		{
			Name = "VrOnScreenKeyboard",
			MouseFilter = Control.MouseFilterEnum.Stop,
		};
		_root.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		_root.OffsetTop = -460f;
		_root.OffsetBottom = 0f;
		_root.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.05f, 0.08f, 0.92f),
		});

		var layout = new VBoxContainer
		{
			Position = new Vector2(16f, 8f),
			Size = new Vector2(1248f, 428f),
		};
		_root.AddChild(layout);

		AddKeyRow(layout, "qwertyuiop");
		AddKeyRow(layout, "asdfghjkl");
		AddKeyRow(layout, "zxcvbnm");
		AddActionRow(layout);
		parent.AddChild(_root);
		RefreshLetterLabels();
	}

	public static void Hide()
	{
		if (_root != null && GodotObject.IsInstanceValid(_root))
		{
			_root.QueueFree();
		}

		_root = null;
		_shiftActive = false;
		_shiftButton = null;
		_letterKeys.Clear();
	}

	static void AddKeyRow(Container parent, string keys)
	{
		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		parent.AddChild(row);
		foreach (var ch in keys)
		{
			row.AddChild(MakeLetterKey(ch));
		}
	}

	static void AddActionRow(Container parent)
	{
		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		parent.AddChild(row);
		row.AddChild(MakeShiftButton());
		row.AddChild(MakeActionButton("Space", () => chargen.AppendNameChar(" "), 280f));
		row.AddChild(MakeActionButton("Back", chargen.BackspaceNameChar, 200f));
		row.AddChild(MakeActionButton("Done", chargen.SubmitNameInput, 240f));
	}

	static Button MakeShiftButton()
	{
		_shiftButton = new Button
		{
			Text = "Shift",
			CustomMinimumSize = new Vector2(144f, 88f),
			FocusMode = Control.FocusModeEnum.None,
			ToggleMode = true,
		};
		ApplyKeyStyles(_shiftButton);
		_shiftButton.Toggled += OnShiftToggled;
		return _shiftButton;
	}

	static void OnShiftToggled(bool pressed)
	{
		_shiftActive = pressed;
		RefreshLetterLabels();
		RefreshShiftStyle();
	}

	static void RefreshLetterLabels()
	{
		foreach (var (button, lower, upper) in _letterKeys)
		{
			button.Text = _shiftActive ? upper.ToString() : lower.ToString();
		}
	}

	static void RefreshShiftStyle()
	{
		if (_shiftButton == null)
		{
			return;
		}

		var active = _shiftActive;
		_shiftButton.AddThemeStyleboxOverride("normal", MakeKeyStyle(
			active ? new Color(0.42f, 0.55f, 0.78f) : KeyNormalColor));
		_shiftButton.AddThemeStyleboxOverride("hover", MakeKeyStyle(
			active ? new Color(0.48f, 0.62f, 0.86f) : KeyHoverColor));
	}

	static Button MakeLetterKey(char lower)
	{
		var upper = char.ToUpper(lower);
		var button = MakeKeyButton(lower, upper, 112f);
		_letterKeys.Add((button, lower, upper));
		button.Text = lower.ToString();
		return button;
	}

	static Button MakeKeyButton(char lower, char upper, float width)
	{
		var button = new Button
		{
			CustomMinimumSize = new Vector2(width, 88f),
			FocusMode = Control.FocusModeEnum.None,
		};
		ApplyKeyStyles(button);
		button.Pressed += () =>
		{
			FlashKeyPressed(button);
			var ch = _shiftActive ? upper : lower;
			chargen.AppendNameChar(ch.ToString());
		};
		return button;
	}

	static Button MakeActionButton(string label, System.Action action, float width)
	{
		var button = new Button
		{
			Text = label,
			CustomMinimumSize = new Vector2(width, 88f),
			FocusMode = Control.FocusModeEnum.None,
		};
		ApplyKeyStyles(button);
		button.Pressed += () =>
		{
			FlashKeyPressed(button);
			action();
		};
		return button;
	}

	static readonly Color KeyNormalColor = new(0.22f, 0.24f, 0.32f);
	static readonly Color KeyHoverColor = new(0.30f, 0.34f, 0.44f);
	static readonly Color KeyPressedColor = new(0.58f, 0.74f, 0.98f);

	static void FlashKeyPressed(Button button)
	{
		var flashStyle = MakeKeyStyle(KeyPressedColor);
		button.AddThemeStyleboxOverride("normal", flashStyle);
		button.AddThemeStyleboxOverride("hover", flashStyle);
		button.AddThemeStyleboxOverride("pressed", flashStyle);
		var timer = button.GetTree().CreateTimer(0.12);
		timer.Timeout += () =>
		{
			if (GodotObject.IsInstanceValid(button))
			{
				ApplyKeyStyles(button);
				RefreshShiftStyle();
			}
		};
	}

	static void ApplyKeyStyles(Button button)
	{
		var normal = MakeKeyStyle(KeyNormalColor);
		var hover = MakeKeyStyle(KeyHoverColor);
		var pressed = MakeKeyStyle(KeyPressedColor);
		button.AddThemeStyleboxOverride("normal", normal);
		button.AddThemeStyleboxOverride("hover", hover);
		button.AddThemeStyleboxOverride("pressed", pressed);
		button.AddThemeStyleboxOverride("focus", normal);
		button.AddThemeColorOverride("font_color", new Color(0.95f, 0.96f, 1f));
		button.AddThemeColorOverride("font_pressed_color", new Color(0.08f, 0.1f, 0.16f));
		button.AddThemeFontSizeOverride("font_size", 28);
	}

	static StyleBoxFlat MakeKeyStyle(Color bg)
	{
		return new StyleBoxFlat
		{
			BgColor = bg,
			CornerRadiusTopLeft = 8,
			CornerRadiusTopRight = 8,
			CornerRadiusBottomLeft = 8,
			CornerRadiusBottomRight = 8,
			ContentMarginLeft = 8,
			ContentMarginRight = 8,
			ContentMarginTop = 8,
			ContentMarginBottom = 8,
		};
	}
}
