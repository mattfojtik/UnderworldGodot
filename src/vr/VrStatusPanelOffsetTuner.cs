using System;
using Godot;

namespace Underworld;

/// <summary>
/// Dev-only status-panel offset tuner (branch <c>vr-status-panel-offset-tuner</c>).
/// Discrete button/stick steps â€” never continuous hand drag â€” so jitter cannot creep offsets.
/// </summary>
public static partial class VrController
{
	const float OffsetTuneCoarseStepMeters = 0.01f;
	const float OffsetTuneFineStepMeters = 0.001f;
	const float OffsetTuneStickThreshold = 0.7f;
	const float OffsetTuneStickDeadzone = 0.35f;
	const float OffsetTuneNudgeRepeatSeconds = 0.14f;
	const float OffsetTuneSelectMaxDistance = 4f;

	/// <summary>Index into widgets, or <see cref="_statusWidgets"/>.Count for global Y.</summary>
	static int _offsetTuneTargetIndex;
	static int _offsetTuneAxis; // 0=X, 1=Y, 2=Z
	static bool _offsetTuneDomTriggerWasPressed;
	static bool _offsetTuneDomGripWasPressed;
	static bool _offsetTuneOffTriggerWasPressed;
	static bool _offsetTuneOffGripWasPressed;
	static bool _offsetTuneLeftStickClickWasPressed;
	static bool _offsetTuneStickYWasOut;
	static float _offsetTuneNudgeReadyAtMs;
	static bool _offsetTuneDemoSeeded;
	static int _offsetTuneWeaponAnimId;
	static int _offsetTuneWeaponFrame;
	static bool _offsetTuneWeaponOnExecute;
	static ulong _offsetTuneWeaponNextMs;
	static Label3D _offsetTuneHudLabel;
	static Label3D _offsetTuneMessageScrollLabel;
	static MeshInstance3D _offsetTuneOutline;
	static StandardMaterial3D _offsetTuneOutlineMaterial;
	static readonly System.Collections.Generic.Dictionary<VrStatusWidgetKind, Label3D> _offsetTunePanelLabels = new();
	const float OffsetTuneOutlineThickness = 0.012f;
	const uint OffsetTuneRenderLayers = main.LayerGeo | main.LayerXFER;

	public static bool IsStatusPanelOffsetTuneActive()
	{
		if (!IsActive
			|| uwsettings.instance.vr_mirror
			|| !uwsettings.instance.vr_status_panels
			|| !(uimanager.InGame || uimanager.InConversation)
			|| IsHudOnMenuScreen())
		{
			return false;
		}

		// Branch default: keep tuner on. Existing settings.json often omits the key (bool â†’ false).
		if (!uwsettings.instance.vr_status_panel_offset_tune)
		{
			uwsettings.instance.vr_status_panel_offset_tune = true;
		}

		return true;
	}

	static void TickStatusPanelOffsetTuner()
	{
		if (!IsStatusPanelOffsetTuneActive())
		{
			HideOffsetTuneLabels();
			ResetOffsetTunePressState();
			_offsetTuneDemoSeeded = false;
			return;
		}

		_headOverlaysVisible = true;
		EnsureVrStatusPanels();
		InitStatusWidgetsIfNeeded();
		EnsureOffsetTuneDemoContent();
		TickOffsetTuneWeaponLoop();

		ClampOffsetTuneTargetIndex();

		ApplyOffsetTuneSelectFromLaser();
		ApplyOffsetTuneCyclePanel();
		ApplyOffsetTuneCycleAxis();
		ApplyOffsetTuneNudge();
		ApplyOffsetTuneSave();
		UpdateOffsetTuneLabels();
		UpdateOffsetTuneSelectionOutline();
	}

	static void EnsureOffsetTuneDemoContent()
	{
		var ui = uimanager.instance;
		if (ui == null)
		{
			return;
		}

		// Keep conversation chrome visible for offset placement (not a real conversation).
		var conversation = GetConversationPanel();
		if (conversation != null)
		{
			uimanager.EnableDisable(conversation, true);
			if (uimanager.ConversationText != null)
			{
				uimanager.ConversationText.Text = "[center]Offset tune â€” conversation panel[/center]";
			}
		}

		// Show inventory / paperdoll / chain family so those quads aren't empty.
		if (ui.PanelInventory != null)
		{
			uimanager.EnableDisable(ui.PanelInventory, true);
		}

		if (ui.PanelRuneBag != null)
		{
			uimanager.EnableDisable(ui.PanelRuneBag, true);
		}

		if (ui.PanelStats != null)
		{
			uimanager.EnableDisable(ui.PanelStats, true);
		}

		if (!_offsetTuneDemoSeeded)
		{
			// Three shelf runes (An, Bet, Corp are indices 0,1,2).
			playerdat.SetRune(0, true);
			playerdat.SetRune(1, true);
			playerdat.SetRune(2, true);
			playerdat.SetSelectedRune(0, 0);
			playerdat.SetSelectedRune(1, 1);
			playerdat.SetSelectedRune(2, 2);
			playerdat.NoOfSelectedRunes = 3;
			uimanager.EnableDisable(ui.SelectedRunes[0], true);
			uimanager.EnableDisable(ui.SelectedRunes[1], true);
			uimanager.EnableDisable(ui.SelectedRunes[2], true);
			uimanager.RedrawSelectedRuneSlots();

			// Three active-spell icons (major classes with valid UW1/UW2 art).
			uimanager.SetSpellIcon(0, 0, 0);
			uimanager.SetSpellIcon(1, 2, 0);
			uimanager.SetSpellIcon(2, 3, 0);

			_offsetTuneWeaponAnimId = playerdat.isLefty
				? uimanager.Sword_Stab_Left_Charge
				: uimanager.Sword_Stab_Right_Charge;
			_offsetTuneWeaponFrame = 0;
			_offsetTuneWeaponOnExecute = false;
			_offsetTuneWeaponNextMs = Time.GetTicksMsec();
			uimanager.CombatAnimationStage = uimanager.CombatAnimationStages.StrikingWeapon;
			uimanager.DrawWeaponAnimation(_offsetTuneWeaponAnimId, 0);
			NotifyVrConversationPanelUpdated();
			NotifyVrWeaponAnimUpdated();
			NotifyVrActiveSpellsUpdated();

			// Message scroll needs visible lines or the VR scroll panel stays hidden.
			uimanager.AddToMessageScroll("Offset tune â€” message scroll\n");
			uimanager.AddToMessageScroll("Line 2 for scroll height\n");
			uimanager.AddToMessageScroll("Line 3 â€” aim here to select\n");
			NotifyMessageScrollUpdated();

			_offsetTuneDemoSeeded = true;
			VrDiagLog.Print("[VR] Offset tune demo content seeded (runes/spells/conversation/stab/scroll).");
		}

		// Keep scroll text present if something cleared it mid-session.
		if (!HasMessageScrollContent() && uimanager.MessageScroll != null)
		{
			uimanager.AddToMessageScroll("Offset tune â€” message scroll\n");
			NotifyMessageScrollUpdated();
		}
	}

	static void TickOffsetTuneWeaponLoop()
	{
		var now = Time.GetTicksMsec();
		if (now < _offsetTuneWeaponNextMs)
		{
			return;
		}

		_offsetTuneWeaponNextMs = now + 120;
		_offsetTuneWeaponFrame++;

		var chargeAnim = playerdat.isLefty
			? uimanager.Sword_Stab_Left_Charge
			: uimanager.Sword_Stab_Right_Charge;
		var executeAnim = playerdat.isLefty
			? uimanager.Sword_Stab_Left_Execute
			: uimanager.Sword_Stab_Right_Execute;

		if (!_offsetTuneWeaponOnExecute)
		{
			if (_offsetTuneWeaponFrame > 3
				|| uimanager.weaponframes[chargeAnim, Mathf.Clamp(_offsetTuneWeaponFrame, 0, 5)] < 0)
			{
				_offsetTuneWeaponOnExecute = true;
				_offsetTuneWeaponFrame = 0;
				_offsetTuneWeaponAnimId = executeAnim;
			}
			else
			{
				_offsetTuneWeaponAnimId = chargeAnim;
			}
		}
		else if (_offsetTuneWeaponFrame > 5
			|| uimanager.weaponframes[executeAnim, Mathf.Clamp(_offsetTuneWeaponFrame, 0, 5)] < 0)
		{
			_offsetTuneWeaponOnExecute = false;
			_offsetTuneWeaponFrame = 0;
			_offsetTuneWeaponAnimId = chargeAnim;
		}

		uimanager.CombatAnimationStage = uimanager.CombatAnimationStages.StrikingWeapon;
		uimanager.DrawWeaponAnimation(_offsetTuneWeaponAnimId, Mathf.Clamp(_offsetTuneWeaponFrame, 0, 5));
	}

	/// <summary>Keep the aim laser glued to the hovered status panel while tuning.</summary>
	static void UpdateOffsetTuneLaserFeedback()
	{
		if (!IsStatusPanelOffsetTuneActive())
		{
			return;
		}

		var rayOrigin = GetAimRayOrigin();
		var rayDir = GetControllerRayDir();
		var bestDist = float.MaxValue;
		var hit = Vector3.Zero;
		var found = false;

		if (TryGetClosestAnyStatusWidgetHit(rayOrigin, rayDir, OffsetTuneSelectMaxDistance, out _, out _, out var widgetHit))
		{
			bestDist = rayOrigin.DistanceTo(widgetHit);
			hit = widgetHit;
			found = true;
		}

		if (TryGetMessageScrollPanelHit(rayOrigin, rayDir, OffsetTuneSelectMaxDistance, out _, out var scrollHit))
		{
			var scrollDist = rayOrigin.DistanceTo(scrollHit);
			if (!found || scrollDist < bestDist)
			{
				hit = scrollHit;
				found = true;
			}
		}

		if (found)
		{
			UpdatePointerLaser(rayOrigin, hit, visible: true);
		}
	}

	static void ResetOffsetTunePressState()
	{
		_offsetTuneDomTriggerWasPressed = false;
		_offsetTuneDomGripWasPressed = false;
		_offsetTuneOffTriggerWasPressed = false;
		_offsetTuneOffGripWasPressed = false;
		_offsetTuneLeftStickClickWasPressed = false;
		_offsetTuneStickYWasOut = false;
		_offsetTuneNudgeReadyAtMs = 0f;
	}

	static int OffsetTuneMessageScrollIndex() => _statusWidgets.Count;

	static int OffsetTuneGlobalYIndex() => _statusWidgets.Count + 1;

	static int OffsetTuneTargetCount() => _statusWidgets.Count + 2; // MessageScroll + GlobalY

	static void ClampOffsetTuneTargetIndex()
	{
		var count = OffsetTuneTargetCount();
		if (count <= 0)
		{
			_offsetTuneTargetIndex = 0;
			return;
		}

		if (_offsetTuneTargetIndex < 0)
		{
			_offsetTuneTargetIndex = 0;
		}
		else if (_offsetTuneTargetIndex >= count)
		{
			_offsetTuneTargetIndex = count - 1;
		}

		if (IsOffsetTuneGlobalYSelected())
		{
			_offsetTuneAxis = 1;
		}
		else if (IsOffsetTuneMessageScrollSelected())
		{
			_offsetTuneAxis = Mathf.Clamp(_offsetTuneAxis, 0, 2);
		}
		else
		{
			_offsetTuneAxis = Mathf.Clamp(_offsetTuneAxis, 0, 2);
		}
	}

	static bool IsOffsetTuneMessageScrollSelected() =>
		_offsetTuneTargetIndex == OffsetTuneMessageScrollIndex();

	static bool IsOffsetTuneGlobalYSelected() =>
		_offsetTuneTargetIndex == OffsetTuneGlobalYIndex();

	static string GetOffsetTuneTargetName()
	{
		if (IsOffsetTuneGlobalYSelected())
		{
			return "GlobalY";
		}

		if (IsOffsetTuneMessageScrollSelected())
		{
			return "MessageScroll";
		}

		if (_offsetTuneTargetIndex < 0 || _offsetTuneTargetIndex >= _statusWidgets.Count)
		{
			return "?";
		}

		return _statusWidgets[_offsetTuneTargetIndex].Kind.ToString();
	}

	static char GetOffsetTuneAxisChar() => _offsetTuneAxis switch
	{
		0 => 'X',
		2 => 'Z',
		_ => 'Y',
	};

	static Vector3 GetOffsetTuneSelectedOffset()
	{
		if (IsOffsetTuneGlobalYSelected())
		{
			return new Vector3(0f, uwsettings.instance.vr_status_panels_offset_y, 0f);
		}

		if (IsOffsetTuneMessageScrollSelected())
		{
			var o = GetMessageScrollOffsetMeters();
			return o;
		}

		return GetWidgetOffsetMeters(_statusWidgets[_offsetTuneTargetIndex].Kind);
	}

	static void SetOffsetTuneSelectedOffset(Vector3 offset)
	{
		if (IsOffsetTuneGlobalYSelected())
		{
			uwsettings.instance.vr_status_panels_offset_y = offset.Y;
			return;
		}

		if (IsOffsetTuneMessageScrollSelected())
		{
			uwsettings.instance.vr_message_scroll_offset_x = offset.X;
			uwsettings.instance.vr_message_scroll_offset_y = offset.Y;
			uwsettings.instance.vr_message_scroll_offset_z = offset.Z;
			return;
		}

		SetWidgetOffsetMeters(_statusWidgets[_offsetTuneTargetIndex].Kind, offset);
	}

	static void SetWidgetOffsetMeters(VrStatusWidgetKind kind, Vector3 offset)
	{
		var settings = uwsettings.instance;
		switch (kind)
		{
			case VrStatusWidgetKind.HealthFlask:
				settings.vr_health_flask_offset_x = offset.X;
				settings.vr_health_flask_offset_y = offset.Y;
				settings.vr_health_flask_offset_z = offset.Z;
				break;
			case VrStatusWidgetKind.ManaFlask:
				settings.vr_mana_flask_offset_x = offset.X;
				settings.vr_mana_flask_offset_y = offset.Y;
				settings.vr_mana_flask_offset_z = offset.Z;
				break;
			case VrStatusWidgetKind.Compass:
				settings.vr_compass_offset_x = offset.X;
				settings.vr_compass_offset_y = offset.Y;
				settings.vr_compass_offset_z = offset.Z;
				break;
			case VrStatusWidgetKind.Inventory:
				settings.vr_inventory_offset_x = offset.X;
				settings.vr_inventory_offset_y = offset.Y;
				settings.vr_inventory_offset_z = offset.Z;
				break;
			case VrStatusWidgetKind.RuneBag:
				settings.vr_rune_bag_offset_x = offset.X;
				settings.vr_rune_bag_offset_y = offset.Y;
				settings.vr_rune_bag_offset_z = offset.Z;
				break;
			case VrStatusWidgetKind.Stats:
				settings.vr_stats_offset_x = offset.X;
				settings.vr_stats_offset_y = offset.Y;
				settings.vr_stats_offset_z = offset.Z;
				break;
			case VrStatusWidgetKind.SelectedRunes:
				settings.vr_rune_shelf_offset_x = offset.X;
				settings.vr_rune_shelf_offset_y = offset.Y;
				settings.vr_rune_shelf_offset_z = offset.Z;
				break;
			case VrStatusWidgetKind.ActiveSpells:
				settings.vr_active_spells_offset_x = offset.X;
				settings.vr_active_spells_offset_y = offset.Y;
				settings.vr_active_spells_offset_z = offset.Z;
				break;
			case VrStatusWidgetKind.Chain:
				settings.vr_chain_offset_x = offset.X;
				settings.vr_chain_offset_y = offset.Y;
				settings.vr_chain_offset_z = offset.Z;
				break;
			case VrStatusWidgetKind.Conversation:
				settings.vr_conversation_offset_x = offset.X;
				settings.vr_conversation_offset_y = offset.Y;
				settings.vr_conversation_offset_z = offset.Z;
				break;
			case VrStatusWidgetKind.WeaponAnim:
				settings.vr_weapon_anim_offset_x = offset.X;
				settings.vr_weapon_anim_offset_y = offset.Y;
				settings.vr_weapon_anim_offset_z = offset.Z;
				break;
			case VrStatusWidgetKind.PowerGem:
				settings.vr_power_gem_offset_x = offset.X;
				settings.vr_power_gem_offset_y = offset.Y;
				settings.vr_power_gem_offset_z = offset.Z;
				break;
			case VrStatusWidgetKind.Eyes:
				settings.vr_eyes_offset_x = offset.X;
				settings.vr_eyes_offset_y = offset.Y;
				settings.vr_eyes_offset_z = offset.Z;
				break;
		}
	}

	static void ApplyOffsetTuneSelectFromLaser()
	{
		var dominant = GetDominantController();
		if (dominant == null)
		{
			return;
		}

		var pressed = IsButtonPressed(dominant, HudLeftClickActions);
		var edge = pressed && !_offsetTuneDomTriggerWasPressed;
		_offsetTuneDomTriggerWasPressed = pressed;
		if (!edge)
		{
			return;
		}

		var rayOrigin = GetAimRayOrigin();
		var rayDir = GetControllerRayDir();

		var bestDist = float.MaxValue;
		var selectedIndex = -1;

		if (TryGetClosestAnyStatusWidgetHit(rayOrigin, rayDir, OffsetTuneSelectMaxDistance, out var widget, out _, out var widgetHit))
		{
			bestDist = rayOrigin.DistanceTo(widgetHit);
			for (var i = 0; i < _statusWidgets.Count; i++)
			{
				if (_statusWidgets[i] == widget)
				{
					selectedIndex = i;
					break;
				}
			}
		}

		if (TryGetMessageScrollPanelHit(rayOrigin, rayDir, OffsetTuneSelectMaxDistance, out _, out var scrollHit))
		{
			var scrollDist = rayOrigin.DistanceTo(scrollHit);
			if (scrollDist < bestDist)
			{
				selectedIndex = OffsetTuneMessageScrollIndex();
			}
		}

		if (selectedIndex >= 0)
		{
			_offsetTuneTargetIndex = selectedIndex;
		}
	}

	static void ApplyOffsetTuneCyclePanel()
	{
		var offHand = GetOffHandController();
		if (offHand == null)
		{
			return;
		}

		var nextPressed = IsButtonPressed(offHand, HudLeftClickActions);
		if (nextPressed && !_offsetTuneOffTriggerWasPressed)
		{
			_offsetTuneTargetIndex++;
			if (_offsetTuneTargetIndex >= OffsetTuneTargetCount())
			{
				_offsetTuneTargetIndex = 0;
			}

			ClampOffsetTuneTargetIndex();
		}

		_offsetTuneOffTriggerWasPressed = nextPressed;

		var prevPressed = IsButtonPressed(offHand, HudRightClickActions);
		if (prevPressed && !_offsetTuneOffGripWasPressed)
		{
			_offsetTuneTargetIndex--;
			if (_offsetTuneTargetIndex < 0)
			{
				_offsetTuneTargetIndex = OffsetTuneTargetCount() - 1;
			}

			ClampOffsetTuneTargetIndex();
		}

		_offsetTuneOffGripWasPressed = prevPressed;
	}

	static void ApplyOffsetTuneCycleAxis()
	{
		if (IsOffsetTuneGlobalYSelected())
		{
			_offsetTuneAxis = 1;
			_offsetTuneDomGripWasPressed = IsButtonPressed(GetDominantController(), HudRightClickActions);
			return;
		}

		var dominant = GetDominantController();
		if (dominant == null)
		{
			return;
		}

		var pressed = IsButtonPressed(dominant, HudRightClickActions);
		if (pressed && !_offsetTuneDomGripWasPressed)
		{
			_offsetTuneAxis = (_offsetTuneAxis + 1) % 3;
		}

		_offsetTuneDomGripWasPressed = pressed;
	}

	static void ApplyOffsetTuneNudge()
	{
		if (_rightController == null)
		{
			return;
		}

		var stick = ReadStick(_rightController);
		// Prefer vertical deflection so snap-turn (X) is not fighting the nudge.
		var vertical = Mathf.Abs(stick.Y) >= Mathf.Abs(stick.X) && Mathf.Abs(stick.Y) >= OffsetTuneStickThreshold;
		if (!vertical)
		{
			_offsetTuneStickYWasOut = Mathf.Abs(stick.Y) >= OffsetTuneStickDeadzone;
			return;
		}

		var direction = stick.Y > 0f ? 1f : -1f;
		var nowMs = Time.GetTicksMsec();
		var canFire = !_offsetTuneStickYWasOut || nowMs >= _offsetTuneNudgeReadyAtMs;
		_offsetTuneStickYWasOut = true;
		if (!canFire)
		{
			return;
		}

		var fine = IsButtonPressed(_leftController, HudRightClickActions);
		var step = fine ? OffsetTuneFineStepMeters : OffsetTuneCoarseStepMeters;
		NudgeOffsetTuneSelectedAxis(direction * step);
		_offsetTuneNudgeReadyAtMs = nowMs + OffsetTuneNudgeRepeatSeconds * 1000f;
	}

	static void NudgeOffsetTuneSelectedAxis(float deltaMeters)
	{
		var offset = GetOffsetTuneSelectedOffset();
		if (IsOffsetTuneGlobalYSelected())
		{
			offset.Y += deltaMeters;
		}
		else
		{
			offset = _offsetTuneAxis switch
			{
				0 => new Vector3(offset.X + deltaMeters, offset.Y, offset.Z),
				2 => new Vector3(offset.X, offset.Y, offset.Z + deltaMeters),
				_ => new Vector3(offset.X, offset.Y + deltaMeters, offset.Z),
			};
		}

		// Quantize to millimetres so float noise doesn't accumulate.
		offset = new Vector3(
			Mathf.Round(offset.X * 1000f) / 1000f,
			Mathf.Round(offset.Y * 1000f) / 1000f,
			Mathf.Round(offset.Z * 1000f) / 1000f);
		SetOffsetTuneSelectedOffset(offset);
	}

	static void ApplyOffsetTuneSave()
	{
		if (_leftController == null)
		{
			return;
		}

		var pressed = IsButtonPressed(_leftController, RecenterStickClickActions);
		if (pressed && !_offsetTuneLeftStickClickWasPressed)
		{
			uwsettings.instance.Save();
			uimanager.AddToMessageScroll("VR panel offsets saved.\n");
			VrDiagLog.Print($"[VR] Offset tune saved: {GetOffsetTuneTargetName()} {FormatOffsetMeters(GetOffsetTuneSelectedOffset())}");
		}

		_offsetTuneLeftStickClickWasPressed = pressed;
	}

	static string FormatOffsetMeters(Vector3 offset) =>
		$"X={offset.X:F3} Y={offset.Y:F3} Z={offset.Z:F3}";

	static bool TryGetClosestAnyStatusWidgetHit(
		Vector3 rayOrigin,
		Vector3 rayDir,
		float maxDistance,
		out VrStatusWidget widget,
		out Vector2 hudViewportPos,
		out Vector3 hitWorld)
	{
		widget = null;
		hudViewportPos = default;
		hitWorld = default;
		var bestDistance = float.MaxValue;

		foreach (var candidate in _statusWidgets)
		{
			if (candidate?.Panel == null || !candidate.Panel.Visible)
			{
				continue;
			}

			if (!TryGetStatusWidgetHit(candidate, rayOrigin, rayDir, maxDistance, out var hudPos, out var candidateHit))
			{
				continue;
			}

			var distance = rayOrigin.DistanceTo(candidateHit);
			if (distance >= bestDistance)
			{
				continue;
			}

			bestDistance = distance;
			widget = candidate;
			hudViewportPos = hudPos;
			hitWorld = candidateHit;
		}

		return widget != null;
	}

	static Label3D CreateOffsetTuneLabel3D(string name, Color modulate)
	{
		var label = new Label3D
		{
			Name = name,
			FontSize = 48,
			PixelSize = 0.0014f,
			Modulate = modulate,
			OutlineSize = 6,
			OutlineModulate = Colors.Black,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Bottom,
			Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
			NoDepthTest = true,
			DoubleSided = true,
			// XRCamera culls to LayerGeo|LayerXFER — default layer 1 is invisible in VR.
			Layers = OffsetTuneRenderLayers,
		};
		if (uimanager.instance?.Font4X5P != null)
		{
			label.Font = uimanager.instance.Font4X5P;
		}

		return label;
	}

	static void EnsureOffsetTuneHudLabel()
	{
		if (_xrCamera == null)
		{
			return;
		}

		if (_offsetTuneHudLabel != null && GodotObject.IsInstanceValid(_offsetTuneHudLabel))
		{
			_offsetTuneHudLabel.Layers = OffsetTuneRenderLayers;
			return;
		}

		_offsetTuneHudLabel = CreateOffsetTuneLabel3D(
			"VrStatusPanelOffsetTuneHud",
			new Color(1f, 0.95f, 0.4f));
		_offsetTuneHudLabel.FontSize = 56;
		_offsetTuneHudLabel.PixelSize = 0.0016f;
		_offsetTuneHudLabel.HorizontalAlignment = HorizontalAlignment.Left;
		_offsetTuneHudLabel.VerticalAlignment = VerticalAlignment.Top;
		_xrCamera.AddChild(_offsetTuneHudLabel);
		_offsetTuneHudLabel.Position = new Vector3(-0.35f, 0.22f, -0.9f);
	}

	static Label3D EnsureOffsetTunePanelLabel(VrStatusWidget widget)
	{
		if (widget?.Panel == null || _xrCamera == null)
		{
			return null;
		}

		if (_offsetTunePanelLabels.TryGetValue(widget.Kind, out var existing)
			&& existing != null
			&& GodotObject.IsInstanceValid(existing))
		{
			existing.Layers = OffsetTuneRenderLayers;
			return existing;
		}

		var label = CreateOffsetTuneLabel3D($"VrOffsetTune_{widget.Kind}", new Color(0.85f, 1f, 0.95f));
		_xrCamera.AddChild(label);
		_offsetTunePanelLabels[widget.Kind] = label;
		return label;
	}

	static Label3D EnsureOffsetTuneMessageScrollLabel()
	{
		if (_xrCamera == null)
		{
			return null;
		}

		if (_offsetTuneMessageScrollLabel != null && GodotObject.IsInstanceValid(_offsetTuneMessageScrollLabel))
		{
			_offsetTuneMessageScrollLabel.Layers = OffsetTuneRenderLayers;
			return _offsetTuneMessageScrollLabel;
		}

		_offsetTuneMessageScrollLabel = CreateOffsetTuneLabel3D(
			"VrOffsetTune_MessageScroll",
			new Color(0.85f, 1f, 0.95f));
		_xrCamera.AddChild(_offsetTuneMessageScrollLabel);
		return _offsetTuneMessageScrollLabel;
	}

	static void HideOffsetTuneLabels()
	{
		if (_offsetTuneHudLabel != null && GodotObject.IsInstanceValid(_offsetTuneHudLabel))
		{
			_offsetTuneHudLabel.Visible = false;
		}

		if (_offsetTuneMessageScrollLabel != null && GodotObject.IsInstanceValid(_offsetTuneMessageScrollLabel))
		{
			_offsetTuneMessageScrollLabel.Visible = false;
		}

		if (_offsetTuneOutline != null && GodotObject.IsInstanceValid(_offsetTuneOutline))
		{
			_offsetTuneOutline.Visible = false;
		}

		foreach (var pair in _offsetTunePanelLabels)
		{
			if (pair.Value != null && GodotObject.IsInstanceValid(pair.Value))
			{
				pair.Value.Visible = false;
			}
		}
	}

	static ArrayMesh BuildOffsetTuneOutlineMesh(Vector2 panelSize)
	{
		var t = OffsetTuneOutlineThickness;
		var hw = panelSize.X * 0.5f;
		var hh = panelSize.Y * 0.5f;
		var ow = hw + t;
		var oh = hh + t;
		var z = 0.004f;

		var verts = new System.Collections.Generic.List<Vector3>(24);
		void AddQuad(float x0, float y0, float x1, float y1)
		{
			verts.Add(new Vector3(x0, y0, z));
			verts.Add(new Vector3(x1, y0, z));
			verts.Add(new Vector3(x1, y1, z));
			verts.Add(new Vector3(x0, y0, z));
			verts.Add(new Vector3(x1, y1, z));
			verts.Add(new Vector3(x0, y1, z));
		}

		AddQuad(-ow, hh, ow, oh);
		AddQuad(-ow, -oh, ow, -hh);
		AddQuad(-ow, -hh, -hw, hh);
		AddQuad(hw, -hh, ow, hh);

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		return mesh;
	}

	static void EnsureOffsetTuneOutline()
	{
		if (_xrCamera == null)
		{
			return;
		}

		if (_offsetTuneOutline != null && GodotObject.IsInstanceValid(_offsetTuneOutline))
		{
			_offsetTuneOutline.Layers = OffsetTuneRenderLayers;
			return;
		}

		_offsetTuneOutlineMaterial = new StandardMaterial3D
		{
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			AlbedoColor = new Color(0.15f, 1f, 0.25f, 0.95f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DisableReceiveShadows = true,
			NoDepthTest = true,
		};

		_offsetTuneOutline = new MeshInstance3D
		{
			Name = "VrStatusPanelOffsetTuneOutline",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Layers = OffsetTuneRenderLayers,
			Visible = false,
		};
		_xrCamera.AddChild(_offsetTuneOutline);
	}

	static void UpdateOffsetTuneSelectionOutline()
	{
		EnsureOffsetTuneOutline();
		if (_offsetTuneOutline == null)
		{
			return;
		}

		MeshInstance3D panel = null;
		if (IsOffsetTuneMessageScrollSelected()
			&& _messageScrollPanel != null
			&& GodotObject.IsInstanceValid(_messageScrollPanel)
			&& _messageScrollPanel.Visible)
		{
			panel = _messageScrollPanel;
		}
		else if (!IsOffsetTuneGlobalYSelected()
			&& _offsetTuneTargetIndex >= 0
			&& _offsetTuneTargetIndex < _statusWidgets.Count)
		{
			var widget = _statusWidgets[_offsetTuneTargetIndex];
			if (widget.Panel != null && widget.Panel.Visible)
			{
				panel = widget.Panel;
			}
		}

		if (panel == null || panel.Mesh is not QuadMesh quad)
		{
			_offsetTuneOutline.Visible = false;
			return;
		}

		_offsetTuneOutline.Mesh = BuildOffsetTuneOutlineMesh(quad.Size);
		if (_offsetTuneOutline.Mesh != null)
		{
			_offsetTuneOutline.Mesh.SurfaceSetMaterial(0, _offsetTuneOutlineMaterial);
		}

		_offsetTuneOutline.Position = panel.Position;
		_offsetTuneOutline.Rotation = panel.Rotation;
		_offsetTuneOutline.Visible = true;
	}

	static void UpdateOffsetTuneLabels()
	{
		EnsureOffsetTuneHudLabel();
		if (_offsetTuneHudLabel == null)
		{
			return;
		}

		var selected = GetOffsetTuneSelectedOffset();
		var fine = IsButtonPressed(_leftController, HudRightClickActions);
		var stepMm = fine ? 1 : 10;
		var axisNote = IsOffsetTuneGlobalYSelected()
			? "axis=Y (global)"
			: $"axis={GetOffsetTuneAxisChar()}";
		_offsetTuneHudLabel.Text =
			"PANEL OFFSET TUNE\n"
			+ $"{GetOffsetTuneTargetName()}  {axisNote}\n"
			+ $"{FormatOffsetMeters(selected)}\n"
			+ $"step={stepMm}mm (hold L-grip=fine)\n"
			+ "aim+trig=select  R-stick Y=nudge\n"
			+ "off trig/grip=next/prev  dom grip=axis\n"
			+ "L-stick click=SAVE";
		_offsetTuneHudLabel.Visible = true;
		_offsetTuneHudLabel.Layers = OffsetTuneRenderLayers;

		for (var i = 0; i < _statusWidgets.Count; i++)
		{
			var widget = _statusWidgets[i];
			var label = EnsureOffsetTunePanelLabel(widget);
			if (label == null || widget.Panel == null)
			{
				continue;
			}

			var offset = GetWidgetOffsetMeters(widget.Kind);
			var selectedPanel = i == _offsetTuneTargetIndex;
			label.Text = $"{widget.Kind}\nX={offset.X:F3}\nY={offset.Y:F3}\nZ={offset.Z:F3}";
			label.Modulate = selectedPanel
				? new Color(0.4f, 1f, 0.45f)
				: new Color(0.85f, 1f, 0.95f);

			var panelPos = widget.Panel.Position;
			var halfH = widget.Panel.Mesh is QuadMesh quad ? quad.Size.Y * 0.5f : 0.05f;
			label.Position = panelPos + new Vector3(0f, halfH + 0.05f, 0.02f);
			label.Visible = widget.Panel.Visible;
			label.Layers = OffsetTuneRenderLayers;
		}

		var scrollLabel = EnsureOffsetTuneMessageScrollLabel();
		if (scrollLabel != null && _messageScrollPanel != null && GodotObject.IsInstanceValid(_messageScrollPanel))
		{
			var scrollOff = GetMessageScrollOffsetMeters();
			var scrollSelected = IsOffsetTuneMessageScrollSelected();
			scrollLabel.Text = $"MessageScroll\nX={scrollOff.X:F3}\nY={scrollOff.Y:F3}\nZ={scrollOff.Z:F3}";
			scrollLabel.Modulate = scrollSelected
				? new Color(0.4f, 1f, 0.45f)
				: new Color(0.85f, 1f, 0.95f);
			var halfH = _messageScrollPanel.Mesh is QuadMesh scrollQuad ? scrollQuad.Size.Y * 0.5f : 0.05f;
			scrollLabel.Position = _messageScrollPanel.Position + new Vector3(0f, halfH + 0.05f, 0.02f);
			scrollLabel.Visible = _messageScrollPanel.Visible;
			scrollLabel.Layers = OffsetTuneRenderLayers;
		}
		else if (scrollLabel != null)
		{
			scrollLabel.Visible = false;
		}
	}
}
