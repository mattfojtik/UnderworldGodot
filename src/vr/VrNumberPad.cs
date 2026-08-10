using Godot;

namespace Underworld;

/// <summary>
/// Head-locked number pad for VR quantity prompts (stack pickup / split).
/// </summary>
public static class VrNumberPad
{
	const int ViewportWidth = 800;
	const int ViewportHeight = 360;
	const float PanelWidthMeters = 0.62f;
	const float PanelHeightMeters = 0.28f;
	const float PanelDistanceMeters = 1.35f;
	const float PanelOffsetY = -0.12f;

	static SubViewport _viewport;
	static MeshInstance3D _panel;
	static Control _root;
	static int _maxQuantity;

	public static bool IsVisible =>
		_panel != null
		&& GodotObject.IsInstanceValid(_panel)
		&& _panel.Visible;

	public static void Show(Node3D underworld, XRCamera3D camera, int maxQuantity)
	{
		if (!VrController.IsActive || uwsettings.instance.vr_mirror || underworld == null || camera == null || maxQuantity < 1)
		{
			return;
		}

		Hide();
		_maxQuantity = maxQuantity;
		EnsurePanel(underworld, camera);
		BuildUi();
		_panel.Visible = true;
	}

	public static void Hide()
	{
		if (_panel != null && GodotObject.IsInstanceValid(_panel))
		{
			_panel.Visible = false;
		}

		if (_root != null && GodotObject.IsInstanceValid(_root))
		{
			_root.QueueFree();
		}

		_root = null;
		_maxQuantity = 0;
	}

	public static bool TryGetHit(
		Vector3 rayOrigin,
		Vector3 rayDir,
		float maxDistance,
		out Vector2 viewportPos,
		out Vector3 hitWorld)
	{
		viewportPos = default;
		hitWorld = rayOrigin + rayDir * maxDistance;
		if (!IsVisible || _panel?.Mesh is not QuadMesh quad)
		{
			return false;
		}

		var xf = _panel.GlobalTransform;
		var planeNormal = xf.Basis.Z;
		var denom = planeNormal.Dot(rayDir);
		if (Mathf.Abs(denom) < 1e-6f)
		{
			return false;
		}

		var t = (xf.Origin - rayOrigin).Dot(planeNormal) / denom;
		if (t < 0f || t > maxDistance)
		{
			return false;
		}

		hitWorld = rayOrigin + rayDir * t;
		var local = xf.AffineInverse() * hitWorld;
		var half = quad.Size * 0.5f;
		if (Mathf.Abs(local.X) > half.X || Mathf.Abs(local.Y) > half.Y)
		{
			return false;
		}

		var u = (local.X / quad.Size.X) + 0.5f;
		var v = (local.Y / quad.Size.Y) + 0.5f;
		viewportPos = new Vector2(
			Mathf.Clamp(u * ViewportWidth, 0f, ViewportWidth - 1f),
			Mathf.Clamp((1f - v) * ViewportHeight, 0f, ViewportHeight - 1f));
		return true;
	}

	public static void PushMouseMotion(Vector2 viewportPos)
	{
		if (_viewport == null)
		{
			return;
		}

		_viewport.WarpMouse(viewportPos);
		var motion = new InputEventMouseMotion
		{
			Position = viewportPos,
			GlobalPosition = viewportPos,
		};
		_viewport.PushInput(motion);
	}

	public static void PushMouseClick(Vector2 viewportPos, MouseButton button)
	{
		if (_viewport == null)
		{
			return;
		}

		_viewport.WarpMouse(viewportPos);
		_viewport.PushInput(new InputEventMouseButton
		{
			ButtonIndex = button,
			Pressed = true,
			Position = viewportPos,
			GlobalPosition = viewportPos,
		});
		_viewport.PushInput(new InputEventMouseButton
		{
			ButtonIndex = button,
			Pressed = false,
			Position = viewportPos,
			GlobalPosition = viewportPos,
		});
	}

	static void EnsurePanel(Node3D underworld, XRCamera3D camera)
	{
		if (_viewport != null && GodotObject.IsInstanceValid(_viewport)
			&& _panel != null && GodotObject.IsInstanceValid(_panel))
		{
			_panel.Position = new Vector3(0f, PanelOffsetY, -PanelDistanceMeters);
			return;
		}

		_viewport = new SubViewport
		{
			Name = "VrNumberPadViewport",
			Size = new Vector2I(ViewportWidth, ViewportHeight),
			TransparentBg = true,
			Disable3D = true,
			HandleInputLocally = true,
			GuiDisableInput = false,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			Msaa2D = Viewport.Msaa.Disabled,
		};
		underworld.AddChild(_viewport);
		_viewport.CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest;

		var material = new StandardMaterial3D
		{
			AlbedoTexture = _viewport.GetTexture(),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DisableReceiveShadows = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};

		_panel = new MeshInstance3D
		{
			Name = "VrNumberPadPanel",
			Mesh = new QuadMesh
			{
				Size = new Vector2(PanelWidthMeters, PanelHeightMeters),
				Material = material,
			},
			Position = new Vector3(0f, PanelOffsetY, -PanelDistanceMeters),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Layers = main.LayerGeo | main.LayerXFER,
			Visible = false,
		};
		camera.AddChild(_panel);
	}

	static void BuildUi()
	{
		if (_viewport == null)
		{
			return;
		}

		_root = new Panel
		{
			Name = "VrNumberPadRoot",
			MouseFilter = Control.MouseFilterEnum.Stop,
			Size = new Vector2(ViewportWidth, ViewportHeight),
		};
		_root.AddThemeStyleboxOverride("panel", new StyleBoxFlat
		{
			BgColor = new Color(0.05f, 0.05f, 0.08f, 0.94f),
		});
		_viewport.AddChild(_root);

		var layout = new VBoxContainer
		{
			Position = new Vector2(12f, 8f),
			Size = new Vector2(ViewportWidth - 24f, ViewportHeight - 16f),
		};
		_root.AddChild(layout);

		var title = new Label
		{
			Text = "How many?",
			HorizontalAlignment = HorizontalAlignment.Center,
		};
		title.AddThemeFontSizeOverride("font_size", 24);
		title.AddThemeColorOverride("font_color", new Color(0.95f, 0.96f, 1f));
		layout.AddChild(title);

		AddDigitRow(layout, "123");
		AddDigitRow(layout, "456");
		AddDigitRow(layout, "789");
		AddActionRow(layout);
	}

	static void AddDigitRow(Container parent, string keys)
	{
		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		parent.AddChild(row);
		foreach (var ch in keys)
		{
			row.AddChild(MakeDigitKey(ch));
		}
	}

	static void AddActionRow(Container parent)
	{
		var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
		parent.AddChild(row);
		row.AddChild(MakeActionButton("All", SelectAll, 150f));
		row.AddChild(MakeDigitKey('0'));
		row.AddChild(MakeActionButton("Back", BackspaceDigit, 130f));
		row.AddChild(MakeActionButton("Done", Submit, 150f));
	}

	static Button MakeDigitKey(char digit)
	{
		var button = new Button
		{
			Text = digit.ToString(),
			CustomMinimumSize = new Vector2(170f, 64f),
			FocusMode = Control.FocusModeEnum.None,
		};
		ApplyKeyStyles(button);
		button.Pressed += () =>
		{
			FlashKeyPressed(button);
			AppendDigit(digit);
		};
		return button;
	}

	static Button MakeActionButton(string label, System.Action action, float width)
	{
		var button = new Button
		{
			Text = label,
			CustomMinimumSize = new Vector2(width, 64f),
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

	static void AppendDigit(char digit)
	{
		var input = uimanager.instance?.TypedInput;
		if (input == null || !MessageDisplay.WaitingForTypedInput)
		{
			return;
		}

		var next = input.Text + digit;
		if (!int.TryParse(next, out var value) || value > _maxQuantity)
		{
			return;
		}

		input.Text = next;
		input.CaretColumn = input.Text.Length;
		uimanager.instance.scroll.UpdateMessageDisplay();
	}

	static void BackspaceDigit()
	{
		var input = uimanager.instance?.TypedInput;
		if (input == null || input.Text.Length == 0)
		{
			return;
		}

		input.Text = input.Text.Substring(0, input.Text.Length - 1);
		input.CaretColumn = input.Text.Length;
		uimanager.instance.scroll.UpdateMessageDisplay();
	}

	static void SelectAll()
	{
		var input = uimanager.instance?.TypedInput;
		if (input == null)
		{
			return;
		}

		input.Text = _maxQuantity.ToString();
		input.CaretColumn = input.Text.Length;
		uimanager.instance.scroll.UpdateMessageDisplay();
	}

	static void Submit()
	{
		if (!MessageDisplay.WaitingForTypedInput)
		{
			return;
		}

		MessageDisplay.CompleteTypedInput();
		Hide();
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
		button.AddThemeFontSizeOverride("font_size", 24);
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
