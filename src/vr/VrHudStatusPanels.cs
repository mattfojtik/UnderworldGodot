using System;
using System.Collections.Generic;
using Godot;

namespace Underworld;

public static partial class VrController
{
	enum VrStatusWidgetKind
	{
		HealthFlask,
		ManaFlask,
		Compass,
		Inventory,
		WeaponAnim,
		PowerGem,
		Eyes,
	}

	sealed class VrStatusWidget
	{
		public VrStatusWidgetKind Kind;
		public Rect2 HudRect;
		public Rect2 EyesStripRect;
		public CanvasItem Source;
		public SubViewport Viewport;
		public Control Duplicate;
		public TextureRect GargoyleBackground;
		public MeshInstance3D Panel;
		public StandardMaterial3D Material;
		public TextureRect OverlayCursor;
		public float Alpha;
		public float HideAfterTime = -1f;
		public bool HoldWasActive;
		public bool FadeWhenInactive = true;
		public bool ShowWhileInGame;
	}

	static readonly List<VrStatusWidget> _statusWidgets = new();

	static bool IsPowerGemActive()
	{
		if (combat.PlayerAttackCharge > 0)
		{
			return true;
		}

		switch (combat.stage)
		{
			case combat.CombatStages.Charging:
			case combat.CombatStages.ReleaseSwing:
			case combat.CombatStages.SwingingAtTarget:
			case combat.CombatStages.StrikingTarget:
			case combat.CombatStages.Resetting:
				return true;
		}

		return playerdat.play_drawn == 1
			&& uimanager.InteractionMode == uimanager.InteractionModes.ModeAttack;
	}

	static bool AreEyesActive()
	{
		return uimanager.EyeLevel > 0 || uimanager.CurrentEyeLevel > 0;
	}

	static Rect2 GetHealthFlaskHudRectFixed()
	{
		var rect = new Rect2(992f, 500f, 96f, 132f);
		if (UWClass._RES == UWClass.GAME_UW2)
		{
			rect.Position += new Vector2(0f, 24f);
		}

		return rect;
	}

	static Rect2 GetManaFlaskHudRectFixed()
	{
		var ui = uimanager.instance;
		if (ui?.ManaFlaskPanel != null && ui.ManaFlaskBG != null)
		{
			return new Rect2(ui.ManaFlaskPanel.Position + ui.ManaFlaskBG.Position, ui.ManaFlaskBG.Size);
		}

		var rect = new Rect2(1136f, 500f, 96f, 132f);
		if (UWClass._RES == UWClass.GAME_UW2)
		{
			rect.Position += new Vector2(16f, 24f);
		}

		return rect;
	}

	static Rect2 GetCompassHudRectFixed()
	{
		return UWClass._RES == UWClass.GAME_UW2
			? new Rect2(376f, 592f, 208f, 64f)
			: new Rect2(448f, 524f, 208f, 104f);
	}

	static Rect2 GetInventoryHudRectFixed()
	{
		var ui = uimanager.instance;
		if (ui?.PanelInventory != null && ui.PanelInventoryArt != null)
		{
			return new Rect2(
				ui.PanelInventory.Position + ui.PanelInventoryArt.Position,
				ui.PanelInventoryArt.Size);
		}

		return UWClass._RES == UWClass.GAME_UW2
			? new Rect2(944f, 28f, 332f, 448f)
			: new Rect2(944f, 28f, 332f, 456f);
	}

	static TextureRect GetWeaponAnimRect()
	{
		var ui = uimanager.instance;
		if (ui == null)
		{
			return null;
		}

		return UWClass._RES == UWClass.GAME_UW2 ? ui.weaponanimuw2 : ui.weaponanimuw1;
	}

	static Rect2 GetWeaponAnimHudRectFixed()
	{
		var anim = GetWeaponAnimRect();
		if (anim != null)
		{
			return new Rect2(anim.Position, anim.Size);
		}

		return UWClass._RES == UWClass.GAME_UW2
			? new Rect2(60f, 64f, 840f, 510f)
			: new Rect2(204f, 72f, 661.6f, 390.4f);
	}

	static bool IsWeaponAnimActive()
	{
		var anim = GetWeaponAnimRect();
		return anim != null && anim.Texture != null;
	}

	static bool IsWeaponAnimStageActive()
	{
		return uimanager.CombatAnimationStage != uimanager.CombatAnimationStages.PutAway;
	}

	static Panel GetCompassPanel()
	{
		var ui = uimanager.instance;
		if (ui == null)
		{
			return null;
		}

		if (UWClass._RES == UWClass.GAME_UW2)
		{
			return ui.CompassPanelUW2;
		}

		return ui.CompassBgUW1?[0]?.GetParent() as Panel;
	}

	static bool UsesPanelSubtreeLayout(VrStatusWidgetKind kind)
	{
		return kind is VrStatusWidgetKind.HealthFlask
			or VrStatusWidgetKind.ManaFlask
			or VrStatusWidgetKind.Compass
			or VrStatusWidgetKind.Inventory;
	}

	static Vector2 GetPanelDuplicateOffset(Control source, Rect2 hudRect)
	{
		return source.Position - hudRect.Position;
	}

	static Rect2 GetPowerGemHudRectFixed()
	{
		return UWClass._RES == UWClass.GAME_UW2
			? new Rect2(452f, 604f, 56f, 20f)
			: new Rect2(16f, 556f, 124f, 48f);
	}

	static Rect2 GetEyesStripHudRectFixed()
	{
		return UWClass._RES == UWClass.GAME_UW2
			? new Rect2(440f, 16f, 80f, 12f)
			: new Rect2(512f, 16f, 80f, 12f);
	}

	/// <summary>Gargoyle head ornament on the UW placeholder frame (includes animated eyes).</summary>
	static Rect2 GetEyesGargoyleHudRectFixed()
	{
		return UWClass._RES == UWClass.GAME_UW2
			? new Rect2(408f, 4f, 144f, 48f)
			: new Rect2(472f, 4f, 160f, 48f);
	}

	static Rect2 GetEyesHudRectFixed() => GetEyesGargoyleHudRectFixed();

	static float HeadOverlayNowSeconds() => Time.GetTicksMsec() / 1000f;

	static float GetHeadOverlayDisplaySeconds()
	{
		return Mathf.Max(0.5f, uwsettings.instance.vr_status_panels_display_seconds);
	}

	static float GetHeadOverlayFadeSeconds()
	{
		return Mathf.Max(0.1f, uwsettings.instance.vr_status_panels_fade_seconds);
	}

	static bool HeadOverlaysAlwaysVisible() => uwsettings.instance.vr_status_panels_always_visible;

	static TextureRect GetHudPlaceholderRect()
	{
		var ui = uimanager.instance;
		return UWClass._RES == UWClass.GAME_UW2 ? ui?.placeholderuw2 : ui?.placeholderuw1;
	}

	static float GetStatusScreenWidthMeters()
	{
		var width = uwsettings.instance.vr_status_screen_width;
		return width <= 0.5f ? 2.2f : width;
	}

	static float GetStatusScreenDistanceMeters()
	{
		var distance = uwsettings.instance.vr_status_screen_distance;
		return distance <= 0.5f ? 2f : distance;
	}

	static bool StatusPanelsAlwaysVisible() => HeadOverlaysAlwaysVisible();

	static bool ShouldShowHeadOverlays()
	{
		return IsActive
			&& _headOverlaysVisible
			&& uwsettings.instance.vr_status_panels
			&& !uwsettings.instance.vr_mirror
			&& uimanager.InGame
			&& !IsHudOnMenuScreen()
			&& _xrCamera != null;
	}

	static bool ShouldShowVrStatusPanels() => ShouldShowHeadOverlays();

	static Vector3 GetWidgetOffsetMeters(VrStatusWidgetKind kind)
	{
		var settings = uwsettings.instance;
		return kind switch
		{
			VrStatusWidgetKind.HealthFlask => new Vector3(
				settings.vr_health_flask_offset_x,
				settings.vr_health_flask_offset_y,
				settings.vr_health_flask_offset_z),
			VrStatusWidgetKind.ManaFlask => new Vector3(
				settings.vr_mana_flask_offset_x,
				settings.vr_mana_flask_offset_y,
				settings.vr_mana_flask_offset_z),
			VrStatusWidgetKind.Compass => new Vector3(
				settings.vr_compass_offset_x,
				settings.vr_compass_offset_y,
				settings.vr_compass_offset_z),
			VrStatusWidgetKind.Inventory => new Vector3(
				settings.vr_inventory_offset_x,
				settings.vr_inventory_offset_y,
				settings.vr_inventory_offset_z),
			VrStatusWidgetKind.WeaponAnim => new Vector3(
				settings.vr_weapon_anim_offset_x,
				settings.vr_weapon_anim_offset_y,
				settings.vr_weapon_anim_offset_z),
			VrStatusWidgetKind.PowerGem => new Vector3(
				settings.vr_power_gem_offset_x,
				settings.vr_power_gem_offset_y,
				settings.vr_power_gem_offset_z),
			VrStatusWidgetKind.Eyes => new Vector3(
				settings.vr_eyes_offset_x,
				settings.vr_eyes_offset_y,
				settings.vr_eyes_offset_z),
			_ => Vector3.Zero,
		};
	}

	static Vector3 HudRectCenterToCameraLocal(Rect2 hudRect, Vector2 offsetMeters)
	{
		return HudRectCenterToCameraLocal(hudRect, new Vector3(offsetMeters.X, offsetMeters.Y, 0f));
	}

	static Vector3 HudRectCenterToCameraLocal(Rect2 hudRect, Vector3 offsetMeters)
	{
		var screenWidth = GetStatusScreenWidthMeters();
		var screenHeight = screenWidth * ((float)HudPanelHeightPx / HudPanelWidthPx);
		var cx = (hudRect.Position.X + hudRect.Size.X * 0.5f) / HudPanelWidthPx;
		var cy = (hudRect.Position.Y + hudRect.Size.Y * 0.5f) / HudPanelHeightPx;
		var x = (cx - 0.5f) * screenWidth + offsetMeters.X;
		var y = (0.5f - cy) * screenHeight + offsetMeters.Y + uwsettings.instance.vr_status_panels_offset_y;
		var z = -GetStatusScreenDistanceMeters() - offsetMeters.Z;
		return new Vector3(x, y, z);
	}

	static Vector3 HudRectCenterToCameraLocal(Rect2 hudRect, VrStatusWidgetKind kind) =>
		HudRectCenterToCameraLocal(hudRect, GetWidgetOffsetMeters(kind));

	static Vector2 HudRectToQuadSize(Rect2 hudRect)
	{
		var screenWidth = GetStatusScreenWidthMeters();
		var w = (hudRect.Size.X / HudPanelWidthPx) * screenWidth;
		var h = (hudRect.Size.Y / HudPanelHeightPx) * screenWidth * ((float)HudPanelHeightPx / HudPanelWidthPx);
		return new Vector2(w, h);
	}

	static void SyncControlSubtree(Node source, Node duplicate)
	{
		if (source is TextureRect srcTr && duplicate is TextureRect dupTr)
		{
			dupTr.Texture = srcTr.Texture;
			dupTr.Material = srcTr.Material;
			dupTr.Visible = srcTr.Visible;
			dupTr.Modulate = srcTr.Modulate;
			dupTr.Position = srcTr.Position;
			dupTr.Size = srcTr.Size;
			dupTr.TextureFilter = srcTr.TextureFilter;
		}
		else if (source is Label srcLbl && duplicate is Label dupLbl)
		{
			dupLbl.Text = srcLbl.Text;
			dupLbl.Visible = srcLbl.Visible;
			dupLbl.Modulate = srcLbl.Modulate;
			dupLbl.Position = srcLbl.Position;
			dupLbl.Size = srcLbl.Size;
		}
		else if (source is CanvasItem srcCi && duplicate is CanvasItem dupCi)
		{
			dupCi.Visible = srcCi.Visible;
			dupCi.Modulate = srcCi.Modulate;
		}

		foreach (var child in source.GetChildren())
		{
			var dupChild = duplicate.GetNodeOrNull(new NodePath(child.Name));
			if (dupChild != null)
			{
				SyncControlSubtree(child, dupChild);
			}
		}
	}

	static void SyncStatusWidgetVisuals(VrStatusWidget widget)
	{
		if (widget.Source == null || widget.Duplicate == null)
		{
			return;
		}

		if (UsesPanelSubtreeLayout(widget.Kind))
		{
			SyncControlSubtree(widget.Source, widget.Duplicate);
			return;
		}

		// Power gem is a single TextureRect; eyes use a gargoyle background plus animated eyes.
		if (widget.Kind == VrStatusWidgetKind.Eyes && widget.Duplicate is TextureRect eyesDup && widget.Source is TextureRect eyesSrc)
		{
			var placeholder = GetHudPlaceholderRect();
			if (widget.GargoyleBackground is TextureRect gargDup && placeholder != null)
			{
				gargDup.Texture = placeholder.Texture;
				gargDup.Modulate = placeholder.Modulate;
				gargDup.Visible = true;
			}

			eyesDup.Texture = eyesSrc.Texture;
			eyesDup.Visible = eyesSrc.Visible;
			eyesDup.Modulate = eyesSrc.Modulate;
			eyesDup.Position = widget.EyesStripRect.Position - widget.HudRect.Position;
			eyesDup.Size = widget.EyesStripRect.Size;
			return;
		}

		if (widget.Source is TextureRect srcTr && widget.Duplicate is TextureRect dupTr)
		{
			dupTr.Texture = srcTr.Texture;
			dupTr.Material = srcTr.Material;
			dupTr.Visible = srcTr.Visible;
			dupTr.Modulate = srcTr.Modulate;
			dupTr.Position = Vector2.Zero;
			dupTr.Size = widget.HudRect.Size;
			dupTr.TextureFilter = srcTr.TextureFilter;
			return;
		}

		SyncControlSubtree(widget.Source, widget.Duplicate);
		widget.Duplicate.Position = Vector2.Zero;
	}

	static bool EnsureStatusWidgetViewport(VrStatusWidget widget, Node3D underworld)
	{
		if (widget.Viewport != null && GodotObject.IsInstanceValid(widget.Viewport))
		{
			return true;
		}

		if (widget.Source == null)
		{
			return false;
		}

		var viewportSize = new Vector2I(
			Mathf.CeilToInt(widget.HudRect.Size.X),
			Mathf.CeilToInt(widget.HudRect.Size.Y));
		widget.Viewport = new SubViewport
		{
			Name = $"VrStatusViewport_{widget.Kind}",
			Size = viewportSize,
			TransparentBg = true,
			Disable3D = true,
			HandleInputLocally = false,
			GuiDisableInput = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			Msaa2D = Viewport.Msaa.Disabled,
		};
		underworld.AddChild(widget.Viewport);
		widget.Viewport.CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest;

		if (widget.Kind == VrStatusWidgetKind.Eyes)
		{
			var placeholder = GetHudPlaceholderRect();
			if (placeholder == null)
			{
				return false;
			}

			var bgDuplicate = placeholder.Duplicate() as TextureRect;
			if (bgDuplicate == null)
			{
				widget.Viewport.QueueFree();
				widget.Viewport = null;
				return false;
			}

			bgDuplicate.Position = -widget.HudRect.Position;
			bgDuplicate.Size = new Vector2(HudPanelWidthPx, HudPanelHeightPx);
			bgDuplicate.Visible = true;
			bgDuplicate.MouseFilter = Control.MouseFilterEnum.Ignore;
			widget.GargoyleBackground = bgDuplicate;
			widget.Viewport.AddChild(bgDuplicate);

			var eyesDuplicate = widget.Source.Duplicate() as Control;
			if (eyesDuplicate == null)
			{
				widget.Viewport.QueueFree();
				widget.Viewport = null;
				return false;
			}

			eyesDuplicate.Position = widget.EyesStripRect.Position - widget.HudRect.Position;
			eyesDuplicate.Size = widget.EyesStripRect.Size;
			widget.Viewport.AddChild(eyesDuplicate);
			widget.Duplicate = eyesDuplicate;
			return true;
		}

		var duplicate = widget.Source.Duplicate() as Control;
		if (duplicate == null)
		{
			widget.Viewport.QueueFree();
			widget.Viewport = null;
			return false;
		}

		widget.Viewport.AddChild(duplicate);
		if (UsesPanelSubtreeLayout(widget.Kind))
		{
			duplicate.Position = GetPanelDuplicateOffset(widget.Source as Control, widget.HudRect);
		}
		else
		{
			duplicate.Position = Vector2.Zero;
			duplicate.Size = widget.HudRect.Size;
		}

		widget.Duplicate = duplicate;

		if (widget.Kind == VrStatusWidgetKind.Inventory)
		{
			widget.OverlayCursor = new TextureRect
			{
				Name = "VrInventoryOverlayCursor",
				Visible = false,
				MouseFilter = Control.MouseFilterEnum.Ignore,
				TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
				ZIndex = 100,
			};
			widget.Viewport.AddChild(widget.OverlayCursor);
		}

		return true;
	}

	static void EnsureStatusWidgetPanel(VrStatusWidget widget)
	{
		if (widget.Panel != null && GodotObject.IsInstanceValid(widget.Panel))
		{
			return;
		}

		widget.Material = new StandardMaterial3D
		{
			AlbedoTexture = widget.Viewport.GetTexture(),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DisableReceiveShadows = true,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
		};

		var quadSize = HudRectToQuadSize(widget.HudRect);
		widget.Panel = new MeshInstance3D
		{
			Name = $"VrStatusPanel_{widget.Kind}",
			Mesh = new QuadMesh
			{
				Size = quadSize,
				Material = widget.Material,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Layers = main.LayerGeo | main.LayerXFER,
			Visible = false,
		};
		_xrCamera.AddChild(widget.Panel);
	}

	static void TryAddStatusWidget(VrStatusWidget widget)
	{
		foreach (var existing in _statusWidgets)
		{
			if (existing.Kind == widget.Kind)
			{
				return;
			}
		}

		_statusWidgets.Add(widget);
	}

	static void InitStatusWidgetsIfNeeded()
	{
		var ui = uimanager.instance;
		if (ui == null)
		{
			return;
		}

		if (ui.HealthFlaskPanel != null)
		{
			TryAddStatusWidget(new VrStatusWidget
			{
				Kind = VrStatusWidgetKind.HealthFlask,
				HudRect = GetHealthFlaskHudRectFixed(),
				Source = ui.HealthFlaskPanel,
				FadeWhenInactive = true,
			});
		}

		if (ui.ManaFlaskPanel != null)
		{
			TryAddStatusWidget(new VrStatusWidget
			{
				Kind = VrStatusWidgetKind.ManaFlask,
				HudRect = GetManaFlaskHudRectFixed(),
				Source = ui.ManaFlaskPanel,
				FadeWhenInactive = true,
			});
		}

		var compassPanel = GetCompassPanel();
		if (compassPanel != null)
		{
			TryAddStatusWidget(new VrStatusWidget
			{
				Kind = VrStatusWidgetKind.Compass,
				HudRect = GetCompassHudRectFixed(),
				Source = compassPanel,
				ShowWhileInGame = true,
			});
		}

		if (ui.PanelInventory != null)
		{
			TryAddStatusWidget(new VrStatusWidget
			{
				Kind = VrStatusWidgetKind.Inventory,
				HudRect = GetInventoryHudRectFixed(),
				Source = ui.PanelInventory,
				ShowWhileInGame = true,
			});
		}

		var weaponAnim = GetWeaponAnimRect();
		if (weaponAnim != null)
		{
			TryAddStatusWidget(new VrStatusWidget
			{
				Kind = VrStatusWidgetKind.WeaponAnim,
				HudRect = GetWeaponAnimHudRectFixed(),
				Source = weaponAnim,
				FadeWhenInactive = true,
			});
		}

		var powerGem = uimanager.PowerGem;
		if (powerGem != null)
		{
			TryAddStatusWidget(new VrStatusWidget
			{
				Kind = VrStatusWidgetKind.PowerGem,
				HudRect = GetPowerGemHudRectFixed(),
				Source = powerGem,
				FadeWhenInactive = true,
			});
		}

		if (ui.Eyes != null)
		{
			TryAddStatusWidget(new VrStatusWidget
			{
				Kind = VrStatusWidgetKind.Eyes,
				HudRect = GetEyesGargoyleHudRectFixed(),
				EyesStripRect = GetEyesStripHudRectFixed(),
				Source = ui.Eyes,
				FadeWhenInactive = true,
			});
		}
	}

	static void EnsureVrStatusPanels(Node3D underworld = null)
	{
		if (!uwsettings.instance.vr_status_panels || _xrCamera == null)
		{
			return;
		}

		underworld ??= _gameRoot?.GetParent<Node3D>();
		if (underworld == null)
		{
			return;
		}

		var ui = underworld.GetNodeOrNull<CanvasLayer>("UI");
		if (ui != null)
		{
			EnsureVrUiViewport(underworld, ui);
		}

		InitStatusWidgetsIfNeeded();
		foreach (var widget in _statusWidgets)
		{
			if (!EnsureStatusWidgetViewport(widget, underworld))
			{
				continue;
			}

			EnsureStatusWidgetPanel(widget);
		}
	}

	static bool IsWidgetContentActive(VrStatusWidget widget)
	{
		return widget.Kind switch
		{
			VrStatusWidgetKind.HealthFlask => true,
			VrStatusWidgetKind.ManaFlask => true,
			VrStatusWidgetKind.Compass => uimanager.InGame,
			VrStatusWidgetKind.Inventory => uimanager.InGame,
			VrStatusWidgetKind.WeaponAnim => IsWeaponAnimActive(),
			VrStatusWidgetKind.PowerGem => IsPowerGemActive(),
			VrStatusWidgetKind.Eyes => AreEyesActive(),
			_ => false,
		};
	}

	static bool ShouldHoldWidgetOpen(VrStatusWidget widget)
	{
		return widget.Kind switch
		{
			VrStatusWidgetKind.PowerGem => IsPowerGemActive(),
			VrStatusWidgetKind.WeaponAnim => IsWeaponAnimActive() || IsWeaponAnimStageActive(),
			VrStatusWidgetKind.Eyes => AreEyesActive(),
			_ => false,
		};
	}

	static void StartWidgetDisplayTimer(VrStatusWidget widget)
	{
		widget.HideAfterTime = HeadOverlayNowSeconds() + GetHeadOverlayDisplaySeconds();
		widget.Alpha = 1f;
	}

	static void UpdateWidgetFade(VrStatusWidget widget)
	{
		if (!widget.FadeWhenInactive)
		{
			widget.Alpha = IsWidgetContentActive(widget) ? 1f : 0f;
			return;
		}

		var holdOpen = ShouldHoldWidgetOpen(widget);
		if (holdOpen)
		{
			widget.HideAfterTime = -1f;
			widget.Alpha = 1f;
			widget.HoldWasActive = true;
			return;
		}

		if (widget.HoldWasActive)
		{
			widget.HoldWasActive = false;
			widget.HideAfterTime = HeadOverlayNowSeconds() + GetHeadOverlayDisplaySeconds();
			widget.Alpha = 1f;
			return;
		}

		if (widget.HideAfterTime < 0f)
		{
			return;
		}

		var now = HeadOverlayNowSeconds();
		if (now < widget.HideAfterTime)
		{
			widget.Alpha = 1f;
			return;
		}

		var fadeT = (now - widget.HideAfterTime) / GetHeadOverlayFadeSeconds();
		widget.Alpha = Mathf.Clamp(1f - fadeT, 0f, 1f);
		if (widget.Alpha <= 0f)
		{
			widget.HideAfterTime = -1f;
		}
	}

	static void ApplyWidgetMaterialAlpha(VrStatusWidget widget)
	{
		if (widget.Material == null)
		{
			return;
		}

		widget.Material.AlbedoColor = new Color(1f, 1f, 1f, widget.Alpha);
		widget.Material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
	}

	static void UpdateStatusWidget(VrStatusWidget widget)
	{
		if (widget.Panel == null || widget.Viewport == null)
		{
			return;
		}

		if (!ShouldShowVrStatusPanels())
		{
			widget.Panel.Visible = false;
			return;
		}

		if (StatusPanelsAlwaysVisible())
		{
			widget.Alpha = 1f;
		}
		else if (widget.ShowWhileInGame)
		{
			if (!IsWidgetContentActive(widget))
			{
				widget.Panel.Visible = false;
				return;
			}

			widget.Alpha = 1f;
		}
		else
		{
			if (widget.HideAfterTime < 0f && !ShouldHoldWidgetOpen(widget))
			{
				widget.Panel.Visible = false;
				return;
			}

			UpdateWidgetFade(widget);
			if (widget.Alpha <= 0.001f)
			{
				widget.Panel.Visible = false;
				return;
			}
		}

		SyncStatusWidgetVisuals(widget);

		if (widget.Kind == VrStatusWidgetKind.ManaFlask)
		{
			widget.HudRect = GetManaFlaskHudRectFixed();
		}
		else if (widget.Kind == VrStatusWidgetKind.Inventory)
		{
			widget.HudRect = GetInventoryHudRectFixed();
		}
		else if (widget.Kind == VrStatusWidgetKind.WeaponAnim)
		{
			widget.HudRect = GetWeaponAnimHudRectFixed();
		}

		var quadSize = HudRectToQuadSize(widget.HudRect);
		if (widget.Panel.Mesh is QuadMesh quad)
		{
			quad.Size = quadSize;
		}

		widget.Panel.Position = HudRectCenterToCameraLocal(widget.HudRect, widget.Kind);
		widget.Panel.RotationDegrees = Vector3.Zero;
		ApplyWidgetMaterialAlpha(widget);
		widget.Panel.Visible = true;
	}

	static void UpdateVrStatusPanels()
	{
		if (!uwsettings.instance.vr_status_panels || !IsActive || uwsettings.instance.vr_mirror)
		{
			return;
		}

		EnsureVrStatusPanels();
		foreach (var widget in _statusWidgets)
		{
			UpdateStatusWidget(widget);
		}
	}

	/// <summary>Call when enemy damage eyes change level.</summary>
	public static void NotifyVrEyesUpdated()
	{
		if (!uwsettings.instance.vr_status_panels || !IsActive || uwsettings.instance.vr_mirror)
		{
			return;
		}

		InitStatusWidgetsIfNeeded();
		foreach (var widget in _statusWidgets)
		{
			if (widget.Kind == VrStatusWidgetKind.Eyes && AreEyesActive())
			{
				widget.HideAfterTime = -1f;
				widget.Alpha = 1f;
				widget.HoldWasActive = true;
			}
		}
	}

	/// <summary>Call when attack power gem frame changes.</summary>
	public static void NotifyVrPowerGemUpdated()
	{
		if (!uwsettings.instance.vr_status_panels || !IsActive || uwsettings.instance.vr_mirror)
		{
			return;
		}

		InitStatusWidgetsIfNeeded();
		foreach (var widget in _statusWidgets)
		{
			if (widget.Kind == VrStatusWidgetKind.PowerGem && IsPowerGemActive())
			{
				widget.HideAfterTime = -1f;
				widget.Alpha = 1f;
				widget.HoldWasActive = true;
			}
		}
	}

	/// <summary>Call when a weapon attack/draw animation frame changes.</summary>
	public static void NotifyVrWeaponAnimUpdated()
	{
		if (!uwsettings.instance.vr_status_panels || !IsActive || uwsettings.instance.vr_mirror)
		{
			return;
		}

		InitStatusWidgetsIfNeeded();
		foreach (var widget in _statusWidgets)
		{
			if (widget.Kind != VrStatusWidgetKind.WeaponAnim)
			{
				continue;
			}

			if (IsWeaponAnimActive() || IsWeaponAnimStageActive())
			{
				widget.HideAfterTime = -1f;
				widget.Alpha = 1f;
				widget.HoldWasActive = true;
			}
			else
			{
				widget.HoldWasActive = false;
				widget.HideAfterTime = -1f;
				widget.Alpha = 0f;
			}
		}
	}

	/// <summary>Call when the player takes damage and the health flask animates down.</summary>
	public static void NotifyVrHealthFlaskDamage()
	{
		if (!uwsettings.instance.vr_status_panels || !IsActive || uwsettings.instance.vr_mirror)
		{
			return;
		}

		InitStatusWidgetsIfNeeded();
		foreach (var widget in _statusWidgets)
		{
			if (widget.Kind == VrStatusWidgetKind.HealthFlask)
			{
				StartWidgetDisplayTimer(widget);
			}
		}
	}

	/// <summary>Call when the player spends mana and the mana flask animates down.</summary>
	public static void NotifyVrManaFlaskUsed()
	{
		if (!uwsettings.instance.vr_status_panels || !IsActive || uwsettings.instance.vr_mirror)
		{
			return;
		}

		InitStatusWidgetsIfNeeded();
		foreach (var widget in _statusWidgets)
		{
			if (widget.Kind == VrStatusWidgetKind.ManaFlask)
			{
				StartWidgetDisplayTimer(widget);
			}
		}
	}

	public static bool ShouldEnterCinemaForCutscene(int cutsceneNo, bool wasInGame)
	{
		return IsActive
			&& !uwsettings.instance.vr_mirror
			&& wasInGame
			&& (cutsceneNo == 0x102 || cutsceneNo == 0x103);
	}

	/// <summary>Show the full HUD viewport on the large head-locked screen (death cutscenes).</summary>
	public static bool EnterVrCinemaScreen()
	{
		if (!IsActive || uwsettings.instance.vr_mirror || _vrUiOnMenuTv || _xrCamera == null)
		{
			return false;
		}

		var underworld = _gameRoot?.GetParent<Node3D>();
		if (underworld == null)
		{
			return false;
		}

		var ui = GetVrUiCanvasLayer(underworld);
		if (ui == null || !EnsureVrUiViewport(underworld, ui))
		{
			return false;
		}

		var width = uwsettings.instance.vr_menu_screen_width;
		if (width <= 0.5f)
		{
			width = 2.2f;
		}

		var aspect = (float)HudPanelHeightPx / HudPanelWidthPx;
		var quadSize = new Vector2(width, width * aspect);
		if (_hudPanel == null)
		{
			_hudPanel = new MeshInstance3D
			{
				Name = "VrCinemaScreen",
				Mesh = new QuadMesh
				{
					Size = quadSize,
					Material = CreateVrUiQuadMaterial(),
				},
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Layers = main.LayerGeo | main.LayerXFER,
			};
		}
		else
		{
			_hudPanel.GetParent()?.RemoveChild(_hudPanel);
			ResizeVrHudDisplay(quadSize);
			if (_hudPanel.Mesh is QuadMesh quad)
			{
				quad.Material = CreateVrUiQuadMaterial();
				if (quad.Material is StandardMaterial3D menuMat)
				{
					ApplyMenuTvMaterialBrightness(menuMat);
				}
			}
		}

		_hudPanel.Position = MenuTvCameraLocalPosition;
		_hudPanel.RotationDegrees = Vector3.Zero;
		_xrCamera.AddChild(_hudPanel);
		_vrCinemaFromGameplay = true;
		_vrUiOnMenuTv = true;
		_hudPanelVisible = true;
		_hudPanel.Visible = true;
		UpdateXrViewportHdrForUiMode();
		_hudViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		RefreshVrUiQuadMaterial();
		VrDiagLog.Print("[VR] Cinema screen enabled for death cutscene.");
		return true;
	}

	public static void ExitVrCinemaScreen(bool returnToHandHud)
	{
		if (!_vrCinemaFromGameplay)
		{
			return;
		}

		_vrCinemaFromGameplay = false;
		if (returnToHandHud && uwsettings.instance.vr_hud_panel)
		{
			TransitionMenuTvToHandHud();
			return;
		}

		_vrUiOnMenuTv = true;
		UpdateXrViewportHdrForUiMode();
	}

	static VrStatusWidget GetStatusWidget(VrStatusWidgetKind kind)
	{
		foreach (var widget in _statusWidgets)
		{
			if (widget.Kind == kind)
			{
				return widget;
			}
		}

		return null;
	}

	static bool IsStatusWidgetClickable(VrStatusWidgetKind kind)
	{
		return kind is VrStatusWidgetKind.Inventory
			or VrStatusWidgetKind.HealthFlask
			or VrStatusWidgetKind.ManaFlask
			or VrStatusWidgetKind.Compass;
	}

	static bool IsStatusOverlayPointerActive()
	{
		return ShouldShowVrStatusPanels() && !uimanager.blockinput;
	}

	static bool IsStatusWidgetInteractive(VrStatusWidget widget)
	{
		return widget != null
			&& IsStatusWidgetClickable(widget.Kind)
			&& widget.Panel != null
			&& widget.Panel.Visible
			&& widget.Alpha > 0.001f;
	}

	static void ClearStatusOverlayPointerState()
	{
		_statusOverlayHovering = false;
		_statusOverlayHoverKind = default;
		_statusOverlayHitWorld = default;
		_statusOverlayLeftWasPressed = false;
		_statusOverlayRightWasPressed = false;
		_lastStatusOverlayHudPos = new Vector2(-1f, -1f);
		UpdateInventoryOverlayCursor(default, show: false);
	}

	static void UpdateInventoryOverlayCursor(Vector2 hudPos, bool show)
	{
		var widget = GetStatusWidget(VrStatusWidgetKind.Inventory);
		if (widget?.OverlayCursor == null)
		{
			return;
		}

		if (!show || playerdat.ObjectInHand == -1)
		{
			widget.OverlayCursor.Visible = false;
			return;
		}

		var objList = UWTileMap.current_tilemap?.LevelObjects;
		if (objList == null || playerdat.ObjectInHand <= 0 || playerdat.ObjectInHand >= objList.Length)
		{
			widget.OverlayCursor.Visible = false;
			return;
		}

		var obj = objList[playerdat.ObjectInHand];
		var tex = uimanager.grObjects.LoadImageAt(obj.item_id);
		if (tex == null)
		{
			widget.OverlayCursor.Visible = false;
			return;
		}

		widget.OverlayCursor.Texture = tex;
		widget.OverlayCursor.Material = uimanager.grObjects.GetMaterial(obj.item_id);
		widget.OverlayCursor.Size = tex.GetSize() * 4;
		var localPos = hudPos - widget.HudRect.Position;
		widget.OverlayCursor.Position = localPos + new Vector2(-tex.GetWidth(), -tex.GetHeight());
		widget.OverlayCursor.Visible = true;
	}

	static bool TryGetStatusWidgetHit(
		VrStatusWidget widget,
		Vector3 rayOrigin,
		Vector3 rayDir,
		float maxDistance,
		out Vector2 hudViewportPos,
		out Vector3 hitWorld)
	{
		hudViewportPos = default;
		hitWorld = rayOrigin + rayDir * maxDistance;
		if (widget?.Panel == null || widget.Viewport == null || widget.Panel.Mesh is not QuadMesh quad)
		{
			return false;
		}

		var xf = widget.Panel.GlobalTransform;
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
		var vpSize = widget.Viewport.Size;
		var localVp = new Vector2(
			Mathf.Clamp(u * vpSize.X, 0f, vpSize.X - 1f),
			Mathf.Clamp((1f - v) * vpSize.Y, 0f, vpSize.Y - 1f));
		hudViewportPos = widget.HudRect.Position + localVp;
		return true;
	}

	static bool TryGetClosestClickableStatusWidgetHit(
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
			if (!IsStatusWidgetInteractive(candidate))
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

	/// <summary>
	/// Raycast clickable head-locked status overlays and forward hits to the real HUD controls.
	/// </summary>
	static void ApplyStatusOverlayPointerInput()
	{
		if (!IsActive || uwsettings.instance.vr_mirror || _hudViewport == null || GetAimController() == null)
		{
			ClearStatusOverlayPointerState();
			return;
		}

		if (_hudPointerHovering && !_statusOverlayHovering)
		{
			ClearStatusOverlayPointerState();
			return;
		}

		if (!IsStatusOverlayPointerActive())
		{
			ClearStatusOverlayPointerState();
			return;
		}

		var rayOrigin = GetAimRayOrigin();
		var rayDir = GetControllerRayDir();
		var hovering = TryGetClosestClickableStatusWidgetHit(
			rayOrigin,
			rayDir,
			StatusOverlayPointerMaxDistance,
			out var widget,
			out var hudPos,
			out var hitWorld);

		_statusOverlayHovering = hovering;
		_statusOverlayHoverKind = hovering ? widget.Kind : default;
		_statusOverlayHitWorld = hitWorld;
		if (!hovering)
		{
			_statusOverlayLeftWasPressed = false;
			_statusOverlayRightWasPressed = false;
			_lastStatusOverlayHudPos = new Vector2(-1f, -1f);
			UpdateInventoryOverlayCursor(default, show: false);
			return;
		}

		if (_hudMouseLayer != null)
		{
			_hudMouseLayer.Visible = true;
		}

		if (hudPos != _lastStatusOverlayHudPos)
		{
			_lastStatusOverlayHudPos = hudPos;
			PushHudMouseMotion(hudPos);
		}

		UpdateInventoryOverlayCursor(hudPos, show: widget.Kind == VrStatusWidgetKind.Inventory);

		var leftPressed = IsHudPointerLeftClickHeld(menuScreen: false);
		if (leftPressed && !_statusOverlayLeftWasPressed)
		{
			if (!TryDismissMessageMore()
				&& !TryConfirmYesNoPrompt(hudPos, yes: true)
				&& !TrySelectConversationOption(hudPos))
			{
				PushVrHudMouseClick(hudPos, MouseButton.Left);
			}
		}
		_statusOverlayLeftWasPressed = leftPressed;

		var rightPressed = IsHudPointerRightClickHeld(menuScreen: false);
		if (rightPressed && !_statusOverlayRightWasPressed)
		{
			if (!TryDismissMessageMore()
				&& !TryConfirmYesNoPrompt(hudPos, yes: false)
				&& !TrySelectConversationOption(hudPos))
			{
				PushVrHudMouseClick(hudPos, MouseButton.Right);
			}
		}
		_statusOverlayRightWasPressed = rightPressed;
	}
}
