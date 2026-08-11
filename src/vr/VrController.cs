using System;
using Godot;

namespace Underworld;

/// <summary>
/// Minimal VR support: OpenXR head tracking with thumbstick locomotion.
/// Enable via "vr": true in settings.json (user://settings.json).
/// </summary>
public static partial class VrController
{
	public static bool IsActive { get; private set; }

	/// <summary>True while intro/menu/chargen UI is shown on the front VR menu screen.</summary>
	public static bool UsesFrontMenuScreen => IsHudOnMenuScreen();

	/// <summary>Native VR: OpenXR head pose replaces DOS camera bob on the flat gimbals.</summary>
	public static bool SuppressFlatCameraBob =>
		IsActive && !uwsettings.instance.vr_mirror;

	/// <summary>Right-hand laser is over the 3D viewport hole in the HUD (not chrome/buttons).</summary>
	public static bool IsHud3DViewportHovering { get; private set; }

	/// <summary>Legacy HUD viewport right-click; no longer used for VR attack charging.</summary>
	public static bool IsHud3DViewportRightHeld { get; private set; }

	/// <summary>Controller laser is aimed into the live VR world (not the HUD panel).</summary>
	public static bool IsVrWorldPointerActive { get; private set; }

	/// <summary>Legacy world right-click; no longer used for VR attack charging.</summary>
	public static bool IsVrWorldRightHeld { get; private set; }

	static Vector3 _baseGodotScale;
	static bool _vrWorldScaleApplied;

	static SubViewport _gameViewport;
	static Node3D _mirrorYawGimbal;
	static Node3D _mirrorRollGimbal;
	static Node3D _mirrorPitchGimbal;
	static XROrigin3D _xrOrigin;
	static XRCamera3D _xrCamera;
	static XRController3D _leftController;
	static XRController3D _rightController;
	static SubViewport _hudViewport;
	static MeshInstance3D _hudPanel;
	static MeshInstance3D _messageScrollPanel;
	static SubViewport _messageScrollViewport;
	static StandardMaterial3D _messageScrollMaterial;
	static float _messageScrollHideAfterTime = -1f;
	static float _messageScrollAlpha;
	static bool _messageScrollHoldWasActive;
	static CanvasLayer _hudMouseLayer;
	static bool _vrUiOnMenuTv;
	static bool _vrCinemaFromGameplay;
	static bool _vrGameplayEnterPending;
	static bool _vrShortcutTriggerWasPressed;
	static bool _vrEscapeWasPressed;
	static MeshInstance3D _pointerLaser;
	static CylinderMesh _pointerLaserMesh;
	static Node3D _pointerLaserWorldParent;
	static bool _pointerLaserOnCamera;
	static bool _pointerLaserOnController;
	static bool _introDiagLastLaserVisible;
	static bool _hudPanelVisible = true;
	static bool _headOverlaysVisible = true;
	static bool _hudMenuToggleWasPressed;
	static bool _hudPanelToggleWasPressed;
	static Vector2 _lastHudPointerPos = new(-1f, -1f);
	static bool _hudPointerHovering;
	static bool _hudPointerLeftWasPressed;
	static bool _hudPointerRightWasPressed;
	static bool _statusOverlayHovering;
	static VrStatusWidgetKind _statusOverlayHoverKind;
	static Vector3 _statusOverlayHitWorld;
	static Vector2 _lastStatusOverlayHudPos = new(-1f, -1f);
	static bool _statusOverlayLeftWasPressed;
	static bool _statusOverlayRightWasPressed;
	static bool _dominantGripWasPressed;
	static bool _dominantTriggerWasPressed;
	static bool _offHandGripWasPressed;
	static bool _offHandTriggerWasPressed;
	static bool _spellCastTriggerWasPressed;
	static bool _spellCastGripWasPressed;
	static bool _numberPadLeftWasPressed;
	static Vector2 _lastNumberPadPointerPos = new(-1f, -1f);
	static bool _combatToggleWasPressed;
	static float _pendingPickupRayDistance;
	static int _vrHeldObjectIndex = -1;
	static float _vrHeldRayDistance = 1f;
	const float InventoryHeldRayDistance = 1.2f;
	static main _gameRoot;
	static SceneTree _sceneTree;
	static float _snapTurnCooldown;
	static bool _jumpWasPressed;
	static bool _recenterWasPressed;
	static bool _spellCastShortcutWasPressed;
	static bool _xrOriginFloorInitialized;
	static Vector3 _lastSyncedDisplayFloorPos;
	static Vector3 _motionStepPrevFloor;
	static Vector3 _motionStepCurrFloor;
	static bool _motionStepInitialized;
	static float _xrPlaySpaceYawRadians;
	static int _debugFrameCounter;
	static int _setupWaitFrames;

	static bool _worldSetupPending;
	static bool _worldSetupComplete;
	static bool _openXrOutputEnabled;
	static bool _processFrameHooked;
	static bool _useHeadRelativeMotion;
	static short _motionYaw;
	static MeshInstance3D _bodyMarker;

	const float StickDeadzone = 0.35f;
	const float SnapTurnDegrees = 45f;
	const float SnapTurnCooldownSeconds = 0.35f;
	const int DebugLogIntervalFrames = 180;
	const float MirrorScreenWidthMeters = 1.5f;
	const float MirrorScreenDistanceMeters = 0.85f;
	const float BodyMarkerScale = 0.1f;
	const float HudPointerMaxDistance = 2.5f;
	const float MenuTvPointerMaxDistance = 4f;
	const float StatusOverlayPointerMaxDistance = 4f;
	static Vector3 MenuTvCameraLocalPosition => new(
		0f,
		uwsettings.instance.vr_menu_screen_offset_y,
		-Mathf.Max(1.2f, uwsettings.instance.vr_menu_screen_distance));
	const float PointerLaserRadius = 0.0025f;
	const int HudPanelWidthPx = 1280;
	const int HudPanelHeightPx = 800;
	/// <summary>Left-controller local offset for the HUD quad (metres).</summary>
	static readonly Vector3 HudPanelLocalPosition = new(0.04f, 0.06f, 0.14f);
	/// <summary>Tilt/yaw so the panel faces roughly toward the user from the left grip.</summary>
	static readonly Vector3 HudPanelLocalRotationDegrees = new(-70f, 180f, 180f);
	/// <summary>Grip-to-aim offset for menu pointer laser visual (controller local metres). Ray hits stay on grip pose.</summary>
	static readonly Vector3 MenuPointerLaserAimOffset = new(0f, -0.032f, -0.048f);
	/// <summary>Eye height above floor in Godot metres (0xA4 game units).</summary>
	static float GameEyeHeightMeters => (0xA4 / 1024f) * tileMapRender.godotscale.Y;

	static float GetGameFloorY()
	{
		return (motion.playerMotionParams.z_4 / 1024f) * tileMapRender.godotscale.Y;
	}

	static readonly StringName[] StickActions =
	{
		"primary",
		"thumbstick",
		"joystick",
	};

	static readonly StringName[] DoorUseButtonActions =
	{
		"grip_click",
	};

	static readonly StringName[] RecenterButtonActions =
	{
		"by_button",
		"b_button",
	};

	static readonly StringName[] RecenterStickClickActions =
	{
		"thumbstick_click",
		"joystick_click",
		"primary_click",
	};

	static readonly StringName[] JumpButtonActions =
	{
		"ax_button",
		"a_button",
	};

	// Left-hand ax_button is Quest X (spell cast shortcut while runes are ready).
	static readonly StringName[] SpellCastShortcutActions =
	{
		"ax_button",
		"x_button",
	};

	// Left-hand by_button is Quest Y (head overlay toggle).
	static readonly StringName[] HeadOverlayToggleButtonActions =
	{
		"by_button",
		"y_button",
	};

	// Left-hand menu button (HUD panel toggle).
	static readonly StringName[] HudPanelToggleButtonActions =
	{
		"menu_button",
	};

	static readonly StringName[] HudLeftClickActions =
	{
		"trigger_click",
	};

	static readonly StringName[] HudRightClickActions =
	{
		"grip_click",
	};

	public static void InitExplorePlayer()
	{
		playerdat.InitEmptyPlayer("Avatar");
		playerdat.STR = 18;
		playerdat.DEX = 18;
		playerdat.INT = 18;
		playerdat.RecalculateHPManaMaxWeight(true);
		playerdat.play_hp = playerdat.max_hp;
	}

	/// <summary>Flush and close VR log files (call on quit).</summary>
	public static void CloseLogSessions()
	{
		VrCombatMotionLog.CloseSession();
		VrDiagLog.CloseSession();
	}

	public static void TryInitialize(main gameRoot)
	{
		if (!uwsettings.instance.vr)
		{
			return;
		}

		var xrInterface = XRServer.FindInterface("OpenXR");
		if (xrInterface == null)
		{
			GD.PushWarning("OpenXR interface not found. Running in flat-screen mode.");
			ResetVrWorldScale();
			RenderingServer.GlobalShaderParameterSet("final_color_pass", false);
			uwsettings.instance.vr = false;
			return;
		}

		VrDiagLog.EnsureSession();
		VrDebugLog("TryInitialize", $"OpenXR found, initialized={xrInterface.IsInitialized()}");

		ApplyVrWorldScale(gameRoot);

		if (!xrInterface.IsInitialized() && !xrInterface.Initialize())
		{
			GD.PushWarning("OpenXR failed to initialize. Running in flat-screen mode.");
			ResetVrWorldScale();
			RenderingServer.GlobalShaderParameterSet("final_color_pass", false);
			uwsettings.instance.vr = false;
			return;
		}

		XRServer.PrimaryInterface = xrInterface;
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		ConfigureRootWindowForXr(gameRoot.GetTree());

		VrDiagLog.Print("[VR] OpenXR initialized; waiting for SceneTree.ProcessFrame to create XRCamera.");
	}

	/// <summary>
	/// Scale simulation coords and tile geometry together (must run before the level is built).
	/// </summary>
	static void ApplyVrWorldScale(main gameRoot)
	{
		if (_vrWorldScaleApplied)
		{
			return;
		}

		var scale = uwsettings.instance.vr_world_scale;
		if (scale <= 0f)
		{
			scale = 1f;
		}

		tileMapRender.WorldScaleFactor = scale;

		var spriteScale = uwsettings.instance.vr_sprite_scale;
		if (spriteScale <= 0f)
		{
			spriteScale = scale;
		}
		ArtLoader.SpriteScaleFactor = spriteScale;

		if (Mathf.IsEqualApprox(scale, 1f) && Mathf.IsEqualApprox(spriteScale, 1f))
		{
			_vrWorldScaleApplied = true;
			return;
		}

		_baseGodotScale = tileMapRender.godotscale;
		tileMapRender.godotscale = _baseGodotScale * scale;

		EnsureVrTilemapScale(gameRoot);

		_vrWorldScaleApplied = true;
		VrDiagLog.Print($"[VR] World scale {scale}x, sprite scale {spriteScale}x — godotscale={tileMapRender.godotscale}, tilemap.Scale={GetTilemapNode(gameRoot)?.Scale}");
	}

	/// <summary>Tile verts use hardcoded 1.2f; scale the tilemap node so geometry matches godotscale coords.</summary>
	public static void EnsureVrTilemapScale(main gameRoot = null)
	{
		var scale = tileMapRender.WorldScaleFactor;
		if (scale <= 1f)
		{
			return;
		}

		var tilemap = GetTilemapNode(gameRoot ?? main.instance);
		if (tilemap != null)
		{
			tilemap.Scale = Vector3.One * scale;
		}
	}

	public static Node3D GetTilemapNode(main gameRoot)
	{
		return gameRoot?.GetNodeOrNull<Node3D>("/root/Underworld/tilemap")
			?? gameRoot?.GetNodeOrNull<Node3D>("../tilemap");
	}

	static void ResetVrWorldScale()
	{
		if (!_vrWorldScaleApplied)
		{
			tileMapRender.WorldScaleFactor = 1f;
			return;
		}

		if (_baseGodotScale != default)
		{
			tileMapRender.godotscale = _baseGodotScale;
		}

		tileMapRender.WorldScaleFactor = 1f;
		var tilemap = GetTilemapNode(main.instance);
		if (tilemap != null)
		{
			tilemap.Scale = Vector3.One;
		}

		_vrWorldScaleApplied = false;
	}

	public static bool TryGetXrEyeWorldPosition(out Vector3 worldPos)
	{
		if (_xrCamera != null && _xrCamera.IsInsideTree())
		{
			worldPos = _xrCamera.GlobalPosition;
			return true;
		}

		worldPos = default;
		return false;
	}

	public static Vector3 GetViewForwardWorld()
	{
		if (_xrCamera != null && _xrCamera.IsInsideTree())
		{
			return -_xrCamera.GlobalTransform.Basis.Z;
		}

		if (main.cameraPitchGimbal_world != null)
		{
			return -main.cameraPitchGimbal_world.GlobalTransform.Basis.Z;
		}

		return Vector3.Forward;
	}

	public static bool TryRaycastFromView(out Vector3 hitPos, float maxDist)
	{
		hitPos = default;
		if (_gameRoot == null)
		{
			return false;
		}

		Vector3 origin;
		Vector3 dir;
		if (_xrCamera != null && _xrCamera.IsInsideTree())
		{
			origin = _xrCamera.GlobalPosition;
			dir = -_xrCamera.GlobalTransform.Basis.Z;
		}
		else if (main.cameraPitchGimbal_world != null)
		{
			origin = main.cameraPitchGimbal_world.GlobalPosition;
			dir = -main.cameraPitchGimbal_world.GlobalTransform.Basis.Z;
		}
		else
		{
			return false;
		}

		if (dir.LengthSquared() < 0.0001f)
		{
			return false;
		}

		dir = dir.Normalized();
		return TryPhysicsRayPick(origin, dir, maxDist, out _, out _, out hitPos);
	}

	/// <summary>
	/// Sprites/NPCs scale via ArtLoader.*Scale. Remaining 3D models scale on their root node.
	/// </summary>
	public static void FinalizeObjectVisualScale(Node3D node, uwObject obj, bool renderedAsGenericSprite)
	{
		var scale = tileMapRender.WorldScaleFactor;
		if (scale <= 1f || node == null || obj == null)
		{
			return;
		}

		if (ObjectUsesSpriteMeshScale(obj, renderedAsGenericSprite))
		{
			node.Scale = Vector3.One;
			return;
		}

		if (ObjectUsesTileWidthGeometry(obj))
		{
			node.Scale = Vector3.One;
			return;
		}

		node.Scale = Vector3.One * scale;
	}

	static bool ObjectUsesSpriteMeshScale(uwObject obj, bool renderedAsGenericSprite)
	{
		if (renderedAsGenericSprite)
		{
			return true;
		}

		if (obj.majorclass == 1)
		{
			return true;
		}

		// GR sprites: runestones (majorclass 3, any minorclass) and similar.
		if (obj.majorclass == 3 || runestone.IsRunestone(obj.item_id))
		{
			return true;
		}

		// Animos (not moving doors).
		if (obj.majorclass == 7 && obj.classindex != 0xF)
		{
			return true;
		}

		// Tmaps use TileWidth-sized geometry; node scale would double-size them.
		if (obj.majorclass == 5 && obj.minorclass == 2 && (obj.classindex == 0xE || obj.classindex == 0xF))
		{
			return true;
		}

		// Doors/doorways bake TileWidth into mesh verts.
		if (obj.majorclass == 5 && obj.minorclass == 0)
		{
			return true;
		}

		return false;
	}

	static bool ObjectUsesTileWidthGeometry(uwObject obj)
	{
		if (obj.majorclass == 5 && obj.minorclass == 0)
		{
			return true;
		}

		return obj.majorclass == 5 && obj.minorclass == 2 && (obj.classindex == 0xE || obj.classindex == 0xF);
	}

	static void RescaleExistingWorldObjects(Node3D underworld)
	{
		var objList = UWTileMap.current_tilemap?.LevelObjects;
		if (objList == null)
		{
			return;
		}

		foreach (var obj in objList)
		{
			if (obj?.instance?.uwnode is not Node3D node)
			{
				continue;
			}

			RefreshSpriteVisual(obj);
			RefreshWallMountedAlignment(obj);
			var genericSprite = obj.instance is genericsprite;
			FinalizeObjectVisualScale(node, obj, genericSprite);
		}
	}

	static void RefreshWallMountedAlignment(uwObject obj)
	{
		if (obj?.instance?.uwnode is not Node3D node)
		{
			return;
		}

		var onWall = obj.xpos == 0 || obj.xpos == 7 || obj.ypos == 0 || obj.ypos == 7;
		if (!onWall)
		{
			return;
		}

		node.Position = obj.GetCoordinate();

		if (obj.majorclass == 7 && obj.classindex != 0xF)
		{
			model3D.AlignToWall(node, obj, nudgeFactor: 0.18f);
			return;
		}

		if (obj.majorclass != 5)
		{
			return;
		}

		if (obj.minorclass == 3)
		{
			model3D.AlignToWall(node, obj, nudgeFactor: 0.18f);
		}
		else if (obj.minorclass == 2)
		{
			if (obj.classindex == 6)
			{
				model3D.AlignToWall(node, obj, nudgeFactor: 0.1f);
			}
			// Tmaps: placement is set at CreateInstance; don't re-run VR wall refresh (breaks storm drains).
		}
	}

	static void RefreshSpriteVisual(uwObject obj)
	{
		if (tileMapRender.WorldScaleFactor <= 1f || obj?.instance == null)
		{
			return;
		}

		if (obj.instance is npc n)
		{
			n.SetAnimSprite(obj.npc_animation, obj.AnimationFrame, npc.CalculateFacingAngleToNPC(obj));
			return;
		}

		if (obj.instance is animo && obj.instance.uwnode?.GetChildCount() > 0)
		{
			if (obj.instance.uwnode.GetChild(0) is uwMeshInstance3D sprite && sprite.Mesh is QuadMesh quad)
			{
				var img = animo.grAnimo?.LoadImageAt(obj.owner);
				if (img != null)
				{
					var newSize = new Vector2(
						ArtLoader.SpriteScale * img.GetWidth(),
						ArtLoader.SpriteScale * img.GetHeight());
					quad.Size = newSize;
					sprite.Position = new Vector3(0, newSize.Y / 2f, 0);
				}
			}

			return;
		}

		if (obj.instance is genericsprite && obj.instance.uwnode?.GetChildCount() > 0)
		{
			RefreshSpriteQuadChild(obj.instance.uwnode.GetChild(0), obj.item_id, ObjectCreator.grObjects);
			return;
		}

		if (obj.majorclass == 3 && obj.instance.uwnode?.GetChildCount() > 0)
		{
			RefreshSpriteQuadChild(obj.instance.uwnode.GetChild(0), obj.item_id, ObjectCreator.grObjects);
			if (obj.instance.uwnode is Node3D node)
			{
				node.Scale = Vector3.One;
				node.Position = obj.GetCoordinate();
			}
		}
	}

	static void RefreshSpriteQuadChild(Node child, int spriteNo, GRLoader gr)
	{
		if (child is not uwMeshInstance3D sprite || sprite.Mesh is not QuadMesh quad || gr == null)
		{
			return;
		}

		var img = gr.LoadImageAt(spriteNo);
		if (img == null)
		{
			return;
		}

		var newSize = new Vector2(
			ArtLoader.SpriteScale * img.GetWidth(),
			ArtLoader.SpriteScale * img.GetHeight());
		quad.Size = newSize;
		sprite.Position = new Vector3(0, newSize.Y / 2f, 0);
		if (sprite.GetParent() is Node3D parent)
		{
			parent.Scale = Vector3.One;
		}
	}

	public static void FinishWorldSetup(main gameRoot)
	{
		if (!uwsettings.instance.vr)
		{
			return;
		}

		var xrInterface = XRServer.FindInterface("OpenXR");
		if (xrInterface == null || !xrInterface.IsInitialized())
		{
			GD.PushWarning("OpenXR unavailable during VR world setup.");
			uwsettings.instance.vr = false;
			return;
		}

		XRServer.PrimaryInterface = xrInterface;
		_gameRoot = gameRoot;
		_sceneTree = gameRoot.GetTree();
		_gameViewport = gameRoot.GetNode<SubViewport>("../WorldViewContainer/SubViewport");
		_gameViewport.OwnWorld3D = false;
		_mirrorYawGimbal = gameRoot.GetNodeOrNull<Node3D>("../WorldViewContainer/SubViewport/GimbalYaw");
		_mirrorRollGimbal = gameRoot.GetNodeOrNull<Node3D>("../WorldViewContainer/SubViewport/GimbalYaw/GimbalRoll");
		_mirrorPitchGimbal = gameRoot.GetNodeOrNull<Node3D>("../WorldViewContainer/SubViewport/GimbalYaw/GimbalRoll/GimbalPitchCamera");

		_worldSetupPending = true;
		_worldSetupComplete = false;
		_openXrOutputEnabled = false;
		_setupWaitFrames = 0;

		HookProcessFrame();
		VrDiagLog.Print("[VR] FinishWorldSetup queued on SceneTree.ProcessFrame.");
	}

	public static void TickRuntime(float delta, float motionBlend = 1f)
	{
		if (uwsettings.instance.vr && !uwsettings.instance.vr_mirror)
		{
			ApplyVrShortcutInput();
		}

		if (!IsActive)
		{
			return;
		}

		_motionBlend = motionBlend;

		if (_snapTurnCooldown > 0f)
		{
			_snapTurnCooldown -= delta;
		}

		if (uwsettings.instance.vr_mirror)
		{
			SyncXrOriginBodyFromGame();
			SyncMirrorHeadLook();
		}
		else
		{
			ApplySpellCastShortcutInput();
			ApplyHudMenuToggleInput();
			ApplyRecenterInput();
			RetryPendingVrHudSetup();
			if (uimanager.InGame && !uimanager.blockinput)
			{
				ApplySnapTurn();
			}
			ApplyBodyFacingFromHead();
			SyncXrOriginFromGimbal();
			ApplyNativeXrTrackingPassthrough();
			UpdateHeadRelativeMotionYaw();
			UpdateBodyMarker();
		}

		if (!uwsettings.instance.vr_debug)
		{
			return;
		}

		_debugFrameCounter++;
		if (_debugFrameCounter <= 5 || _debugFrameCounter % DebugLogIntervalFrames == 0)
		{
			LogVrRuntimeState();
		}
	}

	/// <summary>True when VR pointer/laser input should run (includes conversations and automap).</summary>
	public static bool ShouldTickVrInput()
	{
		if (!uwsettings.instance.vr || uwsettings.instance.vr_mirror)
		{
			IntroDiagLogOnce(ref _introDiagLastLaserSkipReason, "tick-vr-off",
				"ShouldTickVrInput=false (vr off or mirror mode).");
			return false;
		}

		var tick = uimanager.InGame || uimanager.InConversation || uimanager.InAutomap
			|| uimanager.AtMainMenu
			|| uimanager.CurrentGameMode == uimanager.GameModes.CUTSCENE
			|| uimanager.CurrentGameMode == uimanager.GameModes.OPTIONS
			|| IsHudOnMenuScreen();
		if (!tick)
		{
			IntroDiagLogOnce(ref _introDiagLastLaserSkipReason, "tick-vr-mode-skip",
				$"ShouldTickVrInput=false (mode={uimanager.CurrentGameMode} active={IsActive} menuTv={IsHudOnMenuScreen()}).");
		}

		return tick;
	}

	/// <summary>VR pointer/attack input — runs in _Process so combat reads grip on the same frame.</summary>
	public static void TickVrInput()
	{
		if (!uwsettings.instance.vr || uwsettings.instance.vr_mirror)
		{
			return;
		}

		if (NeedsFrontMenuLaser() || uimanager.AtMainMenu)
		{
			LogIntroDiagSnapshot("TickVrInput");
		}

		if (VrNumberPad.IsVisible)
		{
			ApplyNumberPadPointerInput();
		}
		else
		{
			if (IsActive && uimanager.InGame)
			{
				ApplyStatusOverlayPointerInput();
			}

			ApplyHudPointerInput();
		}

		if (IsActive)
		{
			ApplyCombatModeToggleInput();
			VrCombatMotion.Tick();
			VrCombatMotionDebug.Update(VrCombatMotion.ShouldShowGesturePlanes(), VrCombatMotion.GetDebugWeaponHandLocal());
			if (!VrNumberPad.IsVisible)
			{
				if (SpellCasting.currentSpell != null)
				{
					ApplySpellTargetingInput();
				}
				else
				{
					ApplyExplorationVerbInput();
				}
			}
			else
			{
				IsVrWorldPointerActive = false;
				IsVrWorldRightHeld = false;
				ResetExplorationVerbPressState();
				ResetSpellTargetingPressState();
			}

			UpdateHeldObjectVisual();
			UpdateMessageScrollPanel();
			UpdateVrStatusPanels();
		}

		UpdateVrGameplayPointerLaser();
		if (VrNumberPad.IsVisible && NeedsFrontMenuLaser())
		{
			EnsureFrontMenuLaserDrawn();
		}
	}

	static XRController3D GetMenuPointerController() => _rightController ?? _leftController;

	/// <summary>Intro/menu TV keeps the right-hand pointer; in-game HUD uses the aim hand.</summary>
	static void GetHudPointerRay(bool menuScreen, out Vector3 rayOrigin, out Vector3 rayDir)
	{
		if (menuScreen)
		{
			rayOrigin = GetMenuPointerRayOrigin();
			rayDir = GetMenuPointerRayDir();
			return;
		}

		rayOrigin = GetAimRayOrigin();
		rayDir = GetControllerRayDir();
	}

	/// <summary>
	/// Menu TV: trigger only. In-game HUD: dominant trigger only.
	/// Dominant grip is Get (press pick / release place) — not a generic HUD left click.
	/// </summary>
	static bool IsHudPointerLeftClickHeld(bool menuScreen)
	{
		if (menuScreen)
		{
			var menuPointer = GetMenuPointerController();
			return menuPointer != null && IsButtonPressed(menuPointer, HudLeftClickActions);
		}

		var dominant = GetDominantController();
		return dominant != null && IsButtonPressed(dominant, HudLeftClickActions);
	}

	/// <summary>Menu TV: grip on menu pointer. In-game HUD: off-hand grip (Talk hand).</summary>
	static bool IsHudPointerRightClickHeld(bool menuScreen)
	{
		if (menuScreen)
		{
			var menuPointer = GetMenuPointerController();
			return menuPointer != null && IsButtonPressed(menuPointer, HudRightClickActions);
		}

		var offHand = GetOffHandController();
		return offHand != null && IsButtonPressed(offHand, HudRightClickActions);
	}

	static bool IsMenuControllerTrackingReady(XRController3D controller)
	{
		if (controller == null)
		{
			return false;
		}

		// OpenXR updates GlobalTransform before local Position is meaningful on some runtimes.
		if (controller.GlobalTransform.Origin.LengthSquared() > 0.0004f)
		{
			return true;
		}

		return controller.Position.LengthSquared() > 0.0004f;
	}

	static bool UseHeadRayForMenuPointer()
	{
		if (!IsHudOnMenuScreen() || _xrCamera == null)
		{
			return false;
		}

		return !IsMenuControllerTrackingReady(GetMenuPointerController());
	}

	static Vector3 GetMenuPointerRayOrigin()
	{
		var controller = GetMenuPointerController();
		if (IsHudOnMenuScreen() && IsMenuControllerTrackingReady(controller))
		{
			return controller.GlobalPosition;
		}

		if (UseHeadRayForMenuPointer())
		{
			var basis = _xrCamera.GlobalTransform.Basis;
			return _xrCamera.GlobalPosition + basis * new Vector3(0.08f, -0.08f, 0f);
		}

		return controller?.GlobalPosition ?? GetAimRayOrigin();
	}

	static Vector3 GetRayDirFromController(XRController3D controller)
	{
		if (controller == null)
		{
			return Vector3.Forward;
		}

		var rayDir = -controller.GlobalTransform.Basis.Z;
		if (rayDir.LengthSquared() < 0.0001f)
		{
			rayDir = -controller.GlobalTransform.Basis.Y;
		}

		return rayDir.Normalized();
	}

	static Vector3 GetMenuPointerRayDir()
	{
		var controller = GetMenuPointerController();
		if (IsHudOnMenuScreen() && IsMenuControllerTrackingReady(controller))
		{
			return GetRayDirFromController(controller);
		}

		if (UseHeadRayForMenuPointer())
		{
			var forward = -_xrCamera.GlobalTransform.Basis.Z;
			return forward.LengthSquared() > 0.0001f ? forward.Normalized() : Vector3.Forward;
		}

		return GetRayDirFromController(controller);
	}

	/// <summary>Guarantee a visible laser on intro/menu screens (ApplyHudPointerInput can bail early).</summary>
	static void EnsureFrontMenuLaserDrawn()
	{
		if (!uwsettings.instance.vr)
		{
			IntroDiagLogOnce(ref _introDiagLastLaserSkipReason, "menu-laser-vr-off", "menu laser: vr disabled.");
			return;
		}

		if (uwsettings.instance.vr_mirror)
		{
			IntroDiagLogOnce(ref _introDiagLastLaserSkipReason, "menu-laser-mirror", "menu laser: mirror mode.");
			return;
		}

		if (!NeedsFrontMenuLaser())
		{
			IntroDiagLogOnce(ref _introDiagLastLaserSkipReason, "menu-laser-not-needed",
				$"menu laser: NeedsFrontMenuLaser=false (mode={uimanager.CurrentGameMode} atMain={uimanager.AtMainMenu} menuTv={IsHudOnMenuScreen()}).");
			return;
		}

		DrawMenuTvLaserOnly();
	}

	static bool NeedsFrontMenuLaser() =>
		IsHudOnMenuScreen()
		|| uimanager.AtMainMenu
		|| uimanager.CurrentGameMode == uimanager.GameModes.CUTSCENE
		|| uimanager.CurrentGameMode == uimanager.GameModes.OPTIONS;

	static void HookProcessFrame()
	{
		if (_processFrameHooked || _sceneTree == null)
		{
			return;
		}

		_sceneTree.ProcessFrame += OnProcessFrame;
		_processFrameHooked = true;
		VrDiagLog.Print("[VR] Hooked SceneTree.ProcessFrame.");
	}

	static void UnhookProcessFrame()
	{
		if (!_processFrameHooked || _sceneTree == null)
		{
			return;
		}

		_sceneTree.ProcessFrame -= OnProcessFrame;
		_processFrameHooked = false;
	}

	static void OnProcessFrame()
	{
		if (!uwsettings.instance.vr)
		{
			UnhookProcessFrame();
			return;
		}

		if (_worldSetupPending && !_worldSetupComplete)
		{
			TickWorldSetup();
		}
	}

	static void TickWorldSetup()
	{
		_setupWaitFrames++;

		if (_gameRoot?.IsInsideTree() != true)
		{
			if (_setupWaitFrames <= 5)
			{
				VrDiagLog.Print("[VR] TickWorldSetup: waiting for gameRoot in tree.");
			}

			return;
		}

		var underworld = _gameRoot.GetParent<Node3D>();
		if (underworld?.IsInsideTree() != true)
		{
			if (_setupWaitFrames <= 5)
			{
				VrDiagLog.Print("[VR] TickWorldSetup: waiting for Underworld in tree.");
			}

			return;
		}

		if (_xrOrigin == null)
		{
			VrDiagLog.Print($"[VR] TickWorldSetup frame={_setupWaitFrames}: creating XR rig.");
			CreateXrRig(underworld);
		}

		if (_xrCamera?.IsInsideTree() != true)
		{
			if (_setupWaitFrames <= 10)
			{
				VrDiagLog.Print($"[VR] TickWorldSetup frame={_setupWaitFrames}: XRCamera not in tree yet (origin={_xrOrigin?.IsInsideTree()}).");
			}

			return;
		}

		if (!_worldSetupComplete)
		{
			FinishActivation(underworld);
		}
	}

	static void CreateXrRig(Node3D underworld)
	{
		if (main.cameraYawGimbal_world == null)
		{
			GD.PushError("[VR] CreateXrRig: yaw gimbal missing.");
			_worldSetupPending = false;
			return;
		}

		VrDebugLog("CreateXrRig", $"GimbalYaw inTree={main.cameraYawGimbal_world.IsInsideTree()} path={main.cameraYawGimbal_world.GetPath()}");

		_xrOrigin = new XROrigin3D { Name = "XROrigin" };
		underworld.AddChild(_xrOrigin);

		_xrCamera = new XRCamera3D
		{
			Name = "XRCamera",
			Current = false,
			Near = 0.05f,
			Far = 300f,
			Fov = Math.Max(50, uwsettings.instance.FOV),
			// Match sprite/world composite layers (not ObjectInfo). Avoids ObjectInfo/default shader paths.
			CullMask = main.LayerGeo | main.LayerXFER,
		};
		_xrOrigin.AddChild(_xrCamera);

		_leftController = CreateController(_xrOrigin, "LeftController", "left_hand");
		_rightController = CreateController(_xrOrigin, "RightController", "right_hand");

		if (uwsettings.instance.vr_mirror)
		{
			SetupMirrorScreen();
		}
		else
		{
			SetupNativeWorldCamera();
			EnsureWorldEnvironment(underworld);
		}

		VrDiagLog.Print($"[VR] CreateXrRig: XROrigin inTree={_xrOrigin.IsInsideTree()} path={_xrOrigin.GetPath()}");
		VrDiagLog.Print($"[VR] CreateXrRig: XRCamera inTree={_xrCamera.IsInsideTree()} path={_xrCamera.GetPath()}");
	}

	static void FinishActivation(Node3D underworld)
	{
		_worldSetupComplete = true;
		_worldSetupPending = false;
		IsActive = true;

		ConfigureFlatScreenPresentation(_gameRoot);
		RescaleExistingWorldObjects(underworld);
		EnsureBodyMarker(underworld);
		if (!uwsettings.instance.vr_mirror)
		{
			EnsurePointerLaser(underworld);
			var ui = underworld.GetNodeOrNull<CanvasLayer>("UI");
			if (UsesFrontMenuBoot() && !uimanager.InGame)
			{
				SetupMenuTvScreen(underworld);
			}
			else
			{
				if (uwsettings.instance.vr_hud_panel)
				{
					SetupHudHandPanel(underworld);
				}

				EnsureMessageScrollPanel(underworld);
				EnsureVrStatusPanels(underworld);
			}
		}
		try
		{
			playerdat.RefreshLighting();
			ResetXrOriginFloorTracking();
			InitializeMotionStep();
			if (uimanager.InGame)
			{
				playerdat.PositionPlayerCamera();
				SnapRoomOriginToAvatar();
				uimanager.UpdateInventoryDisplay();
			}
			else
			{
				SyncXrOriginFromGimbal();
			}
		}
		catch (Exception ex)
		{
			VrDiagLog.Warn($"[VR] FinishActivation post-setup failed: {ex}");
		}
		finally
		{
			UpdateBodyMarker();
			TryEnableOpenXrOutput();
			LogVrSetupState(_gameRoot, _sceneTree.Root.GetViewport(), "FinishActivation");
			LogIntroDiagSnapshot("FinishActivation", force: true);
			if (IsHudOnMenuScreen())
			{
				ApplyHudPointerInput();
			}

			VrDiagLog.Print($"[VR] Active — display mode: {(uwsettings.instance.vr_mirror ? "mirror (SubViewport screen)" : "native world")}");
			VrDiagLog.Print($"[VR] Head tracking: passthrough (OpenXR local pose, origin at game floor)");
			if (_vrWorldScaleApplied)
			{
				VrDiagLog.Print($"[VR] World scale: {uwsettings.instance.vr_world_scale}x");
			}
		}
	}

	static void TryEnableOpenXrOutput()
	{
		if (_openXrOutputEnabled || _xrCamera?.IsInsideTree() != true || _sceneTree == null)
		{
			return;
		}

		_xrCamera.Current = true;

		var rootViewport = _sceneTree.Root.GetViewport();
		rootViewport.VrsMode = Viewport.VrsModeEnum.XR;
		rootViewport.UseXR = true;

		_openXrOutputEnabled = true;
		UpdateXrViewportHdrForUiMode();
		VrDiagLog.Print($"[VR] OpenXR output enabled. UseXR={rootViewport.UseXR} UseHdr2D={rootViewport.UseHdr2D} menuTv={_vrUiOnMenuTv} XRCamera path={_xrCamera.GetPath()} Current={_xrCamera.Current}");
	}

	static void SetupNativeWorldCamera()
	{
		if (main.cameraPitchGimbal_world != null)
		{
			main.cameraPitchGimbal_world.Current = false;
		}

		_gameViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		main.instance.cam_world = _xrCamera;
		main.cameraPitchGimbal_world = _xrCamera;
		// Bypass multi-viewport palette composition; resolve colors in spatial shaders.
		RenderingServer.GlobalShaderParameterSet("final_color_pass", true);
		shade.UpdateShaderShadeUniforms(playerdat.lightlevel);
		PaletteLoader.UpdateSmoothPaletteForLighting();
		SyncXrOriginFromGimbal();
	}

	static void SetupMirrorScreen()
	{
		if (main.cameraPitchGimbal_world != null)
		{
			main.cameraPitchGimbal_world.Current = true;
		}

		_gameViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;

		var viewportTexture = _gameViewport.GetTexture();
		var aspect = 9f / 16f;
		if (_gameViewport.Size.Y > 0)
		{
			aspect = (float)_gameViewport.Size.Y / _gameViewport.Size.X;
		}

		var screenMaterial = new StandardMaterial3D
		{
			AlbedoTexture = viewportTexture,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
		};

		var quad = new MeshInstance3D
		{
			Name = "VrMirrorScreen",
			Mesh = new QuadMesh
			{
				Size = new Vector2(MirrorScreenWidthMeters, MirrorScreenWidthMeters * aspect),
				Material = screenMaterial,
			},
			Position = new Vector3(0f, 0f, -MirrorScreenDistanceMeters),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		_xrCamera.AddChild(quad);

		VrDiagLog.Print($"[VR] Mirror screen attached ({MirrorScreenWidthMeters:F1}m wide).");
	}

	static void ConfigureFlatScreenPresentation(main gameRoot)
	{
		var worldView = gameRoot.GetNodeOrNull<Control>("../WorldViewContainer");
		if (worldView != null)
		{
			worldView.Visible = uwsettings.instance.vr_mirror && uwsettings.instance.vr_debug;
		}
	}

	/// <summary>
	/// Render the existing 1280×800 CanvasLayer HUD into a SubViewport and show it
	/// on a quad attached to the left controller (ViveCraft-style).
	/// </summary>
	static bool UsesFrontMenuBoot() =>
		uwsettings.instance.vr && uwsettings.instance.VrBootFull;

	static CanvasLayer GetVrUiCanvasLayer(Node3D underworld) =>
		underworld?.GetNodeOrNull<CanvasLayer>("UI")
		?? _hudViewport?.GetNodeOrNull<CanvasLayer>("UI");

	static bool EnsureVrUiViewport(Node3D underworld, CanvasLayer ui)
	{
		if (_hudViewport != null)
		{
			EnsureHudMouseLayer();
			return true;
		}

		_hudViewport = new SubViewport
		{
			Name = "VrHudViewport",
			Size = new Vector2I(HudPanelWidthPx, HudPanelHeightPx),
			TransparentBg = false,
			Disable3D = true,
			HandleInputLocally = true,
			GuiDisableInput = false,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			Msaa2D = Viewport.Msaa.Disabled,
		};
		underworld.AddChild(_hudViewport);
		_hudViewport.CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest;

		ui.FollowViewportEnabled = false;
		ui.GetParent()?.RemoveChild(ui);
		_hudViewport.AddChild(ui);

		_hudMouseLayer = ui.GetNodeOrNull<CanvasLayer>("mouse");
		if (_hudMouseLayer != null)
		{
			_hudMouseLayer.Visible = false;
		}

		return true;
	}

	static StandardMaterial3D CreateVrUiQuadMaterial()
	{
		return new StandardMaterial3D
		{
			AlbedoTexture = _hudViewport.GetTexture(),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DisableReceiveShadows = true,
		};
	}

	static MeshInstance3D CreateHandHudMesh(Vector2 quadSize)
	{
		return new MeshInstance3D
		{
			Name = "VrHudPanel",
			Mesh = new QuadMesh
			{
				Size = quadSize,
				Material = CreateVrUiQuadMaterial(),
			},
			Position = GetHudPanelLocalPosition(),
			RotationDegrees = GetHudPanelLocalRotationDegrees(),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Layers = main.LayerGeo | main.LayerXFER,
		};
	}

	static void AttachHandHudMesh(Vector2 quadSize)
	{
		var hudHand = GetHudHandController();
		if (hudHand == null)
		{
			return;
		}

		_hudPanel = CreateHandHudMesh(quadSize);
		hudHand.AddChild(_hudPanel);
	}

	static void ResizeVrHudDisplay(Vector2 size)
	{
		if (_hudPanel?.Mesh is QuadMesh quad)
		{
			quad.Size = size;
		}
	}

	static void ApplyMenuTvMaterialBrightness(StandardMaterial3D mat)
	{
		if (mat == null)
		{
			return;
		}

		var brightness = Mathf.Max(1f, uwsettings.instance.vr_menu_screen_brightness);
		mat.AlbedoColor = new Color(brightness, brightness, brightness);
		mat.EmissionEnabled = true;
		mat.Emission = new Color(0.15f * brightness, 0.15f * brightness, 0.18f * brightness);
		mat.EmissionEnergyMultiplier = 0.65f;
	}

	static Rect2 GetMessageScrollHudRectFixed()
	{
		return UWClass._RES == UWClass.GAME_UW2
			? new Rect2(48f, 656f, 864f, 140f)
			: new Rect2(44f, 656f, 1196f, 140f);
	}

	static RichTextLabel FindScrollLabelInTree(Node node)
	{
		if (node is RichTextLabel label)
		{
			return label;
		}

		foreach (var child in node.GetChildren())
		{
			var found = FindScrollLabelInTree(child);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	static void RegisterVrMessageScrollOutput(RichTextLabel label)
	{
		var scroll = uimanager.instance?.scroll;
		if (label == null || scroll == null)
		{
			return;
		}

		var existing = scroll.OutputControl;
		if (existing != null)
		{
			foreach (var ctl in existing)
			{
				if (ctl == label)
				{
					return;
				}
			}
		}

		var length = existing?.Length ?? 0;
		var updated = new RichTextLabel[length + 1];
		if (existing != null)
		{
			Array.Copy(existing, updated, length);
		}

		updated[length] = label;
		scroll.OutputControl = updated;
	}

	static bool EnsureMessageScrollViewport(Node3D underworld)
	{
		if (_messageScrollViewport != null && GodotObject.IsInstanceValid(_messageScrollViewport))
		{
			return true;
		}

		var scroll = uimanager.MessageScroll;
		var sourcePanel = scroll?.GetParent();
		if (sourcePanel == null)
		{
			return false;
		}

		var hudRect = GetMessageScrollHudRectFixed();
		_messageScrollViewport = new SubViewport
		{
			Name = "VrMessageScrollViewport",
			Size = new Vector2I(Mathf.CeilToInt(hudRect.Size.X), Mathf.CeilToInt(hudRect.Size.Y)),
			TransparentBg = true,
			Disable3D = true,
			HandleInputLocally = false,
			GuiDisableInput = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
			Msaa2D = Viewport.Msaa.Disabled,
		};
		underworld.AddChild(_messageScrollViewport);
		_messageScrollViewport.CanvasItemDefaultTextureFilter = Viewport.DefaultCanvasItemTextureFilter.Nearest;

		var duplicate = sourcePanel.Duplicate() as Node;
		if (duplicate == null)
		{
			_messageScrollViewport.QueueFree();
			_messageScrollViewport = null;
			return false;
		}

		_messageScrollViewport.AddChild(duplicate);
		if (duplicate is Control dupRoot)
		{
			dupRoot.Position = -hudRect.Position;
		}

		RegisterVrMessageScrollOutput(FindScrollLabelInTree(duplicate));
		uimanager.instance?.scroll?.UpdateMessageDisplay();
		VrDiagLog.Print($"[VR] Message scroll viewport ready ({_messageScrollViewport.Size.X}x{_messageScrollViewport.Size.Y}).");
		return true;
	}

	static void EnsureMessageScrollPanel(Node3D underworld = null)
	{
		if (!uwsettings.instance.vr_status_panels || _xrCamera == null)
		{
			return;
		}

		underworld ??= _gameRoot?.GetParent<Node3D>();
		if (underworld == null || !EnsureMessageScrollViewport(underworld))
		{
			return;
		}

		if (_messageScrollPanel != null && GodotObject.IsInstanceValid(_messageScrollPanel))
		{
			return;
		}

		var hudRect = GetMessageScrollHudRectFixed();
		_messageScrollMaterial = new StandardMaterial3D
		{
			AlbedoTexture = _messageScrollViewport.GetTexture(),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DisableReceiveShadows = true,
		};

		var quadSize = HudRectToQuadSize(hudRect);
		_messageScrollPanel = new MeshInstance3D
		{
			Name = "VrMessageScrollPanel",
			Mesh = new QuadMesh
			{
				Size = quadSize,
				Material = _messageScrollMaterial,
			},
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Layers = main.LayerGeo | main.LayerXFER,
			Visible = false,
		};
		_xrCamera.AddChild(_messageScrollPanel);
		VrDiagLog.Print($"[VR] Message scroll panel attached to headset ({quadSize.X:F2}m wide).");
	}

	static Vector2 GetMessageScrollOffsetMeters() => new(
		uwsettings.instance.vr_message_scroll_offset_x,
		uwsettings.instance.vr_message_scroll_offset_y);

	static bool ShouldShowMessageScrollPanel()
	{
		return ShouldShowHeadOverlays()
			&& _messageScrollViewport != null;
	}

	static bool HasMessageScrollContent()
	{
		var scroll = uimanager.MessageScroll;
		return scroll != null && !string.IsNullOrWhiteSpace(scroll.Text);
	}

	static bool ShouldHoldMessageScrollOpen()
	{
		return MessageDisplay.WaitingForTypedInput
			|| MessageDisplay.WaitingForYesOrNo
			|| MessageDisplay.WaitingForMore
			|| uimanager.MessageScrollIsTemporary
			|| (uimanager.InConversation && ConversationVM.WaitingForInput);
	}

	static float MessageScrollNowSeconds() => HeadOverlayNowSeconds();

	/// <summary>Show a head-locked number pad for stack quantity prompts.</summary>
	public static void ShowQuantityNumberPad(int maxQuantity)
	{
		if (!IsActive || uwsettings.instance.vr_mirror || !uimanager.InGame || maxQuantity < 1)
		{
			return;
		}

		var underworld = _gameRoot?.GetParent<Node3D>();
		if (underworld == null || _xrCamera == null)
		{
			return;
		}

		VrNumberPad.Show(underworld, _xrCamera, maxQuantity);
		_numberPadLeftWasPressed = false;
		_lastNumberPadPointerPos = new Vector2(-1f, -1f);
		NotifyMessageScrollUpdated();
	}

	public static void HideQuantityNumberPad()
	{
		VrNumberPad.Hide();
	}

	/// <summary>Call when bottom message scroll text changes (new line printed).</summary>
	public static void NotifyMessageScrollUpdated()
	{
		if (!uwsettings.instance.vr_status_panels || !IsActive || uwsettings.instance.vr_mirror)
		{
			return;
		}

		if (!HasMessageScrollContent())
		{
			_messageScrollHideAfterTime = -1f;
			_messageScrollAlpha = 0f;
			return;
		}

		if (HeadOverlaysAlwaysVisible() || ShouldHoldMessageScrollOpen())
		{
			_messageScrollHideAfterTime = -1f;
			_messageScrollAlpha = 1f;
			return;
		}

		_messageScrollHideAfterTime = MessageScrollNowSeconds() + GetHeadOverlayDisplaySeconds();
		_messageScrollAlpha = 1f;
	}

	static void UpdateMessageScrollFade()
	{
		if (HeadOverlaysAlwaysVisible())
		{
			_messageScrollHideAfterTime = -1f;
			_messageScrollAlpha = HasMessageScrollContent() ? 1f : 0f;
			return;
		}

		var holdOpen = ShouldHoldMessageScrollOpen();
		if (holdOpen)
		{
			_messageScrollHideAfterTime = -1f;
			_messageScrollAlpha = 1f;
			_messageScrollHoldWasActive = true;
			return;
		}

		if (_messageScrollHoldWasActive)
		{
			_messageScrollHoldWasActive = false;
			_messageScrollHideAfterTime = MessageScrollNowSeconds() + GetHeadOverlayDisplaySeconds();
			_messageScrollAlpha = 1f;
			return;
		}

		if (_messageScrollHideAfterTime < 0f)
		{
			return;
		}

		var now = MessageScrollNowSeconds();
		if (now < _messageScrollHideAfterTime)
		{
			_messageScrollAlpha = 1f;
			return;
		}

		var fadeT = (now - _messageScrollHideAfterTime) / GetHeadOverlayFadeSeconds();
		_messageScrollAlpha = Mathf.Clamp(1f - fadeT, 0f, 1f);
		if (_messageScrollAlpha <= 0f)
		{
			_messageScrollHideAfterTime = -1f;
		}
	}

	static void ApplyMessageScrollMaterialAlpha()
	{
		if (_messageScrollMaterial == null)
		{
			return;
		}

		_messageScrollMaterial.AlbedoColor = new Color(1f, 1f, 1f, _messageScrollAlpha);
		_messageScrollMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
	}

	static void UpdateMessageScrollPanel()
	{
		if (_messageScrollPanel == null || !GodotObject.IsInstanceValid(_messageScrollPanel))
		{
			return;
		}

		if (!ShouldShowMessageScrollPanel() || !HasMessageScrollContent())
		{
			_messageScrollPanel.Visible = false;
			return;
		}

		if (HeadOverlaysAlwaysVisible())
		{
			_messageScrollAlpha = 1f;
		}
		else
		{
			UpdateMessageScrollFade();
			if (_messageScrollAlpha <= 0.001f)
			{
				_messageScrollPanel.Visible = false;
				return;
			}
		}

		var hudRect = GetMessageScrollHudRectFixed();
		if (_messageScrollPanel.Mesh is QuadMesh quad)
		{
			quad.Size = HudRectToQuadSize(hudRect);
		}

		_messageScrollPanel.Position = HudRectCenterToCameraLocal(hudRect, GetMessageScrollOffsetMeters());
		_messageScrollPanel.RotationDegrees = Vector3.Zero;
		ApplyMessageScrollMaterialAlpha();
		_messageScrollPanel.Visible = true;
	}

	static void UpdateXrViewportHdrForUiMode()
	{
		if (_sceneTree?.Root?.GetViewport() is not Viewport rootVp || !rootVp.UseXR)
		{
			return;
		}

		// 2D UI-on-quad needs hdr_2d in XR during menu TV; native dungeon pass keeps it off (lighting fix).
		rootVp.UseHdr2D = _vrUiOnMenuTv;
	}

	static void RefreshVrUiQuadMaterial()
	{
		if (_hudViewport == null || _hudPanel?.Mesh is not QuadMesh quad)
		{
			return;
		}

		if (quad.Material is StandardMaterial3D mat)
		{
			mat.AlbedoTexture = _hudViewport.GetTexture();
		}
	}

	/// <summary>Intro, main menu, and chargen on a large screen in front of the player.</summary>
	static void SetupMenuTvScreen(Node3D underworld)
	{
		if (_xrCamera == null)
		{
			VrDiagLog.Warn("[VR] Menu TV: XRCamera missing.");
			return;
		}

		var ui = GetVrUiCanvasLayer(underworld);
		if (ui == null)
		{
			VrDiagLog.Warn("[VR] Menu TV: UI CanvasLayer not found.");
			return;
		}

		if (!EnsureVrUiViewport(underworld, ui))
		{
			return;
		}

		var width = uwsettings.instance.vr_menu_screen_width;
		if (width <= 0.5f)
		{
			width = 2.2f;
		}

		var aspect = (float)HudPanelHeightPx / HudPanelWidthPx;
		if (_hudPanel == null)
		{
			_hudPanel = new MeshInstance3D
			{
				Name = "VrMenuTvScreen",
				Mesh = new QuadMesh
				{
					Size = new Vector2(width, width * aspect),
					Material = CreateVrUiQuadMaterial(),
				},
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
				Layers = main.LayerGeo | main.LayerXFER,
			};
			if (_hudPanel.Mesh is QuadMesh newQuad && newQuad.Material is StandardMaterial3D menuMat)
			{
				ApplyMenuTvMaterialBrightness(menuMat);
			}
		}
		else
		{
			_hudPanel.GetParent()?.RemoveChild(_hudPanel);
			ResizeVrHudDisplay(new Vector2(width, width * aspect));
			if (_hudPanel.Mesh is QuadMesh quad)
			{
				quad.Material = CreateVrUiQuadMaterial();
				if (quad.Material is StandardMaterial3D menuMat)
				{
					ApplyMenuTvMaterialBrightness(menuMat);
				}
			}
		}

		// Head-locked cinema screen (same attachment pattern as VrMirrorScreen).
		_hudPanel.Position = MenuTvCameraLocalPosition;
		_hudPanel.RotationDegrees = Vector3.Zero;
		_xrCamera.AddChild(_hudPanel);
		_vrUiOnMenuTv = true;
		_hudPanelVisible = true;
		_hudPanel.Visible = true;
		UpdateXrViewportHdrForUiMode();
		_hudViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		if (_sceneTree != null)
		{
			_sceneTree.ProcessFrame += OnMenuTvFirstFrame;
		}

		VrDiagLog.Print($"[VR] Menu TV screen attached to XRCamera ({width:F2}m wide, {HudPanelWidthPx}x{HudPanelHeightPx}).");
	}

	static void OnMenuTvFirstFrame()
	{
		if (_sceneTree != null)
		{
			_sceneTree.ProcessFrame -= OnMenuTvFirstFrame;
		}

		RefreshVrUiQuadMaterial();
		if (!_openXrOutputEnabled)
		{
			TryEnableOpenXrOutput();
		}
	}

	static void TransitionMenuTvToHandHud()
	{
		if (_hudPanel == null)
		{
			_vrUiOnMenuTv = false;
			UpdateXrViewportHdrForUiMode();
			return;
		}

		var hudHand = GetHudHandController();
		if (hudHand == null)
		{
			VrDiagLog.Warn("[VR] Menu TV → hand HUD deferred: off-hand controller not ready.");
			return;
		}

		var width = uwsettings.instance.vr_hud_panel_width;
		if (width <= 0.05f)
		{
			width = 0.42f;
		}

		var aspect = (float)HudPanelHeightPx / HudPanelWidthPx;
		var quadSize = new Vector2(width, width * aspect);
		_hudPanel.GetParent()?.RemoveChild(_hudPanel);
		_hudPanel.QueueFree();
		AttachHandHudMesh(quadSize);

		_vrUiOnMenuTv = false;
		UpdateXrViewportHdrForUiMode();
		RefreshVrUiQuadMaterial();
		SetHudPanelVisible(true);
		EnsureMessageScrollPanel(_gameRoot?.GetParent<Node3D>());
		EnsureVrStatusPanels(_gameRoot?.GetParent<Node3D>());
		VrDiagLog.Print($"[VR] Menu TV → hand HUD ({width:F2}m wide).");
	}

	static bool IsHudOnMenuScreen()
	{
		if (_hudPanel == null)
		{
			return _vrUiOnMenuTv;
		}

		var parent = _hudPanel.GetParent();
		return _vrUiOnMenuTv || parent == _xrCamera;
	}

	static void EnsureVrGameplayHud()
	{
		if (uwsettings.instance.vr_mirror)
		{
			return;
		}

		var underworld = _gameRoot?.GetParent<Node3D>();
		if (underworld == null)
		{
			return;
		}

		if (uwsettings.instance.vr_hud_panel)
		{
			if (_hudPanel == null)
			{
				SetupHudHandPanel(underworld);
			}
			else if (IsHudOnMenuScreen())
			{
				TransitionMenuTvToHandHud();
			}
		}

		EnsureMessageScrollPanel(underworld);
		EnsureVrStatusPanels(underworld);
	}

	static void RefreshVrWorldPresentation()
	{
		if (uwsettings.instance.vr_mirror || _xrCamera == null)
		{
			return;
		}

		_xrCamera.Current = true;
		RenderingServer.GlobalShaderParameterSet("final_color_pass", true);
		shade.UpdateShaderShadeUniforms(playerdat.lightlevel);
		PaletteLoader.UpdateSmoothPaletteForLighting();
		UpdateXrViewportHdrForUiMode();
		EnsureVrTilemapScale(_gameRoot);

		if (main.instance?.lblPositionDebug != null)
		{
			var showDebug = uwsettings.instance.vr_light_debug;
			uimanager.EnableDisable(main.instance.lblPositionDebug, showDebug);
		}
	}

	/// <summary>Call when a full-boot VR session enters gameplay (after JourneyOnwards).</summary>
	public static void OnEnteringVrGameplay()
	{
		if (!uwsettings.instance.vr || uwsettings.instance.vr_mirror)
		{
			return;
		}

		_vrGameplayEnterPending = true;
		if (_sceneTree != null)
		{
			_sceneTree.ProcessFrame += FinishEnteringVrGameplayDeferred;
		}
		else
		{
			FinishEnteringVrGameplay();
		}
	}

	static void FinishEnteringVrGameplayDeferred()
	{
		if (_sceneTree != null)
		{
			_sceneTree.ProcessFrame -= FinishEnteringVrGameplayDeferred;
		}

		FinishEnteringVrGameplay();
	}

	static void FinishEnteringVrGameplay()
	{
		_vrGameplayEnterPending = false;
		EnsureVrGameplayHud();
		RefreshVrWorldPresentation();
		playerdat.RefreshLighting();
		ResetXrOriginFloorTracking();
		playerdat.PositionPlayerCamera();
		InitializeMotionStep();
		SnapRoomOriginToAvatar();
		uimanager.UpdateInventoryDisplay();
		TryEnableOpenXrOutput();
		VrDiagLog.Print("[VR] Gameplay presentation ready (hand HUD, world lighting, origin snapped).");
	}

	static void ApplyVrShortcutInput()
	{
		if (!uwsettings.instance.vr || uwsettings.instance.vr_mirror)
		{
			_vrShortcutTriggerWasPressed = false;
			_vrEscapeWasPressed = false;
			return;
		}

		if (_rightController != null)
		{
			var advance = IsButtonPressed(_rightController, HudLeftClickActions);
			if (advance && !_vrShortcutTriggerWasPressed)
			{
				if (MessageDisplay.WaitingForMore)
				{
					MessageDisplay.WaitingForMore = false;
				}
			}

			_vrShortcutTriggerWasPressed = advance;
		}
		else
		{
			_vrShortcutTriggerWasPressed = false;
		}

		if (_leftController != null)
		{
			var escape = IsButtonPressed(_leftController, DoorUseButtonActions);
			if (escape && !_vrEscapeWasPressed && ShouldOfferVrEscapeGrip())
			{
				ApplyVrEscapeAction();
			}

			_vrEscapeWasPressed = escape;
		}
		else
		{
			_vrEscapeWasPressed = false;
		}
	}

	static void ApplyVrEscapeAction()
	{
		if (MessageDisplay.WaitingForYesOrNo)
		{
			MessageDisplay.ConfirmYesNoResponse(false);
			VrDiagLog.Print("[VR] Yes/no declined (left grip = Escape).");
			return;
		}

		if (MessageDisplay.WaitingForTypedInput)
		{
			MessageDisplay.CancelTypedInput();
			VrOnScreenKeyboard.Hide();
			VrNumberPad.Hide();
			VrDiagLog.Print("[VR] Typed input cancelled (left grip = Escape).");
			return;
		}

		switch (uimanager.CurrentGameMode)
		{
			case uimanager.GameModes.CUTSCENE:
				cutsplayer.StopCutscene();
				VrDiagLog.Print("[VR] Cutscene skip (left grip = Escape).");
				break;
			case uimanager.GameModes.MAIN:
			case uimanager.GameModes.CHARGEN:
			case uimanager.GameModes.JOURNEY:
				uimanager.instance?.HandleFrontMenuEscape();
				VrDiagLog.Print("[VR] Front menu back (left grip = Escape).");
				break;
			case uimanager.GameModes.GAME:
				if (cutsplayer.IsPlaying)
				{
					cutsplayer.StopCutscene();
				}

				break;
		}
	}

	static void SetupHudHandPanel(Node3D underworld)
	{
		if (!uwsettings.instance.vr_hud_panel || GetHudHandController() == null)
		{
			return;
		}

		if (_vrUiOnMenuTv)
		{
			TransitionMenuTvToHandHud();
			return;
		}

		if (_hudPanel != null)
		{
			return;
		}

		var ui = GetVrUiCanvasLayer(underworld);
		if (_hudViewport == null)
		{
			if (ui == null)
			{
				VrDiagLog.Warn("[VR] HUD panel: UI CanvasLayer not found.");
				return;
			}

			if (!EnsureVrUiViewport(underworld, ui))
			{
				return;
			}
		}

		var width = uwsettings.instance.vr_hud_panel_width;
		if (width <= 0.05f)
		{
			width = 0.42f;
		}

		var aspect = (float)HudPanelHeightPx / HudPanelWidthPx;
		var quadSize = new Vector2(width, width * aspect);
		AttachHandHudMesh(quadSize);
		SetHudPanelVisible(_hudPanelVisible);
		UpdateXrViewportHdrForUiMode();
		RefreshVrUiQuadMaterial();
		VrDiagLog.Print($"[VR] HUD hand panel attached to left controller ({width:F2}m wide, {HudPanelWidthPx}x{HudPanelHeightPx}).");
	}

	static void SetHudPanelVisible(bool visible)
	{
		_hudPanelVisible = visible;
		if (_hudPanel != null)
		{
			_hudPanel.Visible = visible;
		}

		if (!visible)
		{
			_hudPointerHovering = false;
			_lastHudPointerPos = new Vector2(-1f, -1f);
			if (_hudMouseLayer != null)
			{
				_hudMouseLayer.Visible = false;
			}

			if (!ShouldShowVrPointerLaser())
			{
				UpdatePointerLaser(Vector3.Zero, Vector3.Zero, false);
			}
		}

		UpdateXrViewportHdrForUiMode();
	}

	static void SetHeadOverlaysVisible(bool visible)
	{
		_headOverlaysVisible = visible;
		if (!ShouldShowVrPointerLaser())
		{
			UpdatePointerLaser(Vector3.Zero, Vector3.Zero, false);
		}
	}

	/// <summary>Laser is shown while either the hand HUD or head status overlays are open.</summary>
	static bool ShouldShowVrGameplayPointerLaser()
	{
		if (IsVrInCombat() && SpellCasting.currentSpell == null)
		{
			return false;
		}

		return ShouldShowVrPointerLaser() || SpellCasting.currentSpell != null;
	}

	static bool ShouldShowVrPointerLaser() =>
		_hudPanelVisible || _headOverlaysVisible || IsHudOnMenuScreen() || uimanager.AtMainMenu;

	static bool HudPointerOwnsLaser() =>
		VrNumberPad.IsVisible
		|| NeedsFrontMenuLaser()
		|| ShouldUseHudMenuPointerOnly()
		|| (_hudPanelVisible && _hudPointerHovering);

	static void RetryPendingVrHudSetup()
	{
		if (!IsActive || uwsettings.instance.vr_mirror || !uwsettings.instance.vr_hud_panel || GetHudHandController() == null)
		{
			return;
		}

		if (!uimanager.InGame && !uimanager.InConversation && !uimanager.InAutomap)
		{
			return;
		}

		var underworld = _gameRoot?.GetParent<Node3D>();
		if (underworld == null)
		{
			return;
		}

		if (_hudPanel == null)
		{
			SetupHudHandPanel(underworld);
			return;
		}

		if (_hudPanel.GetParent() == _xrCamera)
		{
			TransitionMenuTvToHandHud();
		}
	}

	static void ApplyHudMenuToggleInput()
	{
		if (!IsActive || uwsettings.instance.vr_mirror || _leftController == null)
		{
			_hudMenuToggleWasPressed = false;
			_hudPanelToggleWasPressed = false;
			return;
		}

		if (IsHudOnMenuScreen())
		{
			_hudMenuToggleWasPressed = false;
			_hudPanelToggleWasPressed = false;
			return;
		}

		var overlayPressed = IsButtonPressed(_leftController, HeadOverlayToggleButtonActions);
		if (overlayPressed && !_hudMenuToggleWasPressed)
		{
			SetHeadOverlaysVisible(!_headOverlaysVisible);
			VrDiagLog.Print($"[VR] Head overlays {(_headOverlaysVisible ? "shown" : "hidden")} (Y).");
		}

		_hudMenuToggleWasPressed = overlayPressed;

		if (!uwsettings.instance.vr_hud_panel)
		{
			_hudPanelToggleWasPressed = false;
			return;
		}

		if (_hudPanel == null)
		{
			RetryPendingVrHudSetup();
		}

		var hudPressed = IsButtonPressed(_leftController, HudPanelToggleButtonActions);
		if (hudPressed && !_hudPanelToggleWasPressed)
		{
			SetHudPanelVisible(!_hudPanelVisible);
			VrDiagLog.Print($"[VR] HUD panel {(_hudPanelVisible ? "shown" : "hidden")} (menu).");
		}

		_hudPanelToggleWasPressed = hudPressed;
	}

	static void EnsurePointerLaser(Node3D underworld)
	{
		if (_pointerLaser != null || underworld == null)
		{
			return;
		}

		var laserMat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.25f, 0.9f, 1f, 0.9f),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DisableReceiveShadows = true,
			NoDepthTest = true,
		};

		_pointerLaserMesh = new CylinderMesh
		{
			TopRadius = PointerLaserRadius,
			BottomRadius = PointerLaserRadius,
			Height = 1f,
			Material = laserMat,
		};

		_pointerLaser = new MeshInstance3D
		{
			Name = "VrPointerLaser",
			Mesh = _pointerLaserMesh,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Layers = main.LayerGeo | main.LayerXFER,
			Visible = false,
		};
		_pointerLaserWorldParent = underworld;
		_pointerLaserOnCamera = false;
		_pointerLaserOnController = false;
		underworld.AddChild(_pointerLaser);
	}

	static void ReparentPointerLaserTo(Node3D parent, bool onCamera, float radius)
	{
		if (_pointerLaser == null || parent == null)
		{
			return;
		}

		_pointerLaserOnCamera = onCamera;
		_pointerLaserOnController = !onCamera && parent is XRController3D;

		if (_pointerLaser.GetParent() != parent)
		{
			_pointerLaser.GetParent()?.RemoveChild(_pointerLaser);
			parent.AddChild(_pointerLaser);
			_pointerLaser.Scale = Vector3.One;
			_pointerLaser.TopLevel = false;
		}

		if (_pointerLaserMesh != null)
		{
			_pointerLaserMesh.TopRadius = radius;
			_pointerLaserMesh.BottomRadius = radius;
		}
	}

	static void ReparentPointerLaser(bool attachToCamera)
	{
		if (attachToCamera)
		{
			ReparentPointerLaserTo(_xrCamera, onCamera: true, PointerLaserRadius);
		}
		else
		{
			ReparentPointerLaserTo(_pointerLaserWorldParent, onCamera: false, PointerLaserRadius);
		}
	}

	static void TryEnsureMenuTvScreen()
	{
		if (_hudViewport != null && _hudPanel != null && _vrUiOnMenuTv)
		{
			EnsureHudMouseLayer();
			return;
		}

		var underworld = _gameRoot?.GetParent<Node3D>();
		if (underworld == null)
		{
			return;
		}

		SetupMenuTvScreen(underworld);
		EnsureHudMouseLayer();
	}

	static void DrawMenuTvLaserOnly()
	{
		TryEnsureMenuTvScreen();

		var controller = GetMenuPointerController();
		if (controller == null || _hudPanel == null)
		{
			return;
		}

		var underworld = _gameRoot?.GetParent<Node3D>();
		if (_pointerLaser == null && underworld != null)
		{
			EnsurePointerLaser(underworld);
		}

		var rayOrigin = GetMenuPointerRayOrigin();
		var rayDir = GetMenuPointerRayDir();
		var hasHit = TryGetHudPanelHit(rayOrigin, rayDir, MenuTvPointerMaxDistance, out _, out var hitWorld);
		DrawMenuPointerLaser(controller, rayOrigin, rayDir, hasHit, hitWorld, MenuTvPointerMaxDistance);
	}

	static void ApplyMenuTvPointerInput()
	{
		var menuPointer = GetMenuPointerController();
		if (menuPointer == null || _hudPanel == null)
		{
			_hudPointerHovering = false;
			_hudPointerLeftWasPressed = false;
			_hudPointerRightWasPressed = false;
			return;
		}

		var underworld = _gameRoot?.GetParent<Node3D>();
		if (_pointerLaser == null && underworld != null)
		{
			EnsurePointerLaser(underworld);
		}

		EnsureHudMouseLayer();

		var rayOrigin = GetMenuPointerRayOrigin();
		var rayDir = GetMenuPointerRayDir();
		var hovering = TryGetHudPanelHit(
			rayOrigin,
			rayDir,
			MenuTvPointerMaxDistance,
			out var viewportPos,
			out var hitWorld);

		DrawMenuPointerLaser(menuPointer, rayOrigin, rayDir, hovering, hitWorld, MenuTvPointerMaxDistance);

		if (hovering)
		{
			if (_hudMouseLayer != null)
			{
				_hudMouseLayer.Visible = true;
			}

			if (viewportPos != _lastHudPointerPos)
			{
				_lastHudPointerPos = viewportPos;
				PushHudMouseMotion(viewportPos);
			}
		}
		else
		{
			_lastHudPointerPos = new Vector2(-1f, -1f);
			if (_hudMouseLayer != null)
			{
				_hudMouseLayer.Visible = false;
			}
		}

		_hudPointerHovering = hovering;
		UpdateMessageScrollHover(viewportPos, hovering);

		if (!hovering)
		{
			_hudPointerLeftWasPressed = false;
			_hudPointerRightWasPressed = false;
			return;
		}

		ApplyHudMenuPointerClicks(viewportPos, menuScreen: true);
	}

	static void UpdateMenuTvPointerLaser()
	{
		ApplyHudPointerInput();
	}

	static void EnsureHudMouseLayer()
	{
		if (_hudMouseLayer != null && GodotObject.IsInstanceValid(_hudMouseLayer))
		{
			return;
		}

		var ui = GetVrUiCanvasLayer(_gameRoot?.GetParent<Node3D>());
		_hudMouseLayer = ui?.GetNodeOrNull<CanvasLayer>("mouse");
	}

	/// <summary>Draw menu laser using the same world ray as HUD hit tests, converted to controller local space.</summary>
	static void DrawMenuPointerLaser(
		XRController3D controller,
		Vector3 rayOrigin,
		Vector3 rayDir,
		bool hasHit,
		Vector3 hitWorld,
		float maxDistance)
	{
		if (_pointerLaser == null || controller == null)
		{
			return;
		}

		ReparentPointerLaserTo(controller, onCamera: false, PointerLaserRadius);
		var endWorld = hasHit ? hitWorld : rayOrigin + rayDir * maxDistance;
		// Aim offset lowers the visible beam origin toward the controller front without moving the UI hit ray.
		var fromLocal = MenuPointerLaserAimOffset;
		var endLocal = controller.ToLocal(endWorld);
		UpdatePointerLaser(fromLocal, endLocal, visible: true, localSpace: true);
	}

	static void LogLaserVisibilityIfChanged(bool visible, string reason)
	{
		if (visible == _introDiagLastLaserVisible)
		{
			return;
		}

		_introDiagLastLaserVisible = visible;
		VrDiagLog.Print(
			$"[VR intro] laser visible -> {visible} ({reason}) "
			+ $"parent={_pointerLaser?.GetParent()?.Name ?? "null"} onCtrl={_pointerLaserOnController}");
	}

	static float _motionBlend = 1f;

	public static void ResetXrOriginFloorTracking()
	{
		_xrOriginFloorInitialized = false;
		_lastSyncedDisplayFloorPos = Vector3.Zero;
		_motionStepInitialized = false;
	}

	/// <summary>Call after each DOS motion tick so VR can interpolate between steps.</summary>
	public static void EndMotionStep()
	{
		var floorPos = GetAvatarFloorPos();
		if (!_motionStepInitialized)
		{
			_motionStepPrevFloor = floorPos;
			_motionStepCurrFloor = floorPos;
			_motionStepInitialized = true;
			return;
		}

		_motionStepPrevFloor = _motionStepCurrFloor;
		_motionStepCurrFloor = floorPos;
	}

	/// <summary>Seed interpolation state after the avatar is first positioned.</summary>
	public static void InitializeMotionStep()
	{
		var floorPos = GetAvatarFloorPos();
		_motionStepPrevFloor = floorPos;
		_motionStepCurrFloor = floorPos;
		_motionStepInitialized = true;
	}

	static Vector3 GetDisplayFloorPos()
	{
		if (!_motionStepInitialized || uwsettings.instance.vr_mirror)
		{
			return GetAvatarFloorPos();
		}

		return _motionStepPrevFloor.Lerp(_motionStepCurrFloor, _motionBlend);
	}

	static Vector3 GetAvatarFloorPos()
	{
		var feet = uwObject.XYZToVector3(
			motion.playerMotionParams.x_0,
			motion.playerMotionParams.y_2,
			motion.playerMotionParams.z_4);
		return new Vector3(feet.X, GetGameFloorY(), feet.Z);
	}

	static float GetBodyYawRadians()
	{
		return (float)(-((float)playerdat.PlayerCameraYaw_dseg_8294 / 32767f) * Math.PI);
	}

	static void ApplyXrPlaySpaceRotation()
	{
		_xrOrigin.Rotation = Vector3.Zero;
		_xrOrigin.Rotate(Vector3.Up, (float)Math.PI);
		_xrOrigin.Rotate(Vector3.Up, _xrPlaySpaceYawRadians);
	}

	public static void SyncXrOriginFromGimbal()
	{
		if (_xrOrigin == null || main.cameraYawGimbal_world == null || uwsettings.instance.vr_mirror)
		{
			return;
		}

		// Follow avatar by delta only — preserves the sticky XZ offset from B-recenter.
		// Interpolate between DOS motion ticks so the play space does not snap at ~10 Hz.
		var floorPos = GetDisplayFloorPos();
		if (!_xrOriginFloorInitialized)
		{
			_xrOrigin.GlobalPosition = floorPos;
			_lastSyncedDisplayFloorPos = floorPos;
			_xrOriginFloorInitialized = true;
			_xrPlaySpaceYawRadians = GetBodyYawRadians();
		}
		else
		{
			_xrOrigin.GlobalPosition += floorPos - _lastSyncedDisplayFloorPos;
			_lastSyncedDisplayFloorPos = floorPos;
		}

		// Play-space yaw changes only via comfort snap-turn (RotatePlaySpaceYaw), not body-facing sync.
		ApplyXrPlaySpaceRotation();
	}

	/// <summary>Mirror mode: XR origin carries body position/yaw; OpenXR rotates the XRCamera for head look.</summary>
	static void SyncXrOriginBodyFromGame()
	{
		if (_xrOrigin == null || main.cameraYawGimbal_world == null)
		{
			return;
		}

		_xrOrigin.GlobalPosition = main.cameraYawGimbal_world.GlobalPosition;

		var bodyYaw = (float)(-((float)playerdat.PlayerCameraYaw_dseg_8294 / 32767f) * Math.PI);
		_xrOrigin.Rotation = Vector3.Zero;
		_xrOrigin.Rotate(Vector3.Up, (float)Math.PI);
		_xrOrigin.Rotate(Vector3.Up, bodyYaw);
	}

	/// <summary>Drive the flat SubViewport camera from the tracked XRCamera so the mirror shows head look.</summary>
	public static void SyncMirrorHeadLook()
	{
		if (_xrCamera == null || _mirrorYawGimbal == null || _mirrorRollGimbal == null || _mirrorPitchGimbal == null)
		{
			return;
		}

		// Body yaw from locomotion/snap-turn; head offset from OpenXR tracking (local to XROrigin).
		var bodyYaw = (float)(-((float)playerdat.PlayerCameraYaw_dseg_8294 / 32767f) * Math.PI);
		var headEuler = _xrCamera.Transform.Basis.GetEuler(EulerOrder.Yxz);

		_mirrorYawGimbal.Rotation = Vector3.Zero;
		_mirrorYawGimbal.Rotate(Vector3.Up, (float)Math.PI);
		_mirrorYawGimbal.Rotate(Vector3.Up, bodyYaw + headEuler.Y);

		_mirrorRollGimbal.Rotation = Vector3.Zero;
		_mirrorRollGimbal.Rotate(Vector3.Forward, -headEuler.Z);

		_mirrorPitchGimbal.Rotation = Vector3.Zero;
		_mirrorPitchGimbal.Rotate(Vector3.Right, -headEuler.X);

		var visionYaw = GetHeadYawForVision();
		UpdateVisionHeadingFromYaw(visionYaw);
		UpdateVisionFromHead(playerdat.CameraTileX, playerdat.CameraTileY, visionYaw);
	}

	/// <summary>
	/// B-recenter only: shift the play space so the headset sits over the cyan avatar (XZ).
	/// That sticky offset is preserved by <see cref="SyncXrOriginFromGimbal"/> until the next B.
	/// </summary>
	public static void SnapRoomOriginToAvatar()
	{
		if (_xrOrigin == null || _xrCamera == null || uwsettings.instance.vr_mirror)
		{
			return;
		}

		if (uimanager.InGame)
		{
			playerdat.PositionPlayerCamera();
		}

		SyncXrOriginFromGimbal();

		var floorPos = GetAvatarFloorPos();
		var headWorld = _xrCamera.GlobalPosition;
		var delta = new Vector3(floorPos.X - headWorld.X, 0f, floorPos.Z - headWorld.Z);
		_xrOrigin.GlobalPosition += delta;
		_lastSyncedDisplayFloorPos = floorPos;
		_motionStepPrevFloor = GetAvatarFloorPos();
		_motionStepCurrFloor = _motionStepPrevFloor;
		_motionStepInitialized = true;
	}

	static void ApplyRecenterInput()
	{
		if (!IsActive || uwsettings.instance.vr_mirror || _rightController == null)
		{
			_recenterWasPressed = false;
			return;
		}

		var pressed = IsButtonPressed(_rightController, RecenterStickClickActions);
		if (pressed && !_recenterWasPressed)
		{
			SnapRoomOriginToAvatar();
			VrDiagLog.Print("[VR] View recentered (right stick click).");
		}

		_recenterWasPressed = pressed;
	}

	static void ApplySpellCastShortcutInput()
	{
		if (!IsActive || uwsettings.instance.vr_mirror || _leftController == null
			|| !uimanager.InGame || uimanager.blockinput || IsHudOnMenuScreen())
		{
			_spellCastShortcutWasPressed = false;
			return;
		}

		var pressed = IsButtonPressed(_leftController, SpellCastShortcutActions);
		if (pressed && !_spellCastShortcutWasPressed && playerdat.NoOfSelectedRunes > 0)
		{
			RunicMagic.CastRunicSpell();
		}

		_spellCastShortcutWasPressed = pressed;
	}

	/// <summary>
	/// Head height from OpenXR; room-scale X/Z is left alone so you can lean/walk in the play space.
	/// Press right stick click to snap the view back onto the cyan avatar.
	/// </summary>
	static void ApplyNativeXrTrackingPassthrough()
	{
		if (_xrCamera == null || uwsettings.instance.vr_mirror)
		{
			return;
		}

		if (uwsettings.instance.vr_debug && Engine.GetProcessFrames() % DebugLogIntervalFrames == 0)
		{
			var transform = _xrCamera.Transform;
			var floorY = GetGameFloorY();
			VrDiagLog.Print($"[VR debug] passthrough localXZ=({transform.Origin.X:F3},{transform.Origin.Z:F3}) rawY={transform.Origin.Y:F3} worldEyeY={_xrCamera.GlobalPosition.Y:F3} floorY={floorY:F3}");
		}
	}

	static void UpdateHeadRelativeMotionYaw()
	{
		if (_leftController == null)
		{
			return;
		}

		var stick = ReadStick(_leftController);
		if (stick.Length() > StickDeadzone)
		{
			_motionYaw = GetHeadYawForMotion();
			_useHeadRelativeMotion = true;
		}
	}

	/// <summary>When true, motion uses <see cref="_motionYaw"/> (look direction) instead of body facing.</summary>
	public static bool TryGetMotionYaw(out short yaw)
	{
		if (IsActive && !uwsettings.instance.vr_mirror && _useHeadRelativeMotion)
		{
			yaw = _motionYaw;
			return true;
		}

		yaw = 0;
		return false;
	}

	static void ConfigureRootWindowForXr(SceneTree tree)
	{
		if (tree.Root is Window window)
		{
			window.ContentScaleMode = Window.ContentScaleModeEnum.Disabled;
			VrDebugLog("ConfigureRootWindowForXr", $"ContentScaleMode={window.ContentScaleMode}");
		}
	}

	static void EnsureWorldEnvironment(Node3D underworld)
	{
		var existing = underworld.GetNodeOrNull<WorldEnvironment>("VrWorldEnvironment");
		WorldEnvironment worldEnvironment;
		if (existing != null)
		{
			worldEnvironment = existing;
		}
		else
		{
			worldEnvironment = new WorldEnvironment { Name = "VrWorldEnvironment" };
			underworld.AddChild(worldEnvironment);
		}

		worldEnvironment.Environment ??= new Godot.Environment();
		worldEnvironment.Environment.BackgroundMode = Godot.Environment.BGMode.Color;
		worldEnvironment.Environment.BackgroundColor = Colors.Black;
		// UW lighting is entirely shader/palette-based (unshaded); no Godot ambient fill.
		worldEnvironment.Environment.AmbientLightSource = Godot.Environment.AmbientSource.Disabled;
		// Flat mode has no WorldEnvironment; keep tonemap linear with no exposure crush.
		worldEnvironment.Environment.TonemapMode = Godot.Environment.ToneMapper.Linear;
		worldEnvironment.Environment.TonemapExposure = 1.0f;
	}

	static void LogVrSetupState(main gameRoot, Viewport rootViewport, string phase)
	{
		if (!uwsettings.instance.vr_debug)
		{
			return;
		}

		var flatCam = main.cameraPitchGimbal_world;

		VrDiagLog.Print($"[VR debug] ========== VR SETUP ({phase}) ==========");
		VrDiagLog.Print($"[VR debug] vr_mirror={uwsettings.instance.vr_mirror} openXrEnabled={_openXrOutputEnabled}");
		VrDiagLog.Print($"[VR debug] Root UseXR={rootViewport?.UseXR} VrsMode={rootViewport?.VrsMode}");
		VrDiagLog.Print($"[VR debug] SubViewport update={_gameViewport?.RenderTargetUpdateMode} size={_gameViewport?.Size}");
		VrDiagLog.Print($"[VR debug] Flat camera Current={flatCam?.Current} inTree={flatCam?.IsInsideTree()}");
		VrDiagLog.Print($"[VR debug] XROrigin inTree={_xrOrigin?.IsInsideTree()} path={_xrOrigin?.GetPath()}");
		VrDiagLog.Print($"[VR debug] XRCamera inTree={_xrCamera?.IsInsideTree()} path={_xrCamera?.GetPath()} Current={_xrCamera?.Current}");
		VrDiagLog.Print($"[VR debug] Mirror={_xrCamera?.GetNodeOrNull("VrMirrorScreen") != null}");
		VrDiagLog.Print("[VR debug] ========================================");
	}

	static void LogVrRuntimeState()
	{
		var rootVp = _sceneTree?.Root?.GetViewport();
		VrDiagLog.Print($"[VR debug] frame={_debugFrameCounter} UseXR={rootVp?.UseXR} xrInTree={_xrCamera?.IsInsideTree()} flatCam={main.cameraPitchGimbal_world?.Current} xrCam={_xrCamera?.Current}");
	}

	static long _introDiagLastLogMsec;
	static long _introDiagLastSnapshotMsec;
	static bool _introDiagLastBodyMarkerVisible;
	static string _introDiagLastLaserSkipReason = "";

	static bool IntroDiagEnabled =>
		uwsettings.instance.vr_diag_log
		|| uwsettings.instance.vr_debug
		|| uwsettings.instance.vr_intro_debug;

	static void IntroDiagLog(string message, bool throttle = true)
	{
		if (!IntroDiagEnabled)
		{
			return;
		}

		if (throttle)
		{
			var now = (long)Time.GetTicksMsec();
			if (now - _introDiagLastLogMsec < 3000)
			{
				return;
			}

			_introDiagLastLogMsec = now;
		}

		VrDiagLog.Print($"[VR intro] {message}");
	}

	static void IntroDiagLogOnce(ref string lastKey, string key, string message)
	{
		if (!IntroDiagEnabled || lastKey == key)
		{
			return;
		}

		lastKey = key;
		VrDiagLog.Print($"[VR intro] {message}");
	}

	static void LogIntroDiagSnapshot(string reason, bool force = false)
	{
		if (!IntroDiagEnabled)
		{
			return;
		}

		var now = (long)Time.GetTicksMsec();
		if (!force && now - _introDiagLastSnapshotMsec < 3000)
		{
			return;
		}

		_introDiagLastSnapshotMsec = now;

		var hudParent = _hudPanel?.GetParent()?.Name ?? "null";
		var laserLen = _pointerLaserMesh?.Height ?? 0f;
		var laserVisible = _pointerLaser?.Visible == true;
		var bodyVisible = _bodyMarker?.Visible == true;
		var rightPos = _rightController?.GlobalPosition ?? Vector3.Zero;
		var leftPos = _leftController?.GlobalPosition ?? Vector3.Zero;
		VrDiagLog.Print(
			$"[VR intro] snapshot ({reason}) "
			+ $"active={IsActive} tickVr={ShouldTickVrInput()} mode={uimanager.CurrentGameMode} "
			+ $"atMain={uimanager.AtMainMenu} inGame={uimanager.InGame} blockinput={uimanager.blockinput} "
			+ $"menuTv={_vrUiOnMenuTv} onMenuScreen={IsHudOnMenuScreen()} needsMenuLaser={NeedsFrontMenuLaser()} "
			+ $"headRay={UseHeadRayForMenuPointer()} "
			+ $"hudPanel={_hudPanel != null} hudVp={_hudViewport != null} hudParent={hudParent} hudVis={_hudPanelVisible} "
			+ $"pointerLaser={_pointerLaser != null} laserVis={laserVisible} laserLen={laserLen:F3} "
			+ $"bodyShow={ShouldShowBodyMarker()} bodyVis={bodyVisible} "
			+ $"rightCtrl={_rightController != null} leftCtrl={_leftController != null} "
			+ $"rightPos=({rightPos.X:F2},{rightPos.Y:F2},{rightPos.Z:F2}) "
			+ $"leftPos=({leftPos.X:F2},{leftPos.Y:F2},{leftPos.Z:F2})");
	}

	static void VrDebugLog(string phase, string message)
	{
		if (uwsettings.instance.vr_debug)
		{
			VrDiagLog.Print($"[VR debug] {phase}: {message}");
		}
	}

	static XRController3D CreateController(XROrigin3D origin, string name, StringName tracker)
	{
		var controller = new XRController3D { Name = name, Tracker = tracker };
		origin.AddChild(controller);
		return controller;
	}

	public static void ApplyMotionInputs()
	{
		if (!IsActive || _leftController == null || !uimanager.InGame)
		{
			return;
		}

		var stick = ReadStick(_leftController);
		motion.PlayerMotionWalk_77C = 0;
		motion.PlayerMotionHeading_77E = 0;
		motion.MotionInputPressed = 0;
		_useHeadRelativeMotion = false;

		var moving = stick.Length() > StickDeadzone;
		if (moving && !uwsettings.instance.vr_mirror)
		{
			_motionYaw = GetHeadYawForMotion();
			_useHeadRelativeMotion = true;
		}

		if (stick.Y > StickDeadzone)
		{
			motion.PlayerMotionWalk_77C = stick.Y > 0.75f ? (short)0x70 : (short)0x32;
			motion.MotionInputPressed = 1;
		}
		else if (stick.Y < -StickDeadzone)
		{
			motion.MotionInputPressed = 8;
		}

		if (stick.X < -StickDeadzone)
		{
			motion.MotionInputPressed = 9;
		}
		else if (stick.X > StickDeadzone)
		{
			motion.MotionInputPressed = 0xA;
		}

		ApplyJumpInput();
	}

	static void ApplyBodyFacingFromHead()
	{
		if (!IsActive || uwsettings.instance.vr_mirror || !uimanager.InGame)
		{
			return;
		}

		SyncPlayerYawFromHead();
		playerdat.PlayerCameraPitch_dseg_67d6_33D6 = GetHeadPitchUw();
		motion.SyncPlayerObjectHeadingFromCameraYaw(playerdat.playerObject);
	}

	public static XRController3D GetDominantController()
		=> playerdat.isLefty ? _leftController : _rightController;

	public static XRController3D GetOffHandController()
		=> playerdat.isLefty ? _rightController : _leftController;

	public static XRController3D GetWeaponHandController()
	{
		if (!IsActive)
		{
			return null;
		}

		return GetDominantController();
	}

	static XRController3D GetHudHandController() => GetOffHandController();

	static XRController3D GetAimController() => GetDominantController();

	static Vector3 GetAimRayOrigin()
	{
		var controller = GetAimController();
		return controller?.GlobalPosition ?? GetAvatarBodyCenter();
	}

	static bool IsVrInCombat() =>
		uimanager.InGame
		&& uimanager.InteractionMode == uimanager.InteractionModes.ModeAttack
		&& playerdat.play_drawn == 1;

	static bool ShouldOfferVrEscapeGrip()
	{
		if (MessageDisplay.WaitingForYesOrNo || MessageDisplay.WaitingForTypedInput)
		{
			return true;
		}

		if (uimanager.blockinput)
		{
			return true;
		}

		switch (uimanager.CurrentGameMode)
		{
			case uimanager.GameModes.CUTSCENE:
			case uimanager.GameModes.MAIN:
			case uimanager.GameModes.CHARGEN:
			case uimanager.GameModes.JOURNEY:
				return true;
		}

		return false;
	}

	static Vector3 GetHudPanelLocalPosition()
	{
		if (playerdat.isLefty)
		{
			return HudPanelLocalPosition;
		}

		return new Vector3(-HudPanelLocalPosition.X, HudPanelLocalPosition.Y, HudPanelLocalPosition.Z);
	}

	static Vector3 GetHudPanelLocalRotationDegrees()
	{
		if (playerdat.isLefty)
		{
			return HudPanelLocalRotationDegrees;
		}

		return new Vector3(
			HudPanelLocalRotationDegrees.X,
			-HudPanelLocalRotationDegrees.Y,
			HudPanelLocalRotationDegrees.Z);
	}

	public static Vector3 WorldToTorsoLocal(Vector3 worldPos)
	{
		var frame = GetTorsoFrame();
		var rel = worldPos - frame.Origin;
		return new Vector3(
			rel.Dot(frame.Basis.X),
			rel.Dot(frame.Basis.Y),
			rel.Dot(frame.Basis.Z));
	}

	/// <summary>Torso-local gesture frame for debug overlays (chest origin, body yaw).</summary>
	public static Transform3D GetTorsoTransform() => GetTorsoFrame();

	internal static Node3D GetUnderworldNode() => _gameRoot?.GetParent<Node3D>();

	static Transform3D GetTorsoFrame()
	{
		var origin = GetAvatarBodyCenter();
		if (_xrCamera != null)
		{
			origin = _xrCamera.GlobalPosition + Vector3.Down * 0.35f;
		}

		var forward = -(_xrCamera?.GlobalTransform.Basis.Z ?? Vector3.Forward);
		forward.Y = 0f;
		if (forward.LengthSquared() < 1e-5f)
		{
			forward = Vector3.Forward;
		}
		else
		{
			forward = forward.Normalized();
		}

		var right = forward.Cross(Vector3.Up).Normalized();
		var basis = new Basis(right, Vector3.Up, forward);
		return new Transform3D(basis, origin);
	}

	static short GetHeadPitchUw()
	{
		if (_xrCamera == null)
		{
			return playerdat.PlayerCameraPitch_dseg_67d6_33D6;
		}

		var forward = -_xrCamera.GlobalTransform.Basis.Z;
		var horizLen = Mathf.Sqrt(forward.X * forward.X + forward.Z * forward.Z);
		if (horizLen < 1e-5f)
		{
			return playerdat.PlayerCameraPitch_dseg_67d6_33D6;
		}

		var elevDeg = Mathf.RadToDeg(Mathf.Atan2(forward.Y, horizLen));
		var pitchIndex = Mathf.Clamp(elevDeg / 6f, -4f, 16f);
		return (short)(pitchIndex * 0x300);
	}

	/// <summary>Refresh body heading and look pitch immediately before a combat strike.</summary>
	public static void SyncCombatAimFromHead()
	{
		if (!IsActive || uwsettings.instance.vr_mirror || !uimanager.InGame || _xrCamera == null)
		{
			return;
		}

		SyncPlayerYawFromHead();
		playerdat.PlayerCameraPitch_dseg_67d6_33D6 = GetHeadPitchUw();
		motion.SyncPlayerObjectHeadingFromCameraYaw(playerdat.playerObject);
	}

	/// <summary>Map head look to viewport coords for ranged combat and legacy combat helpers.</summary>
	public static void UpdateViewPortMouseFromHeadAim()
	{
		if (_xrCamera == null)
		{
			return;
		}

		var rayOrigin = _xrCamera.GlobalPosition;
		var rayDir = -_xrCamera.GlobalTransform.Basis.Z;
		UpdateViewPortMouseFromControllerRay(rayOrigin, rayDir);
	}

	static void RotatePlaySpaceYaw(float radians)
	{
		if (_xrOrigin == null)
		{
			return;
		}

		if (_xrCamera != null)
		{
			var headBefore = _xrCamera.GlobalPosition;
			_xrPlaySpaceYawRadians -= radians;
			ApplyXrPlaySpaceRotation();
			var headAfter = _xrCamera.GlobalPosition;
			_xrOrigin.GlobalPosition += new Vector3(headBefore.X - headAfter.X, 0f, headBefore.Z - headAfter.Z);
		}
		else
		{
			_xrPlaySpaceYawRadians -= radians;
			ApplyXrPlaySpaceRotation();
		}
	}

	static void ApplyJumpInput()
	{
		if (!IsActive || playerdat.ParalyseTimer > 0 || playerdat.TileState == 1)
		{
			_jumpWasPressed = false;
			return;
		}

		var pressed = IsButtonPressed(_rightController, JumpButtonActions);
		if (pressed && !_jumpWasPressed)
		{
			VrCombatMotionLog.LogJumpMarker();
			motion.MotionInputPressed = 7;
		}

		_jumpWasPressed = pressed;
	}

	static bool IsButtonPressed(XRController3D controller, StringName[] actions)
	{
		if (controller == null)
		{
			return false;
		}

		foreach (var action in actions)
		{
			if (controller.IsButtonPressed(action))
			{
				return true;
			}
		}

		return false;
	}

	static Vector2 ReadStick(XRController3D controller)
	{
		if (controller == null)
		{
			return Vector2.Zero;
		}

		Vector2 stick = Vector2.Zero;
		foreach (var action in StickActions)
		{
			stick = controller.GetVector2(action);
			if (stick.Length() >= 0.01f)
			{
				break;
			}
		}

		if (uwsettings.instance.vr_invert_stick_y)
		{
			stick.Y = -stick.Y;
		}

		return stick;
	}

	static void ApplySnapTurn()
	{
		if (_rightController == null || _snapTurnCooldown > 0f)
		{
			return;
		}

		var stick = ReadStick(_rightController);
		if (stick.X > StickDeadzone)
		{
			SnapTurn(+SnapTurnDegrees);
		}
		else if (stick.X < -StickDeadzone)
		{
			SnapTurn(-SnapTurnDegrees);
		}
	}

	static void SnapTurn(float degrees)
	{
		_snapTurnCooldown = SnapTurnCooldownSeconds;

		if (uwsettings.instance.vr_mirror)
		{
			playerdat.PlayerCameraYaw_dseg_8294 = (short)(playerdat.PlayerCameraYaw_dseg_8294 + (degrees / 180f * 32767f));
			SyncXrOriginBodyFromGame();
			return;
		}

		// Comfort turn: rotate play space only; body facing follows global head direction.
		RotatePlaySpaceYaw(Mathf.DegToRad(degrees));
	}

	public static void SyncPlayerYawFromHead()
	{
		playerdat.PlayerCameraYaw_dseg_8294 = GetHeadYawForMotion();
		UpdateVisionHeadingFromYaw(playerdat.PlayerCameraYaw_dseg_8294);
	}

	/// <summary>Convert horizontal look direction to UW heading (matches PositionCamera axis mapping).</summary>
	public static short GetHeadYawForMotion()
	{
		if (_xrCamera == null)
		{
			return playerdat.PlayerCameraYaw_dseg_8294;
		}

		var forward = -_xrCamera.GlobalTransform.Basis.Z;
		forward.Y = 0;
		if (forward.LengthSquared() < 0.0001f)
		{
			return playerdat.PlayerCameraYaw_dseg_8294;
		}

		forward = forward.Normalized();

		// Godot world X/Z ↔ UW game X/Y (see PositionCamera underworldVector remap).
		var gameDx = -forward.X;
		var gameDy = forward.Z;
		var gameYawRad = Mathf.Atan2(gameDx, gameDy);
		return (short)(gameYawRad / Math.PI * 32767f);
	}

	public static short GetHeadYawForVision()
	{
		return GetHeadYawForMotion();
	}

	/// <summary>Convert a horizontal laser direction to the UW heading byte used by motion/drop.</summary>
	public static int GetUwHeadingByteFromRay(Vector3 rayDir)
	{
		rayDir = rayDir.Normalized();
		var gameDx = -rayDir.X;
		var gameDy = rayDir.Z;
		if (gameDx * gameDx + gameDy * gameDy < 1e-6f)
		{
			return (playerdat.PlayerCameraYaw_dseg_8294 >> 8) & 0xFF;
		}

		var gameYawRad = Mathf.Atan2(gameDx, gameDy);
		var yaw = (short)(gameYawRad / Math.PI * 32767f);
		return (yaw >> 8) & 0xFF;
	}

	/// <summary>Convert laser elevation to UW missile pitch (independent of avatar facing).</summary>
	public static int GetUwPitchFromRay(Vector3 rayDir)
	{
		rayDir = rayDir.Normalized();
		var horizLen = Mathf.Sqrt(rayDir.X * rayDir.X + rayDir.Z * rayDir.Z);
		if (horizLen < 1e-5f)
		{
			return playerdat.PlayerCameraPitch_dseg_67d6_33D6 / 0x300;
		}

		var elevDeg = Mathf.RadToDeg(Mathf.Atan2(rayDir.Y, horizLen));
		return (int)Mathf.Clamp(elevDeg / 6f, -4f, 16f);
	}

	public static void UpdateVisionHeadingFromYaw(short yaw)
	{
		playerdat.CameraYawHeadingRelated_2B52 = (short)(((1 + (yaw >> 0xD)) & 0x7) >> 1);
		playerdat.CameraPointer2C = (short)(yaw - motion.PlayerCardinalHeadingLookupTable[playerdat.CameraYawHeadingRelated_2B52]);
	}

	public static void UpdateVisionFromHead(short tileX, short tileY, short yaw)
	{
		if (!uimanager.InGame)
		{
			return;
		}

		var x = (short)(tileX & 0xFF);
		var y = (short)(tileY & 0xFF);

		switch (playerdat.CameraYawHeadingRelated_2B52)
		{
			case 1:
				{
					var si = x;
					x = (short)(0xFF - y);
					y = si;
					break;
				}
			case 2:
				{
					x = (short)(0xFF - x);
					y = (short)(0xFF - y);
					break;
				}
			case 3:
				{
					var si = x;
					x = y;
					y = (short)(0xFF - si);
					break;
				}
		}

		var visionYaw = (short)(yaw - VisionParams.cardinallookup_44A[playerdat.CameraYawHeadingRelated_2B52]);
		playerdat.LOS_x = (short)(x & 0xFF);
		playerdat.LOS_y = (short)(y & 0xFF);

		VisionParams.SetRangeOfVisionParams(camerax: x, cameray: y, camerayaw: visionYaw);
		VisionParams.GetViewDistance();
		VisionParams.FakeRender();
	}

	static void EnsureBodyMarker(Node3D underworld)
	{
		if (!IsActive || uwsettings.instance.vr_mirror || !uwsettings.instance.vr_show_body)
		{
			return;
		}

		if (_bodyMarker != null && GodotObject.IsInstanceValid(_bodyMarker))
		{
			return;
		}

		_bodyMarker = new MeshInstance3D
		{
			Name = "VrBodyMarker",
			Scale = Vector3.One * BodyMarkerScale,
			Visible = false,
			// XR camera culls LayerGeo|LayerXFER; default layer 1 is invisible in native VR.
			Layers = main.LayerGeo | main.LayerXFER,
		};
		var mat = new StandardMaterial3D
		{
			AlbedoColor = new Color(0.25f, 0.85f, 1f, 0.55f),
			Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			NoDepthTest = true,
		};
		_bodyMarker.Mesh = new CapsuleMesh();
		_bodyMarker.MaterialOverride = mat;
		underworld.AddChild(_bodyMarker);
		UpdateBodyMarker();
		VrDiagLog.Print("[VR] Body marker created.");
	}

	static void UpdateBodyMarker()
	{
		if (_bodyMarker == null || !GodotObject.IsInstanceValid(_bodyMarker))
		{
			return;
		}

		var show = ShouldShowBodyMarker();
		if (show != _introDiagLastBodyMarkerVisible)
		{
			_introDiagLastBodyMarkerVisible = show;
			IntroDiagLog(
				$"body marker visible -> {show} (mode={uimanager.CurrentGameMode} inGame={uimanager.InGame} "
				+ $"atMain={uimanager.AtMainMenu} menuTv={IsHudOnMenuScreen()} vr_show_body={uwsettings.instance.vr_show_body})",
				throttle: false);
		}

		_bodyMarker.Visible = show;
		if (!show)
		{
			return;
		}

		var px = motion.playerMotionParams.x_0;
		var py = motion.playerMotionParams.y_2;
		var pz = motion.playerMotionParams.z_4;
		var displayFloor = GetDisplayFloorPos();
		var simFeet = uwObject.XYZToVector3(px, py, pz);
		var eye = uwObject.XYZToVector3(px, py, pz + 0xA4);
		var bodyHeight = Mathf.Max(0.2f, eye.Y - simFeet.Y);
		var radius = Mathf.Max(0.08f, (motion.playerMotionParams.radius_22 / 8f) * tileMapRender.TileWidth);

		if (_bodyMarker.Mesh is CapsuleMesh capsule)
		{
			capsule.Radius = radius;
			capsule.Height = bodyHeight;
		}

		_bodyMarker.GlobalPosition = GetAvatarBodyCenter();
		var bodyYaw = (float)(-((float)playerdat.PlayerCameraYaw_dseg_8294 / 32767f) * Math.PI);
		_bodyMarker.Rotation = new Vector3(0f, bodyYaw, 0f);
	}

	static bool ShouldShowBodyMarker()
	{
		if (!IsActive || uwsettings.instance.vr_mirror || !uwsettings.instance.vr_show_body)
		{
			return false;
		}

		// Only during live gameplay — hide on intro/menu/chargen, cutscenes, and the menu TV.
		return uimanager.InGame
			&& uimanager.CurrentGameMode == uimanager.GameModes.GAME
			&& !uimanager.AtMainMenu
			&& !IsHudOnMenuScreen();
	}

	static void SyncVrObjectInfoCamera()
	{
		if (!IsActive || uwsettings.instance.vr_mirror || _xrCamera == null)
		{
			return;
		}

		if (main.cameraPitchGimbal_objectinfo != null)
		{
			main.cameraPitchGimbal_objectinfo.GlobalTransform = _xrCamera.GlobalTransform;
		}
	}

	public static void OnConversationStarted()
	{
		if (!IsActive)
		{
			return;
		}

		SetHudPanelVisible(true);
	}

	static void ApplyNumberPadPointerInput()
	{
		var aimController = GetAimController();
		if (!IsActive || uwsettings.instance.vr_mirror || aimController == null)
		{
			_numberPadLeftWasPressed = false;
			_lastNumberPadPointerPos = new Vector2(-1f, -1f);
			return;
		}

		var rayOrigin = GetAimRayOrigin();
		var rayDir = GetControllerRayDir();
		var hovering = VrNumberPad.TryGetHit(
			rayOrigin,
			rayDir,
			HudPointerMaxDistance,
			out var viewportPos,
			out var hitWorld);

		if (hovering)
		{
			UpdatePointerLaser(rayOrigin, hitWorld, visible: true);
			if (viewportPos != _lastNumberPadPointerPos)
			{
				_lastNumberPadPointerPos = viewportPos;
				VrNumberPad.PushMouseMotion(viewportPos);
			}
		}
		else
		{
			UpdatePointerLaser(rayOrigin, rayOrigin + rayDir * 0.35f, visible: true);
			_lastNumberPadPointerPos = new Vector2(-1f, -1f);
		}

		var leftPressed = IsHudPointerLeftClickHeld(menuScreen: false);
		if (hovering && leftPressed && !_numberPadLeftWasPressed)
		{
			VrNumberPad.PushMouseClick(viewportPos, MouseButton.Left);
		}

		_numberPadLeftWasPressed = leftPressed;
	}

	static void ApplyCombatModeToggleInput()
	{
		if (!IsActive || uwsettings.instance.vr_mirror || _rightController == null
			|| !uimanager.InGame || uimanager.blockinput || IsHudOnMenuScreen())
		{
			_combatToggleWasPressed = false;
			return;
		}

		if ((_hudPanelVisible && _hudPointerHovering) || _statusOverlayHovering)
		{
			_combatToggleWasPressed = IsButtonPressed(_rightController, RecenterButtonActions);
			return;
		}

		var pressed = IsButtonPressed(_rightController, RecenterButtonActions);
		if (pressed && !_combatToggleWasPressed)
		{
			uimanager.ToggleVrCombatMode();
		}

		_combatToggleWasPressed = pressed;
	}

	static void ApplySpellTargetingInput()
	{
		IsVrWorldPointerActive = false;
		IsVrWorldRightHeld = false;

		var dominant = GetDominantController();
		if (!IsActive || uwsettings.instance.vr_mirror || dominant == null || _xrCamera == null)
		{
			ResetSpellTargetingPressState();
			return;
		}

		if (IsHudOnMenuScreen() || !uimanager.InGame || uimanager.blockinput)
		{
			ResetSpellTargetingPressState();
			return;
		}

		if ((_hudPanelVisible && _hudPointerHovering) || _statusOverlayHovering)
		{
			ResetSpellTargetingPressState();
			return;
		}

		var rayOrigin = GetAimRayOrigin();
		var rayDir = GetControllerRayDir();
		IsVrWorldPointerActive = true;
		UpdateViewPortMouseFromControllerRay(rayOrigin, rayDir);

		var triggerPressed = IsButtonPressed(dominant, HudLeftClickActions);
		if (triggerPressed && !_spellCastTriggerWasPressed)
		{
			TryInteractLaserVerb(rayOrigin, rayDir, uimanager.InteractionModes.ModeUse);
		}

		_spellCastTriggerWasPressed = triggerPressed;

		var gripPressed = IsButtonPressed(dominant, HudRightClickActions);
		if (gripPressed && !_spellCastGripWasPressed)
		{
			CancelArmedSpell();
		}

		_spellCastGripWasPressed = gripPressed;
	}

	static void ApplyExplorationVerbInput()
	{
		IsVrWorldPointerActive = false;
		IsVrWorldRightHeld = false;

		var dominant = GetDominantController();
		var offHand = GetOffHandController();
		if (!IsActive || uwsettings.instance.vr_mirror || dominant == null || offHand == null || _xrCamera == null)
		{
			ResetExplorationVerbPressState();
			return;
		}

		if (IsHudOnMenuScreen() || !uimanager.InGame || uimanager.blockinput)
		{
			ResetExplorationVerbPressState();
			return;
		}

		var rayOrigin = GetAimRayOrigin();
		var rayDir = GetControllerRayDir();
		var hudHovering = (_hudPanelVisible && _hudPointerHovering) || _statusOverlayHovering;

		// Get: grip press pick / release place. Use: trigger release (DOS-aligned).
		ApplyDominantGetGripInput(dominant, rayOrigin, rayDir, hudHovering);
		ApplyDominantUseTriggerInput(dominant, rayOrigin, rayDir, hudHovering);

		if (hudHovering)
		{
			// Absorb Look/Talk edges while the laser is on UI; Get/Use own their dominant state.
			_offHandGripWasPressed = IsButtonPressed(offHand, HudRightClickActions);
			_offHandTriggerWasPressed = IsButtonPressed(offHand, HudLeftClickActions);
			return;
		}

		IsVrWorldPointerActive = true;
		UpdateViewPortMouseFromControllerRay(rayOrigin, rayDir);

		if (playerdat.ObjectInHand != -1)
		{
			_offHandGripWasPressed = IsButtonPressed(offHand, HudRightClickActions);
			_offHandTriggerWasPressed = IsButtonPressed(offHand, HudLeftClickActions);
			return;
		}

		if (IsVrInCombat())
		{
			if (TryConsumeCombatBlockedVerbPress(dominant, offHand))
			{
				_offHandGripWasPressed = IsButtonPressed(offHand, HudRightClickActions);
				_offHandTriggerWasPressed = IsButtonPressed(offHand, HudLeftClickActions);
				return;
			}
		}

		TryVerbButtonEdge(offHand, HudLeftClickActions, ref _offHandTriggerWasPressed,
			uimanager.InteractionModes.ModeLook, rayOrigin, rayDir);
		TryVerbButtonEdge(offHand, HudRightClickActions, ref _offHandGripWasPressed,
			uimanager.InteractionModes.ModeTalk, rayOrigin, rayDir);
	}

	/// <summary>
	/// Dominant grip = Get. Press: pick up from world or inventory. Release: place on HUD or throw/drop.
	/// </summary>
	static void ApplyDominantGetGripInput(
		XRController3D dominant,
		Vector3 rayOrigin,
		Vector3 rayDir,
		bool hudHovering)
	{
		var gripPressed = IsButtonPressed(dominant, HudRightClickActions);
		var gripReleased = !gripPressed && _dominantGripWasPressed;

		if (gripPressed && !_dominantGripWasPressed)
		{
			if (!IsVrInCombat() && playerdat.ObjectInHand == -1)
			{
				if (hudHovering)
				{
					TryPickupObjectFromHud();
				}
				else
				{
					TryInteractLaserVerb(rayOrigin, rayDir, uimanager.InteractionModes.ModePickup);
				}
			}
		}
		else if (gripReleased && playerdat.ObjectInHand != -1)
		{
			TryReleaseHeldGetObject(rayOrigin, rayDir, hudHovering);
		}

		_dominantGripWasPressed = gripPressed;
	}

	/// <summary>Grip release while holding: inventory/HUD place if laser on UI, else world throw/drop.</summary>
	static void TryReleaseHeldGetObject(Vector3 rayOrigin, Vector3 rayDir, bool hudHovering)
	{
		if (hudHovering)
		{
			TryGetClickOnHud();
			return;
		}

		TryThrowHeldObject(rayOrigin, rayDir);
	}

	static void TryPickupObjectFromHud() => TryGetClickOnHud();

	/// <summary>
	/// Dominant trigger = Use. Fires on <b>release</b> (DOS-aligned), world or inventory.
	/// </summary>
	static void ApplyDominantUseTriggerInput(
		XRController3D dominant,
		Vector3 rayOrigin,
		Vector3 rayDir,
		bool hudHovering)
	{
		var triggerPressed = IsButtonPressed(dominant, HudLeftClickActions);
		var triggerReleased = !triggerPressed && _dominantTriggerWasPressed;

		if (triggerReleased && !IsVrInCombat())
		{
			if (hudHovering)
			{
				TryUseClickOnHud();
			}
			else
			{
				TryInteractLaserVerb(rayOrigin, rayDir, uimanager.InteractionModes.ModeUse);
			}
		}

		_dominantTriggerWasPressed = triggerPressed;
	}

	/// <summary>
	/// Use on inventory HUD only (other HUD chrome still uses press-to-click).
	/// </summary>
	static void TryUseClickOnHud()
	{
		if (_statusOverlayHovering
			&& _statusOverlayHoverKind == VrStatusWidgetKind.Inventory
			&& _lastStatusOverlayHudPos.X >= 0f)
		{
			PushVrHudUseClick(_lastStatusOverlayHudPos);
			return;
		}

		if (_hudPointerHovering
			&& _lastHudPointerPos.X >= 0f
			&& GetInventoryHudRectFixed().HasPoint(_lastHudPointerPos))
		{
			PushVrHudUseClick(_lastHudPointerPos);
		}
	}

	/// <summary>
	/// Get verb on HUD: ModePickup left-click at the laser UV (pick from slot or place into slot).
	/// </summary>
	static void TryGetClickOnHud()
	{
		if (_statusOverlayHovering && _lastStatusOverlayHudPos.X >= 0f)
		{
			PushVrHudGetClick(_lastStatusOverlayHudPos);
			return;
		}

		if (_hudPointerHovering && _lastHudPointerPos.X >= 0f)
		{
			PushVrHudGetClick(_lastHudPointerPos);
		}
	}

	static bool TryConsumeCombatBlockedVerbPress(XRController3D dominant, XRController3D offHand)
	{
		// Dominant grip/trigger are owned by Get/Use handlers — do not overwrite their edge state.
		var anyPressed =
			(IsButtonPressed(offHand, HudLeftClickActions) && !_offHandTriggerWasPressed)
			|| (IsButtonPressed(offHand, HudRightClickActions) && !_offHandGripWasPressed);

		_offHandTriggerWasPressed = IsButtonPressed(offHand, HudLeftClickActions);
		_offHandGripWasPressed = IsButtonPressed(offHand, HudRightClickActions);
		return anyPressed;
	}

	static void TryVerbButtonEdge(
		XRController3D controller,
		StringName[] actions,
		ref bool wasPressed,
		uimanager.InteractionModes verb,
		Vector3 rayOrigin,
		Vector3 rayDir)
	{
		if (controller == GetOffHandController()
			&& controller == _leftController
			&& ShouldOfferVrEscapeGrip()
			&& IsButtonPressed(controller, HudRightClickActions))
		{
			wasPressed = true;
			return;
		}

		var pressed = IsButtonPressed(controller, actions);
		if (pressed && !wasPressed && !IsVrInCombat())
		{
			TryInteractLaserVerb(rayOrigin, rayDir, verb);
		}

		wasPressed = pressed;
	}

	static void ResetExplorationVerbPressState()
	{
		_dominantGripWasPressed = false;
		_dominantTriggerWasPressed = false;
		_offHandGripWasPressed = false;
		_offHandTriggerWasPressed = false;
	}

	static void ResetSpellTargetingPressState()
	{
		_spellCastTriggerWasPressed = false;
		_spellCastGripWasPressed = false;
	}

	static void CancelArmedSpell()
	{
		SpellCasting.currentSpell = null;
		for (var i = 0; i < 3; i++)
		{
			playerdat.SetSelectedRune(i, 24);
		}

		playerdat.NoOfSelectedRunes = 0;
		uimanager.RedrawSelectedRuneSlots();
	}

	static void UpdateViewPortMouseFromControllerAim(Vector3 rayDir)
	{
		var local = _xrCamera.GlobalTransform.Basis.Inverse() * rayDir;
		var vp = uimanager.instance.uwviewport;
		var xNorm = Mathf.Clamp(0.5f + local.X * 1.2f, 0f, 1f);
		var yNorm = Mathf.Clamp(0.5f - local.Y * 1.2f, 0f, 1f);
		uimanager.SetViewPortMouseFromUwLocal(new Vector2(xNorm * vp.Size.X, yNorm * vp.Size.Y));
	}

	/// <summary>Map controller ray to flat throw/drop mouse coords via the game camera frustum.</summary>
	static void UpdateViewPortMouseFromControllerRay(Vector3 rayOrigin, Vector3 rayDir)
	{
		var cam = uimanager.instance?.cam;
		if (cam == null)
		{
			UpdateViewPortMouseFromControllerAim(rayDir);
			return;
		}

		rayDir = rayDir.Normalized();
		var forward = -cam.GlobalTransform.Basis.Z;
		var denom = forward.Dot(rayDir);
		Vector3 aimPoint;
		if (Mathf.Abs(denom) < 1e-5f)
		{
			aimPoint = rayOrigin + rayDir * 2f;
		}
		else
		{
			var t = forward.Dot(cam.GlobalPosition - rayOrigin) / denom;
			if (t < 0.05f)
			{
				t = 2f;
			}

			aimPoint = rayOrigin + rayDir * t;
		}

		var screen = cam.UnprojectPosition(aimPoint);
		uimanager.SetViewPortMouseFromUwLocal(screen);
	}

	/// <summary>Called when an object is picked up via the VR laser (stores ray distance).</summary>
	public static void NotifyObjectPickedUp(int objectIndex, float rayDistance)
	{
		if (!IsActive || objectIndex <= 0)
		{
			return;
		}

		_vrHeldObjectIndex = objectIndex;
		_vrHeldRayDistance = Mathf.Clamp(rayDistance, 0.15f, GetMaxReachAlongRay(
			GetAimRayOrigin(),
			GetControllerRayDir()));
	}

	/// <summary>Held object visual when taking an item out of inventory (fixed laser depth).</summary>
	public static void NotifyObjectPickedUpFromInventory(int objectIndex)
	{
		NotifyObjectPickedUp(objectIndex, InventoryHeldRayDistance);
	}

	public static float GetPendingPickupRayDistance() => _pendingPickupRayDistance;

	static void ClearHeldObjectVisual()
	{
		_vrHeldObjectIndex = -1;
		_pendingPickupRayDistance = 0f;
	}

	static void SetHeldObjectNodeVisible(bool visible)
	{
		if (_vrHeldObjectIndex <= 0)
		{
			return;
		}

		var objList = UWTileMap.current_tilemap?.LevelObjects;
		if (objList == null || _vrHeldObjectIndex >= objList.Length)
		{
			return;
		}

		var obj = objList[_vrHeldObjectIndex];
		var node = obj?.instance?.uwnode;
		if (node != null)
		{
			node.Visible = visible;
		}
	}

	static void UpdateHeldObjectVisual()
	{
		if (!IsActive || !uimanager.InGame || playerdat.ObjectInHand == -1)
		{
			if (_vrHeldObjectIndex != -1)
			{
				ClearHeldObjectVisual();
			}

			return;
		}

		if (_vrHeldObjectIndex != playerdat.ObjectInHand)
		{
			_vrHeldObjectIndex = playerdat.ObjectInHand;
			if (_pendingPickupRayDistance > 0.15f)
			{
				_vrHeldRayDistance = _pendingPickupRayDistance;
			}
		}

		if (GetAimController() == null)
		{
			return;
		}

		// HUD panel already shows the held sprite while the laser is over it.
		if (_hudPointerHovering || _statusOverlayHovering)
		{
			SetHeldObjectNodeVisible(false);
			return;
		}

		var objList = UWTileMap.current_tilemap?.LevelObjects;
		if (objList == null || _vrHeldObjectIndex <= 0 || _vrHeldObjectIndex >= objList.Length)
		{
			return;
		}

		var obj = objList[_vrHeldObjectIndex];
		if (obj.instance == null)
		{
			obj.tileX = 99;
			obj.tileY = 99;
			objectInstance.RedrawFull(obj);
		}

		var node = obj?.instance?.uwnode;
		if (node == null)
		{
			return;
		}

		var rayOrigin = GetAimRayOrigin();
		var rayDir = GetControllerRayDir();
		var holdPos = rayOrigin + rayDir * _vrHeldRayDistance;
		node.Visible = true;
		node.GlobalPosition = holdPos;
		if (_xrCamera != null)
		{
			node.LookAt(_xrCamera.GlobalPosition, Vector3.Up);
		}
	}

	static float GetCanReachWorldRadius(uimanager.InteractionModes mode)
	{
		var threshold = mode == uimanager.InteractionModes.ModePickup
			? playerdat.PickupDistance
			: playerdat.UseDistance;
		if (threshold <= 0)
		{
			// Telekinesis / unlimited — match flat raycast length (world metres, not × tile width).
			var rayDist = uimanager.RayDistance;
			return rayDist > 0f ? rayDist : 3f;
		}

		// CanReach compares x²+y² to threshold in UW sub-tile units (8 per tile).
		return Mathf.Sqrt(threshold) * (tileMapRender.TileWidth / 8f);
	}

	static float GetInteractRayDistance(uimanager.InteractionModes mode)
	{
		switch (mode)
		{
			case uimanager.InteractionModes.ModeLook:
				return GetLookVisionWorldDistance();
			case uimanager.InteractionModes.ModeTalk:
				return uimanager.RayDistance > 0f ? uimanager.RayDistance : 8f;
			case uimanager.InteractionModes.ModeAttack:
				return uimanager.RayDistance > 0f ? uimanager.RayDistance : 1f;
			default:
				return GetCanReachWorldRadius(mode);
		}
	}

	static float GetInteractRayDistance() => GetInteractRayDistance(uimanager.InteractionMode);

	/// <summary>Avatar torso center — same anchor as the cyan body marker.</summary>
	static Vector3 GetAvatarBodyCenter()
	{
		var px = motion.playerMotionParams.x_0;
		var py = motion.playerMotionParams.y_2;
		var pz = motion.playerMotionParams.z_4;
		var displayFloor = GetDisplayFloorPos();
		var simFeet = uwObject.XYZToVector3(px, py, pz);
		var eye = uwObject.XYZToVector3(px, py, pz + 0xA4);
		var bodyHeight = Mathf.Max(0.2f, eye.Y - simFeet.Y);
		return displayFloor + Vector3.Up * (bodyHeight * 0.5f);
	}

	/// <summary>How far the controller laser may extend along a ray before exceeding pick reach from the avatar.</summary>
	static float GetMaxReachAlongRay(Vector3 rayOrigin, Vector3 rayDir, float maxReach)
	{
		rayDir = rayDir.Normalized();
		var offset = rayOrigin - GetAvatarBodyCenter();
		var alongDir = offset.Dot(rayDir);
		var c = offset.LengthSquared() - maxReach * maxReach;
		var discriminant = alongDir * alongDir - c;
		if (discriminant < 0f)
		{
			return 0.01f;
		}

		var sqrtDisc = Mathf.Sqrt(discriminant);
		var t = -alongDir + sqrtDisc;
		if (t <= 0.01f)
		{
			t = -alongDir - sqrtDisc;
		}

		return Mathf.Max(0.01f, t);
	}

	static float GetMaxReachAlongRay(Vector3 rayOrigin, Vector3 rayDir) =>
		GetMaxReachAlongRay(rayOrigin, rayDir, GetInteractRayDistance());

	static float GetPointerLaserVisualDistance()
	{
		// Attack reach is very short, but a longer beam helps aim; other modes match pick reach.
		if (uimanager.InteractionMode == uimanager.InteractionModes.ModeAttack)
		{
			return GetLookVisionWorldDistance();
		}

		return GetInteractRayDistance();
	}

	/// <summary>Gameplay laser: stable visual length; picking still uses interaction reach.</summary>
	static void UpdateGameplayPointerLaser(Vector3 rayOrigin, Vector3 rayDir)
	{
		rayDir = rayDir.Normalized();
		var laserT = GetMaxReachAlongRay(rayOrigin, rayDir, GetPointerLaserVisualDistance());
		UpdatePointerLaser(rayOrigin, rayOrigin + rayDir * laserT, visible: true);
	}

	static void UpdateVrGameplayPointerLaser()
	{
		// Menu/HUD/number-pad lasers are drawn by their own input handlers.
		if (HudPointerOwnsLaser())
		{
			return;
		}

		var aimController = GetAimController();
		if (aimController == null)
		{
			return;
		}

		if (!ShouldShowVrGameplayPointerLaser())
		{
			UpdatePointerLaser(Vector3.Zero, Vector3.Zero, false);
			return;
		}

		var rayOrigin = GetAimRayOrigin();
		var rayDir = GetControllerRayDir();

		if (_statusOverlayHovering)
		{
			UpdatePointerLaser(rayOrigin, _statusOverlayHitWorld, visible: true);
			return;
		}

		if (!uimanager.InGame || (uimanager.blockinput && SpellCasting.currentSpell == null))
		{
			return;
		}

		UpdateGameplayPointerLaser(rayOrigin, rayDir);
	}

	static void TryThrowHeldObject(Vector3 rayOrigin, Vector3 rayDir)
	{
		if (playerdat.ObjectInHand == -1)
		{
			return;
		}

		rayDir = rayDir.Normalized();
		var objToThrow = UWTileMap.current_tilemap.LevelObjects[playerdat.ObjectInHand];
		var itemid = objToThrow.item_id;
		if (pickup.DropObjectByPlayer(objToThrow, true, rayDir))
		{
			playerdat.ObjectInHand = -1;
			ClearHeldObjectVisual();
			uimanager.instance.mousecursor.SetCursorToCursor();
			pickup.DropSpecialCases(itemid);
		}
	}

	static void TryInteractLaserVerb(Vector3 rayOrigin, Vector3 rayDir, uimanager.InteractionModes verb)
	{
		if (!uimanager.InGame)
		{
			return;
		}

		if (SpellCasting.currentSpell != null)
		{
			if (SpellCasting.currentSpell.SpellMajorClass == 5)
			{
				SpellCasting.CastMagicProjectile(playerdat.playerObject, SpellCasting.currentSpell.SpellMinorClass);
				return;
			}
		}

		if (playerdat.ObjectInHand != -1 && verb != uimanager.InteractionModes.ModeUse)
		{
			return;
		}

		if (verb == uimanager.InteractionModes.ModeLook)
		{
			UpdateVisionFromHead(
				(short)playerdat.playerObject.tileX,
				(short)playerdat.playerObject.tileY,
				GetHeadYawForVision());
		}

		rayDir = rayDir.Normalized();
		var maxDist = GetMaxReachAlongRay(rayOrigin, rayDir, GetInteractRayDistance(verb));
		var bestT = maxDist;
		var bestObjectIndex = 0;
		var bestTileFace = 0;
		var bestTileX = 0;
		var bestTileY = 0;
		var bestTileHitPos = Vector3.Zero;
		var bestPick = LaserPickKind.None;

		if (TryPickClosestObjectAlongRay(rayOrigin, rayDir, maxDist, out var geoT, out var geoIndex))
		{
			bestT = geoT;
			bestObjectIndex = geoIndex;
			bestPick = LaserPickKind.Object;
		}

		if (TryPickClosestTileSurfaceAlongRay(rayOrigin, rayDir, maxDist, out var tileT, out var tileFace, out var tileX, out var tileY, out var tileHitPos))
		{
			if (tileT < bestT)
			{
				bestT = tileT;
				bestTileFace = tileFace;
				bestTileX = tileX;
				bestTileY = tileY;
				bestTileHitPos = tileHitPos;
				bestObjectIndex = 0;
				bestPick = LaserPickKind.Tile;
			}
		}

		if (TryPhysicsRayPick(rayOrigin, rayDir, maxDist, out var physT, out var physIndex, out _))
		{
			if (physIndex > 0 && physT < bestT)
			{
				bestT = physT;
				bestObjectIndex = physIndex;
				bestPick = LaserPickKind.Object;
			}
		}

		switch (bestPick)
		{
			case LaserPickKind.Object:
				_pendingPickupRayDistance = bestT;
				InteractWithLaserObject(bestObjectIndex, verb, rayOrigin + rayDir * bestT);
				return;
			case LaserPickKind.Tile:
				InteractWithLaserTile(bestTileFace, bestTileX, bestTileY, bestTileHitPos, verb);
				return;
		}

		if (verb == uimanager.InteractionModes.ModeLook)
		{
			SayYouSeeNothing();
		}
	}

	enum LaserPickKind
	{
		None,
		Object,
		Tile,
	}

	static void SayYouSeeNothing()
	{
		uimanager.AddToMessageScroll(GameStrings.GetString(1, GameStrings.str_you_see_nothing_));
	}

	static float GetLookVisionWorldDistance()
	{
		var visionTiles = VisionParams.DistanceToWallOrDarkness;
		if (visionTiles < 0)
		{
			var lookRay = uimanager.RayDistance;
			return (lookRay > 0f ? lookRay : 3f) * tileMapRender.TileWidth;
		}

		return (visionTiles + 1) * tileMapRender.TileWidth;
	}

	static bool IsWithinLookRange(Vector3 hitPos)
	{
		var origin = _xrCamera?.GlobalPosition ?? Vector3.Zero;
		return origin.DistanceTo(hitPos) <= GetLookVisionWorldDistance() + 0.05f;
	}

	static void InteractWithLaserObject(int index, uimanager.InteractionModes verb, Vector3 hitPos)
	{
		var objList = UWTileMap.current_tilemap?.LevelObjects;
		if (objList == null || index <= 0 || index >= objList.Length)
		{
			return;
		}

		if (verb == uimanager.InteractionModes.ModeLook
			&& !IsWithinLookRange(hitPos))
		{
			SayYouSeeNothing();
			return;
		}

		if (SpellCasting.currentSpell != null)
		{
			SpellCasting.CastCurrentSpellOnRayCastTarget(index, objList, WorldObject: true);
			return;
		}

		uimanager.PerformVrObjectInteraction(index, verb);
	}

	static void InteractWithLaserTile(int face, int tileX, int tileY, Vector3 hitPos, uimanager.InteractionModes verb)
	{
		if (!UWTileMap.ValidTile(tileX, tileY))
		{
			if (verb == uimanager.InteractionModes.ModeLook)
			{
				SayYouSeeNothing();
			}

			return;
		}

		if (verb == uimanager.InteractionModes.ModeLook)
		{
			if (!IsWithinLookRange(hitPos))
			{
				SayYouSeeNothing();
				return;
			}

			uimanager.LookAtTile(face, tileX, tileY);
			return;
		}

		if (SpellCasting.currentSpell != null)
		{
			TryObjectInfoAtWorldPoint(hitPos, leftClick: true);
		}
	}

	static bool TryObjectInfoAtWorldPoint(Vector3 worldPoint, bool leftClick)
	{
		SyncVrObjectInfoCamera();
		if (!TryUnprojectToObjectInfoPixel(worldPoint, out var pixel))
		{
			return false;
		}

		uimanager.SetViewPortMouseFromObjectInfoPixel(pixel);
		uimanager.ProcessObjectInfoPixel(pixel, leftClick);
		return true;
	}

	static bool TryPickClosestObjectAlongRay(Vector3 rayOrigin, Vector3 rayDir, float maxDist, out float bestT, out int bestIndex)
	{
		bestT = maxDist;
		bestIndex = 0;

		var objList = UWTileMap.current_tilemap?.LevelObjects;
		if (objList == null)
		{
			return false;
		}

		var found = false;
		for (var i = 1; i < objList.Length; i++)
		{
			var obj = objList[i];
			if (obj == null || obj.index != i || obj.invis != 0 || obj.instance?.uwnode == null)
			{
				continue;
			}

			if (!IsObjectNearRay(obj, rayOrigin, rayDir, maxDist))
			{
				continue;
			}

			if (!TryPickObjectMeshes(obj.instance.uwnode, rayOrigin, rayDir, out var hitT))
			{
				continue;
			}

			if (hitT <= 0.01f || hitT >= bestT)
			{
				continue;
			}

			bestT = hitT;
			bestIndex = obj.index;
			found = true;
		}

		return found;
	}

	static bool IsObjectNearRay(uwObject obj, Vector3 rayOrigin, Vector3 rayDir, float maxDist)
	{
		var objPos = obj.GetCoordinate();
		var along = (objPos - rayOrigin).Dot(rayDir);
		if (along < 0f || along > maxDist + 1f)
		{
			return false;
		}

		var closest = rayOrigin + rayDir * along;
		return closest.DistanceSquaredTo(objPos) <= 2.5f;
	}

	static bool TryPickObjectMeshes(Node3D node, Vector3 rayOrigin, Vector3 rayDir, out float bestT)
	{
		bestT = float.MaxValue;
		var found = false;
		TryPickMeshesRecursive(node, rayOrigin, rayDir, ref bestT, ref found);
		return found;
	}

	static void TryPickMeshesRecursive(Node node, Vector3 rayOrigin, Vector3 rayDir, ref float bestT, ref bool found)
	{
		if (node is MeshInstance3D mesh && mesh.Visible && mesh.Mesh != null)
		{
			if (TryRayIntersectMesh(mesh, rayOrigin, rayDir, out var hitT) && hitT > 0.01f && hitT < bestT)
			{
				bestT = hitT;
				found = true;
			}
		}

		foreach (var child in node.GetChildren())
		{
			TryPickMeshesRecursive(child, rayOrigin, rayDir, ref bestT, ref found);
		}
	}

	static bool TryRayIntersectMesh(MeshInstance3D mesh, Vector3 rayOrigin, Vector3 rayDir, out float t)
	{
		if (mesh.Mesh is QuadMesh quad)
		{
			return TryRayIntersectQuad(mesh, quad, rayOrigin, rayDir, out t);
		}

		return TryRayIntersectMeshAabb(mesh, rayOrigin, rayDir, out t);
	}

	static bool TryRayIntersectQuad(MeshInstance3D mesh, QuadMesh quad, Vector3 rayOrigin, Vector3 rayDir, out float t)
	{
		t = float.MaxValue;
		var xf = mesh.GlobalTransform;
		var planeNormal = xf.Basis.Z.Normalized();
		var denom = planeNormal.Dot(rayDir);
		if (Mathf.Abs(denom) < 0.000001f)
		{
			return false;
		}

		t = (xf.Origin - rayOrigin).Dot(planeNormal) / denom;
		if (t <= 0.01f)
		{
			return false;
		}

		var hitPoint = rayOrigin + rayDir * t;
		var local = xf.AffineInverse() * hitPoint;
		var halfW = quad.Size.X * 0.5f;
		var halfH = quad.Size.Y * 0.5f;
		if (Mathf.Abs(local.X) > halfW || Mathf.Abs(local.Y) > halfH)
		{
			return false;
		}

		return true;
	}

	static bool TryRayIntersectMeshAabb(MeshInstance3D mesh, Vector3 rayOrigin, Vector3 rayDir, out float t)
	{
		t = float.MaxValue;
		var inv = mesh.GlobalTransform.AffineInverse();
		var localOrigin = inv * rayOrigin;
		var localDir = inv.Basis * rayDir;
		if (localDir.LengthSquared() < 0.000001f)
		{
			return false;
		}

		localDir = localDir.Normalized();
		var aabb = mesh.GetAabb();
		if (!TryRayIntersectLocalAabb(aabb, localOrigin, localDir, out var localT))
		{
			return false;
		}

		var hitLocal = localOrigin + localDir * localT;
		var hitWorld = mesh.GlobalTransform * hitLocal;
		t = rayOrigin.DistanceTo(hitWorld);
		return t > 0.01f;
	}

	static bool TryRayIntersectLocalAabb(Aabb aabb, Vector3 origin, Vector3 dir, out float tEnter)
	{
		tEnter = 0f;
		var tExit = float.MaxValue;
		var min = aabb.Position;
		var max = aabb.Position + aabb.Size;

		if (!TryRaySlab(origin.X, dir.X, min.X, max.X, ref tEnter, ref tExit)
			|| !TryRaySlab(origin.Y, dir.Y, min.Y, max.Y, ref tEnter, ref tExit)
			|| !TryRaySlab(origin.Z, dir.Z, min.Z, max.Z, ref tEnter, ref tExit))
		{
			return false;
		}

		return tExit >= 0f && tEnter <= tExit;
	}

	static bool TryRaySlab(float origin, float dir, float min, float max, ref float tEnter, ref float tExit)
	{
		if (Mathf.Abs(dir) < 0.000001f)
		{
			return origin >= min && origin <= max;
		}

		var invDir = 1f / dir;
		var t0 = (min - origin) * invDir;
		var t1 = (max - origin) * invDir;
		if (t0 > t1)
		{
			(t0, t1) = (t1, t0);
		}

		tEnter = Mathf.Max(tEnter, t0);
		tExit = Mathf.Min(tExit, t1);
		return tEnter <= tExit;
	}

	static bool TryPickClosestTileSurfaceAlongRay(Vector3 rayOrigin, Vector3 rayDir, float maxDist, out float bestT, out int face, out int tileX, out int tileY, out Vector3 hitPos)
	{
		bestT = maxDist;
		face = 0;
		tileX = 0;
		tileY = 0;
		hitPos = default;

		var root = tileMapRender.worldnode;
		if (root == null)
		{
			return false;
		}

		var found = false;
		foreach (var child in root.GetChildren())
		{
			if (child is MeshInstance3D mesh && mesh.Visible)
			{
				TryPickTileMesh(mesh, rayOrigin, rayDir, maxDist, ref bestT, ref face, ref tileX, ref tileY, ref hitPos, ref found);
			}
		}

		return found;
	}

	static void TryPickTileMesh(MeshInstance3D meshInst, Vector3 rayOrigin, Vector3 rayDir, float maxDist, ref float bestT, ref int bestFace, ref int bestTileX, ref int bestTileY, ref Vector3 bestHitPos, ref bool found)
	{
		if (meshInst.Mesh is not ArrayMesh mesh || mesh.GetSurfaceCount() == 0)
		{
			return;
		}

		var inv = meshInst.GlobalTransform.AffineInverse();
		var localOrigin = inv * rayOrigin;
		var localDir = inv.Basis * rayDir;
		if (localDir.LengthSquared() < 0.000001f)
		{
			return;
		}

		localDir = localDir.Normalized();
		var meshAabb = mesh.GetAabb();
		if (!TryRayIntersectLocalAabb(meshAabb, localOrigin, localDir, out _))
		{
			return;
		}

		for (var surface = 0; surface < mesh.GetSurfaceCount(); surface++)
		{
			if (mesh.SurfaceGetMaterial(surface) is not ShaderMaterial mat)
			{
				continue;
			}

			var tileFace = GetShaderIntParam(mat, "tileflags");
			var surfaceTileX = GetShaderIntParam(mat, "objectindex_lowerbytes");
			var surfaceTileY = GetShaderIntParam(mat, "objectindex_upperbytes");
			if (tileFace == 0 && surfaceTileX == 0 && surfaceTileY == 0)
			{
				continue;
			}

			var arrays = mesh.SurfaceGetArrays(surface);
			if (arrays.Count <= (int)Mesh.ArrayType.Vertex)
			{
				continue;
			}

			var verts = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
			if (verts.Length < 3)
			{
				continue;
			}

			var indices = arrays[(int)Mesh.ArrayType.Index];
			if (indices.VariantType != Variant.Type.Nil)
			{
				var indexArray = indices.AsInt32Array();
				for (var i = 0; i + 2 < indexArray.Length; i += 3)
				{
					TryPickTileTriangle(
						verts[indexArray[i]], verts[indexArray[i + 1]], verts[indexArray[i + 2]],
						meshInst.GlobalTransform, rayOrigin, rayDir, maxDist,
						tileFace, surfaceTileX, surfaceTileY,
						localOrigin, localDir,
						ref bestT, ref bestFace, ref bestTileX, ref bestTileY, ref bestHitPos, ref found);
				}
			}
			else
			{
				for (var i = 0; i + 2 < verts.Length; i += 3)
				{
					TryPickTileTriangle(
						verts[i], verts[i + 1], verts[i + 2],
						meshInst.GlobalTransform, rayOrigin, rayDir, maxDist,
						tileFace, surfaceTileX, surfaceTileY,
						localOrigin, localDir,
						ref bestT, ref bestFace, ref bestTileX, ref bestTileY, ref bestHitPos, ref found);
				}
			}
		}
	}

	static void TryPickTileTriangle(
		Vector3 v0, Vector3 v1, Vector3 v2,
		Transform3D meshTransform, Vector3 rayOrigin, Vector3 rayDir, float maxDist,
		int tileFace, int surfaceTileX, int surfaceTileY,
		Vector3 localOrigin, Vector3 localDir,
		ref float bestT, ref int bestFace, ref int bestTileX, ref int bestTileY, ref Vector3 bestHitPos, ref bool found)
	{
		if (!TryRayIntersectTriangle(localOrigin, localDir, v0, v1, v2, out var localT))
		{
			return;
		}

		var localHit = localOrigin + localDir * localT;
		var worldHit = meshTransform * localHit;
		var worldT = rayOrigin.DistanceTo(worldHit);
		if (worldT <= 0.01f || worldT >= bestT || worldT > maxDist)
		{
			return;
		}

		bestT = worldT;
		bestFace = tileFace;
		bestTileX = surfaceTileX;
		bestTileY = surfaceTileY;
		bestHitPos = worldHit;
		found = true;
	}

	static int GetShaderIntParam(ShaderMaterial mat, string name)
	{
		var value = mat.GetShaderParameter(name);
		if (value.VariantType == Variant.Type.Int)
		{
			return value.AsInt32();
		}

		if (value.VariantType == Variant.Type.Float)
		{
			return (int)value.AsDouble();
		}

		return 0;
	}

	static bool TryRayIntersectTriangle(Vector3 origin, Vector3 dir, Vector3 v0, Vector3 v1, Vector3 v2, out float t)
	{
		t = float.MaxValue;
		var edge1 = v1 - v0;
		var edge2 = v2 - v0;
		var pvec = dir.Cross(edge2);
		var det = edge1.Dot(pvec);
		if (Mathf.Abs(det) < 0.000001f)
		{
			return false;
		}

		var invDet = 1f / det;
		var tvec = origin - v0;
		var u = tvec.Dot(pvec) * invDet;
		if (u < 0f || u > 1f)
		{
			return false;
		}

		var qvec = tvec.Cross(edge1);
		var v = dir.Dot(qvec) * invDet;
		if (v < 0f || u + v > 1f)
		{
			return false;
		}

		t = edge2.Dot(qvec) * invDet;
		return t > 0.0001f;
	}

	static bool TryPhysicsRayPick(Vector3 rayOrigin, Vector3 rayDir, float maxDist, out float t, out int objectIndex, out Vector3 hitPos)
	{
		t = maxDist;
		objectIndex = 0;
		hitPos = default;
		if (_gameRoot == null)
		{
			return false;
		}

		var to = rayOrigin + rayDir * maxDist;
		var query = PhysicsRayQueryParameters3D.Create(rayOrigin, to);
		query.CollideWithAreas = true;
		query.CollideWithBodies = true;
		query.CollisionMask = uint.MaxValue;
		var result = _gameRoot.GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (result.Count == 0 || !result.ContainsKey("collider"))
		{
			return false;
		}

		hitPos = result.ContainsKey("position") ? result["position"].AsVector3() : to;
		t = rayOrigin.DistanceTo(hitPos);

		if (result["collider"].AsGodotObject() is not Node node)
		{
			return true;
		}

		while (node != null)
		{
			if (TryGetObjectIndex(node.Name, out var index))
			{
				objectIndex = index;
				break;
			}

			node = node.GetParent();
		}

		return true;
	}

	static Vector3 GetControllerRayDir() => GetRayDirFromController(GetAimController());

	static bool TryUnprojectToObjectInfoPixel(Vector3 worldPoint, out Vector2 texPixel)
	{
		texPixel = default;
		if (_xrCamera == null || uimanager.instance?.uwsubviewport_objectinfo == null)
		{
			return false;
		}

		var screen = _xrCamera.UnprojectPosition(worldPoint);
		var vpSize = _xrCamera.GetViewport().GetVisibleRect().Size;
		if (screen.X < 0f || screen.Y < 0f || screen.X > vpSize.X || screen.Y > vpSize.Y)
		{
			return false;
		}

		var uwSize = uimanager.instance.uwsubviewport_objectinfo.Size;
		texPixel = new Vector2(
			screen.X / Mathf.Max(1f, vpSize.X) * uwSize.X,
			screen.Y / Mathf.Max(1f, vpSize.Y) * uwSize.Y);
		return true;
	}

	static bool ShouldUseHudMenuPointerOnly()
	{
		return uimanager.blockinput || IsHudOnMenuScreen() || uimanager.AtMainMenu
			|| uimanager.CurrentGameMode == uimanager.GameModes.CUTSCENE;
	}

	static void ApplyHudPointerInput()
	{
		IsHud3DViewportHovering = false;
		IsHud3DViewportRightHeld = false;

		if (!uwsettings.instance.vr)
		{
			IntroDiagLogOnce(ref _introDiagLastLaserSkipReason, "hud-ptr-vr-off", "ApplyHudPointerInput: vr disabled.");
			_hudPointerHovering = false;
			_hudPointerLeftWasPressed = false;
			_hudPointerRightWasPressed = false;
			return;
		}

		var needsMenuTv = IsHudOnMenuScreen() || NeedsFrontMenuLaser();
		if (needsMenuTv)
		{
			TryEnsureMenuTvScreen();
		}

		var menuScreen = IsHudOnMenuScreen() || (NeedsFrontMenuLaser() && _hudPanel != null);
		var menuPointer = GetMenuPointerController();

		if (uwsettings.instance.vr_mirror || _hudViewport == null)
		{
			IntroDiagLog(
				$"ApplyHudPointerInput bail: mirror={uwsettings.instance.vr_mirror} hudVp={_hudViewport != null} "
				+ $"hudPanel={_hudPanel != null} menuPtr={menuPointer != null} menuScreen={menuScreen}");
			_hudPointerHovering = false;
			_hudPointerLeftWasPressed = false;
			_hudPointerRightWasPressed = false;
			if (_hudMouseLayer != null)
			{
				_hudMouseLayer.Visible = false;
			}

			if (!menuScreen)
			{
				UpdatePointerLaser(Vector3.Zero, Vector3.Zero, false);
			}

			return;
		}

		if (menuScreen)
		{
			ApplyMenuTvPointerInput();
			return;
		}

		if (_hudPanel == null)
		{
			IntroDiagLog(
				$"ApplyHudPointerInput bail: hand HUD missing panel hudVp={_hudViewport != null} menuPtr={menuPointer != null}");
			_hudPointerHovering = false;
			_hudPointerLeftWasPressed = false;
			_hudPointerRightWasPressed = false;
			if (_hudMouseLayer != null)
			{
				_hudMouseLayer.Visible = false;
			}

			UpdatePointerLaser(Vector3.Zero, Vector3.Zero, false);
			return;
		}

		if (_headOverlaysVisible && _statusOverlayHovering)
		{
			_hudPointerHovering = false;
			_hudPointerLeftWasPressed = false;
			_hudPointerRightWasPressed = false;
			return;
		}

		if (menuPointer == null)
		{
			_hudPointerHovering = false;
			_hudPointerLeftWasPressed = false;
			_hudPointerRightWasPressed = false;
			UpdatePointerLaser(Vector3.Zero, Vector3.Zero, false);
			return;
		}

		if (!_hudPanelVisible)
		{
			if (ShouldUseHudMenuPointerOnly())
			{
				SetHudPanelVisible(true);
			}
			else
			{
				_hudPointerHovering = false;
				_hudPointerLeftWasPressed = false;
				_hudPointerRightWasPressed = false;
				if (!ShouldShowVrPointerLaser())
				{
					UpdatePointerLaser(Vector3.Zero, Vector3.Zero, false);
				}

				return;
			}
		}

		GetHudPointerRay(menuScreen: false, out var rayOrigin, out var rayDir);
		var menuOnly = ShouldUseHudMenuPointerOnly();
		var pointerMaxDistance = HudPointerMaxDistance;
		var hovering = TryGetHudPanelHit(rayOrigin, rayDir, pointerMaxDistance, out var viewportPos, out var hitWorld);

		if (menuOnly && !hovering)
		{
			var laserDistance = 0.2f;
			UpdatePointerLaser(rayOrigin, rayOrigin + rayDir * laserDistance, visible: true);

			_hudPointerHovering = false;
			_hudPointerLeftWasPressed = false;
			_hudPointerRightWasPressed = false;
			_lastHudPointerPos = new Vector2(-1f, -1f);
			if (_hudMouseLayer != null)
			{
				_hudMouseLayer.Visible = false;
			}

			uimanager.CursorOverMessageScroll = false;
			return;
		}

		if (hovering)
		{
			UpdatePointerLaser(rayOrigin, hitWorld, visible: true);
		}
		else
		{
			UpdatePointerLaser(rayOrigin, rayOrigin + rayDir * pointerMaxDistance, visible: true);
		}

		if (hovering)
		{
			EnsureHudMouseLayer();
			if (_hudMouseLayer != null)
			{
				_hudMouseLayer.Visible = true;
			}

			if (viewportPos != _lastHudPointerPos)
			{
				_lastHudPointerPos = viewportPos;
				PushHudMouseMotion(viewportPos);
			}
		}
		else
		{
			_lastHudPointerPos = new Vector2(-1f, -1f);
			if (_hudMouseLayer != null)
			{
				_hudMouseLayer.Visible = false;
			}
		}

		_hudPointerHovering = hovering;
		UpdateMessageScrollHover(viewportPos, hovering);

		if (!hovering)
		{
			_hudPointerLeftWasPressed = false;
			_hudPointerRightWasPressed = false;
			return;
		}

		if (menuOnly)
		{
			ApplyHudMenuPointerClicks(viewportPos, menuScreen: false);
			return;
		}

		var in3dViewport = TryMapToUwViewport(viewportPos, out var uwLocal);
		if (in3dViewport)
		{
			IsHud3DViewportHovering = true;
			uimanager.SetViewPortMouseFromUwLocal(uwLocal);

			var leftPressed = IsHudPointerLeftClickHeld(menuScreen: false);
			if (leftPressed && !_hudPointerLeftWasPressed)
			{
				SyncVrObjectInfoCamera();
				uimanager.TriggerViewPortClick(uwLocal, leftClick: true);
			}
			_hudPointerLeftWasPressed = leftPressed;

			var rightPressed = IsHudPointerRightClickHeld(menuScreen: false);
			if (rightPressed && !_hudPointerRightWasPressed
				&& uimanager.InteractionMode != uimanager.InteractionModes.ModeAttack)
			{
				SyncVrObjectInfoCamera();
				uimanager.TriggerViewPortClick(uwLocal, leftClick: false);
			}
			_hudPointerRightWasPressed = rightPressed;
		}
		else
		{
			ApplyHudMenuPointerClicks(viewportPos, menuScreen: false);
		}
	}

	static void ApplyHudMenuPointerClicks(Vector2 viewportPos, bool menuScreen)
	{
		var leftPressed = IsHudPointerLeftClickHeld(menuScreen);
		if (leftPressed && !_hudPointerLeftWasPressed)
		{
			if (!TryDismissMessageMore()
				&& !TryConfirmYesNoPrompt(viewportPos, yes: true)
				&& !TrySelectConversationOption(viewportPos))
			{
				// Inventory Use is trigger-release via ApplyDominantUseTriggerInput (DOS-aligned).
				if (menuScreen || !GetInventoryHudRectFixed().HasPoint(viewportPos))
				{
					PushVrHudMouseClick(viewportPos, MouseButton.Left);
				}
			}
		}
		_hudPointerLeftWasPressed = leftPressed;

		var rightPressed = IsHudPointerRightClickHeld(menuScreen);
		if (rightPressed && !_hudPointerRightWasPressed)
		{
			if (!TryDismissMessageMore()
				&& !TryConfirmYesNoPrompt(viewportPos, yes: false)
				&& !TrySelectConversationOption(viewportPos))
			{
				PushHudMouseClick(viewportPos, MouseButton.Right);
			}
		}
		_hudPointerRightWasPressed = rightPressed;
	}

	static bool TryDismissMessageMore()
	{
		if (!MessageDisplay.WaitingForMore)
		{
			return false;
		}

		MessageDisplay.WaitingForMore = false;
		return true;
	}

	static void UpdateMessageScrollHover(Vector2 hudViewportPos, bool hoveringHud)
	{
		var needsScrollHover =
			(uimanager.InConversation && ConversationVM.WaitingForInput)
			|| MessageDisplay.WaitingForYesOrNo;

		uimanager.CursorOverMessageScroll = needsScrollHover
			&& hoveringHud
			&& IsHudPointOverMessageScroll(hudViewportPos);
	}

	static bool TryConfirmYesNoPrompt(Vector2 hudViewportPos, bool yes)
	{
		if (!MessageDisplay.WaitingForYesOrNo || !IsHudPointOverMessageScroll(hudViewportPos))
		{
			return false;
		}

		MessageDisplay.ConfirmYesNoResponse(yes);
		return true;
	}

	static bool IsHudPointOverMessageScroll(Vector2 hudViewportPos)
	{
		var scroll = uimanager.MessageScroll;
		if (scroll == null)
		{
			return false;
		}

		var rect = new Rect2(scroll.Position, scroll.Size);
		return rect.HasPoint(hudViewportPos);
	}

	static bool TrySelectConversationOption(Vector2 hudViewportPos)
	{
		if (!uimanager.InConversation || !ConversationVM.WaitingForInput || uimanager.MessageScrollIsTemporary)
		{
			return false;
		}

		var scroll = uimanager.MessageScroll;
		if (scroll == null)
		{
			return false;
		}

		var localClick = hudViewportPos - scroll.Position;
		if (localClick.X < 0f || localClick.Y < 0f
			|| localClick.X > scroll.Size.X || localClick.Y > scroll.Size.Y)
		{
			return false;
		}

		var lineCount = Math.Max(1, scroll.GetLineCount());
		var lineHeight = scroll.Size.Y / lineCount;
		if (lineHeight <= 0f)
		{
			return false;
		}

		var clickedLine = (int)(localClick.Y / lineHeight);
		if (clickedLine < 0 || clickedLine >= lineCount)
		{
			return false;
		}

		var result = clickedLine + 1;
		if (result <= 0 || result > ConversationVM.MaxAnswer)
		{
			return false;
		}

		ConversationVM.PlayerNumericAnswer = result;
		ConversationVM.WaitingForInput = false;
		return true;
	}

	static bool TryMapToUwViewport(Vector2 hudViewportPos, out Vector2 uwLocal)
	{
		uwLocal = default;
		var uwViewport = uimanager.instance?.uwviewport;
		if (uwViewport == null)
		{
			return false;
		}

		uwLocal = hudViewportPos - uwViewport.Position;
		return uwLocal.X >= 0f && uwLocal.Y >= 0f
			&& uwLocal.X <= uwViewport.Size.X && uwLocal.Y <= uwViewport.Size.Y;
	}

	static bool TryGetHudPanelHit(Vector3 rayOrigin, Vector3 rayDir, float maxDistance, out Vector2 viewportPos, out Vector3 hitWorld)
	{
		viewportPos = default;
		hitWorld = rayOrigin + rayDir * maxDistance;
		if (_hudPanel?.Mesh is not QuadMesh quad)
		{
			return false;
		}

		var xf = _hudPanel.GlobalTransform;
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
			Mathf.Clamp(u * HudPanelWidthPx, 0f, HudPanelWidthPx - 1f),
			Mathf.Clamp((1f - v) * HudPanelHeightPx, 0f, HudPanelHeightPx - 1f));
		return true;
	}

	static void UpdatePointerLaser(Vector3 from, Vector3 to, bool visible, bool localSpace = false)
	{
		if (_pointerLaser == null || _pointerLaserMesh == null)
		{
			if (visible && NeedsFrontMenuLaser())
			{
				IntroDiagLog("UpdatePointerLaser: _pointerLaser mesh missing while menu laser requested.");
			}

			return;
		}

		if (localSpace)
		{
			_pointerLaser.TopLevel = false;
		}
		else if (_pointerLaserOnController || _pointerLaserOnCamera)
		{
			ReparentPointerLaserTo(_pointerLaserWorldParent, onCamera: false, PointerLaserRadius);
		}

		_pointerLaser.Visible = visible;
		if (!visible)
		{
			LogLaserVisibilityIfChanged(false, "hidden");
			return;
		}

		var delta = to - from;
		var length = delta.Length();
		if (length < 0.005f)
		{
			_pointerLaser.Visible = false;
			LogLaserVisibilityIfChanged(false, $"beam too short ({length:F4}m)");
			IntroDiagLog($"UpdatePointerLaser: beam too short ({length:F4}m) from=({from.X:F2},{from.Y:F2},{from.Z:F2}) to=({to.X:F2},{to.Y:F2},{to.Z:F2})");
			return;
		}

		var direction = delta / length;
		_pointerLaserMesh.Height = length;
		if (localSpace)
		{
			_pointerLaser.TopLevel = false;
			_pointerLaser.Position = from + direction * (length * 0.5f);
			_pointerLaser.Basis = BasisWithYAxis(direction);
			LogLaserVisibilityIfChanged(true, "controller-local");
		}
		else
		{
			_pointerLaser.TopLevel = true;
			_pointerLaser.GlobalPosition = from + direction * (length * 0.5f);
			_pointerLaser.GlobalBasis = BasisWithYAxis(direction);
			LogLaserVisibilityIfChanged(true, "world");
		}
	}

	/// <summary>CylinderMesh extends along local Y; align Y with <paramref name="axisY"/>.</summary>
	static Basis BasisWithYAxis(Vector3 axisY)
	{
		var yAxis = axisY.Normalized();
		var xAxis = Vector3.Up.Cross(yAxis);
		if (xAxis.LengthSquared() < 1e-6f)
		{
			xAxis = Vector3.Forward.Cross(yAxis);
		}

		xAxis = xAxis.Normalized();
		var zAxis = xAxis.Cross(yAxis);
		return new Basis(xAxis, yAxis, zAxis);
	}

	static void PushHudMouseMotion(Vector2 viewportPos)
	{
		_hudViewport.WarpMouse(viewportPos);
		var motion = new InputEventMouseMotion
		{
			Position = viewportPos,
			GlobalPosition = viewportPos,
		};
		_hudViewport.PushInput(motion);
	}

	static void PushHudMouseClick(Vector2 viewportPos, MouseButton button)
	{
		PushHudMouseButton(viewportPos, button, pressed: true);
		PushHudMouseButton(viewportPos, button, pressed: false);
	}

	/// <summary>
	/// HUD left click from dominant trigger: always Use on inventory (ignore sticky Get/Look modes).
	/// Placement into slots is Get grip-release only.
	/// </summary>
	static void PushVrHudMouseClick(Vector2 viewportPos, MouseButton button)
	{
		if (uwsettings.instance.vr
			&& button == MouseButton.Left
			&& GetInventoryHudRectFixed().HasPoint(viewportPos))
		{
			PushVrHudUseClick(viewportPos);
			return;
		}

		PushHudMouseClick(viewportPos, button);
	}

	/// <summary>
	/// Use on inventory UI: left click under ModeUse (does not follow Hank sticky Get/Look mode).
	/// </summary>
	static void PushVrHudUseClick(Vector2 viewportPos)
	{
		var previous = uimanager.InteractionMode;
		uimanager.InteractionMode = uimanager.InteractionModes.ModeUse;
		PushHudMouseClick(viewportPos, MouseButton.Left);
		uimanager.InteractionMode = previous;
	}

	/// <summary>
	/// Get on inventory UI: left click under ModePickup (pick from occupied slot or place into empty).
	/// </summary>
	static void PushVrHudGetClick(Vector2 viewportPos)
	{
		var previous = uimanager.InteractionMode;
		uimanager.InteractionMode = uimanager.InteractionModes.ModePickup;
		PushHudMouseClick(viewportPos, MouseButton.Left);
		uimanager.InteractionMode = previous;
	}

	static void PushHudMouseButton(Vector2 viewportPos, MouseButton button, bool pressed)
	{
		_hudViewport.WarpMouse(viewportPos);
		var mouseButton = new InputEventMouseButton
		{
			ButtonIndex = button,
			Pressed = pressed,
			Position = viewportPos,
			GlobalPosition = viewportPos,
		};
		_hudViewport.PushInput(mouseButton);
	}

	static bool TryGetObjectIndex(StringName nodeName, out int index)
	{
		index = 0;
		var name = nodeName.ToString();
		var split = name.IndexOf('_');
		if (split <= 0)
		{
			return false;
		}

		return int.TryParse(name.AsSpan(0, split), out index);
	}
}
