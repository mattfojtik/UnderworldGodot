using System;
using Godot;

namespace Underworld;

/// <summary>
/// Minimal VR support: OpenXR head tracking with thumbstick locomotion.
/// Enable via "vr": true in settings.json (user://settings.json).
/// </summary>
public static class VrController
{
	public static bool IsActive { get; private set; }

	/// <summary>Right-hand laser is over the 3D viewport hole in the HUD (not chrome/buttons).</summary>
	public static bool IsHud3DViewportHovering { get; private set; }

	/// <summary>Right grip held while pointing at the 3D viewport (attack charge/release).</summary>
	public static bool IsHud3DViewportRightHeld { get; private set; }

	/// <summary>Controller laser is aimed into the live VR world (not the HUD panel).</summary>
	public static bool IsVrWorldPointerActive { get; private set; }

	/// <summary>Right grip held while aiming into the live VR world.</summary>
	public static bool IsVrWorldRightHeld { get; private set; }

	public const float VrViewDistance = 512f;

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
	static CanvasLayer _hudMouseLayer;
	static MeshInstance3D _pointerLaser;
	static CylinderMesh _pointerLaserMesh;
	static bool _hudPanelVisible = true;
	static bool _hudMenuToggleWasPressed;
	static Vector2 _lastHudPointerPos = new(-1f, -1f);
	static bool _hudPointerHovering;
	static bool _hudPointerLeftWasPressed;
	static bool _hudPointerRightWasPressed;
	static bool _worldPointerLeftWasPressed;
	static bool _worldPointerRightWasPressed;
	static bool _pendingWorldInteract;
	static bool _pendingWorldInteractLeft;
	static Vector3 _pendingRayOrigin;
	static Vector3 _pendingRayDir;
	static int _pendingWorldInteractDelayFrames;
	static main _gameRoot;
	static SceneTree _sceneTree;
	static float _snapTurnCooldown;
	static float _doorUseCooldown;
	static bool _doorUseWasPressed;
	static bool _jumpWasPressed;
	static bool _recenterWasPressed;
	static bool _quitWasPressed;
	static bool _xrOriginFloorInitialized;
	static Vector3 _lastAvatarFloorPos;
	static short _lastSyncedBodyYaw;
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
	const float DoorUseCooldownSeconds = 0.35f;
	const float HudPointerMaxDistance = 2.5f;
	const float PointerLaserRadius = 0.0025f;
	const int HudPanelWidthPx = 1280;
	const int HudPanelHeightPx = 800;
	/// <summary>Left-controller local offset for the HUD quad (metres).</summary>
	static readonly Vector3 HudPanelLocalPosition = new(0.04f, 0.06f, 0.14f);
	/// <summary>Tilt/yaw so the panel faces roughly toward the user from the left grip.</summary>
	static readonly Vector3 HudPanelLocalRotationDegrees = new(-70f, 180f, 180f);
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

	static readonly StringName[] JumpButtonActions =
	{
		"ax_button",
		"a_button",
	};

	// Left-hand ax_button is Quest X; right-hand ax_button is A (jump).
	static readonly StringName[] QuitButtonActions =
	{
		"ax_button",
		"x_button",
	};

	// Left-hand by_button is Quest Y (menu toggle).
	static readonly StringName[] HudMenuToggleButtonActions =
	{
		"by_button",
		"y_button",
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
			uwsettings.instance.vr = false;
			return;
		}

		VrDebugLog("TryInitialize", $"OpenXR found, initialized={xrInterface.IsInitialized()}");

		ApplyVrWorldScale(gameRoot);

		if (!xrInterface.IsInitialized() && !xrInterface.Initialize())
		{
			GD.PushWarning("OpenXR failed to initialize. Running in flat-screen mode.");
			uwsettings.instance.vr = false;
			return;
		}

		XRServer.PrimaryInterface = xrInterface;
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		ConfigureRootWindowForXr(gameRoot.GetTree());

		GD.Print("[VR] OpenXR initialized; waiting for SceneTree.ProcessFrame to create XRCamera.");
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

		var tilemap = gameRoot.GetNodeOrNull<Node3D>("../tilemap");
		if (tilemap != null)
		{
			tilemap.Scale = Vector3.One * scale;
		}

		_vrWorldScaleApplied = true;
		GD.Print($"[VR] World scale {scale}x, sprite scale {spriteScale}x — godotscale={tileMapRender.godotscale}, tilemap.Scale={tilemap?.Scale}");
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
		GD.Print("[VR] FinishWorldSetup queued on SceneTree.ProcessFrame.");
	}

	public static void TickRuntime(float delta)
	{
		if (!IsActive)
		{
			return;
		}

		if (_snapTurnCooldown > 0f)
		{
			_snapTurnCooldown -= delta;
		}

		if (_doorUseCooldown > 0f)
		{
			_doorUseCooldown -= delta;
		}

		if (uwsettings.instance.vr_mirror)
		{
			SyncXrOriginBodyFromGame();
			SyncMirrorHeadLook();
		}
		else
		{
			ApplyQuitInput();
			ApplyHudMenuToggleInput();
			ApplyRecenterInput();
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

	/// <summary>VR pointer/attack input — runs in _Process so combat reads grip on the same frame.</summary>
	public static void TickVrInput()
	{
		if (!IsActive)
		{
			return;
		}

		SyncVrObjectInfoCamera();
		ProcessPendingWorldInteract();
		ApplyHudPointerInput();
		ApplyWorldPointerInput();
		ApplyDoorInteraction();
	}

	static void HookProcessFrame()
	{
		if (_processFrameHooked || _sceneTree == null)
		{
			return;
		}

		_sceneTree.ProcessFrame += OnProcessFrame;
		_processFrameHooked = true;
		GD.Print("[VR] Hooked SceneTree.ProcessFrame.");
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
				GD.Print("[VR] TickWorldSetup: waiting for gameRoot in tree.");
			}

			return;
		}

		var underworld = _gameRoot.GetParent<Node3D>();
		if (underworld?.IsInsideTree() != true)
		{
			if (_setupWaitFrames <= 5)
			{
				GD.Print("[VR] TickWorldSetup: waiting for Underworld in tree.");
			}

			return;
		}

		if (_xrOrigin == null)
		{
			GD.Print($"[VR] TickWorldSetup frame={_setupWaitFrames}: creating XR rig.");
			CreateXrRig(underworld);
		}

		if (_xrCamera?.IsInsideTree() != true)
		{
			if (_setupWaitFrames <= 10)
			{
				GD.Print($"[VR] TickWorldSetup frame={_setupWaitFrames}: XRCamera not in tree yet (origin={_xrOrigin?.IsInsideTree()}).");
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

		GD.Print($"[VR] CreateXrRig: XROrigin inTree={_xrOrigin.IsInsideTree()} path={_xrOrigin.GetPath()}");
		GD.Print($"[VR] CreateXrRig: XRCamera inTree={_xrCamera.IsInsideTree()} path={_xrCamera.GetPath()}");
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
			SetupHudHandPanel(underworld);
			EnsurePointerLaser(underworld);
		}
		playerdat.RefreshLighting();
		ResetXrOriginFloorTracking();
		playerdat.PositionPlayerCamera();
		SnapRoomOriginToAvatar();

		TryEnableOpenXrOutput();

		LogVrSetupState(_gameRoot, _sceneTree.Root.GetViewport(), "FinishActivation");
		GD.Print($"[VR] Active — display mode: {(uwsettings.instance.vr_mirror ? "mirror (SubViewport screen)" : "native world")}");
		GD.Print($"[VR] Head tracking: passthrough (OpenXR local pose, origin at game floor)");
		if (_vrWorldScaleApplied)
		{
			GD.Print($"[VR] World scale: {uwsettings.instance.vr_world_scale}x");
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
		GD.Print($"[VR] OpenXR output enabled. UseXR={rootViewport.UseXR} XRCamera path={_xrCamera.GetPath()} Current={_xrCamera.Current}");
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

		GD.Print($"[VR] Mirror screen attached ({MirrorScreenWidthMeters:F1}m wide).");
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
	static void SetupHudHandPanel(Node3D underworld)
	{
		if (!uwsettings.instance.vr_hud_panel || _leftController == null || _hudPanel != null)
		{
			return;
		}

		var ui = underworld.GetNodeOrNull<CanvasLayer>("UI");
		if (ui == null)
		{
			GD.PushWarning("[VR] HUD panel: UI CanvasLayer not found.");
			return;
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

		ui.FollowViewportEnabled = false;
		ui.GetParent()?.RemoveChild(ui);
		_hudViewport.AddChild(ui);

		// Shown while the right-hand laser points at the panel.
		_hudMouseLayer = ui.GetNodeOrNull<CanvasLayer>("mouse");
		if (_hudMouseLayer != null)
		{
			_hudMouseLayer.Visible = false;
		}

		var width = uwsettings.instance.vr_hud_panel_width;
		if (width <= 0.05f)
		{
			width = 0.42f;
		}

		var aspect = (float)HudPanelHeightPx / HudPanelWidthPx;
		var material = new StandardMaterial3D
		{
			AlbedoTexture = _hudViewport.GetTexture(),
			ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
			TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
			CullMode = BaseMaterial3D.CullModeEnum.Disabled,
			DisableReceiveShadows = true,
		};

		_hudPanel = new MeshInstance3D
		{
			Name = "VrHudPanel",
			Mesh = new QuadMesh
			{
				Size = new Vector2(width, width * aspect),
				Material = material,
			},
			Position = HudPanelLocalPosition,
			RotationDegrees = HudPanelLocalRotationDegrees,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			Layers = main.LayerGeo | main.LayerXFER,
		};
		_leftController.AddChild(_hudPanel);
		SetHudPanelVisible(_hudPanelVisible);

		GD.Print($"[VR] HUD hand panel attached to left controller ({width:F2}m wide, {HudPanelWidthPx}x{HudPanelHeightPx}).");
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

			UpdatePointerLaser(Vector3.Zero, Vector3.Zero, false);
		}
	}

	static void ApplyHudMenuToggleInput()
	{
		if (!IsActive || uwsettings.instance.vr_mirror || _leftController == null)
		{
			_hudMenuToggleWasPressed = false;
			return;
		}

		var pressed = IsButtonPressed(_leftController, HudMenuToggleButtonActions);
		if (pressed && !_hudMenuToggleWasPressed)
		{
			SetHudPanelVisible(!_hudPanelVisible);
			GD.Print($"[VR] HUD panel {(_hudPanelVisible ? "shown" : "hidden")} (Y).");
		}

		_hudMenuToggleWasPressed = pressed;
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
		underworld.AddChild(_pointerLaser);
	}

	public static void ResetXrOriginFloorTracking()
	{
		_xrOriginFloorInitialized = false;
		_lastAvatarFloorPos = Vector3.Zero;
	}

	static Vector3 GetAvatarFloorPos()
	{
		var feet = uwObject.XYZToVector3(
			motion.playerMotionParams.x_0,
			motion.playerMotionParams.y_2,
			motion.playerMotionParams.z_4);
		return new Vector3(feet.X, GetGameFloorY(), feet.Z);
	}

	public static void SyncXrOriginFromGimbal()
	{
		if (_xrOrigin == null || main.cameraYawGimbal_world == null || uwsettings.instance.vr_mirror)
		{
			return;
		}

		// Follow avatar by delta only — preserves the sticky XZ offset from B-recenter.
		// Do not pin/compensate to the cyan marker every frame (that causes motion sickness).
		var floorPos = GetAvatarFloorPos();
		if (!_xrOriginFloorInitialized)
		{
			_xrOrigin.GlobalPosition = floorPos;
			_lastAvatarFloorPos = floorPos;
			_xrOriginFloorInitialized = true;
			_lastSyncedBodyYaw = playerdat.PlayerCameraYaw_dseg_8294;
		}
		else
		{
			_xrOrigin.GlobalPosition += floorPos - _lastAvatarFloorPos;
			_lastAvatarFloorPos = floorPos;
		}

		// When body yaw changes (snap-turn), keep the headset world XZ fixed so the
		// view rotates in place instead of orbiting the XROrigin.
		var yawChanged = playerdat.PlayerCameraYaw_dseg_8294 != _lastSyncedBodyYaw;
		var headBefore = yawChanged && _xrCamera != null ? _xrCamera.GlobalPosition : Vector3.Zero;

		var bodyYaw = (float)(-((float)playerdat.PlayerCameraYaw_dseg_8294 / 32767f) * Math.PI);
		_xrOrigin.Rotation = Vector3.Zero;
		_xrOrigin.Rotate(Vector3.Up, (float)Math.PI);
		_xrOrigin.Rotate(Vector3.Up, bodyYaw);

		if (yawChanged && _xrCamera != null)
		{
			var headAfter = _xrCamera.GlobalPosition;
			_xrOrigin.GlobalPosition += new Vector3(headBefore.X - headAfter.X, 0f, headBefore.Z - headAfter.Z);
		}

		_lastSyncedBodyYaw = playerdat.PlayerCameraYaw_dseg_8294;
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

		playerdat.PositionPlayerCamera();
		SyncXrOriginFromGimbal();

		var floorPos = GetAvatarFloorPos();
		var headWorld = _xrCamera.GlobalPosition;
		var delta = new Vector3(floorPos.X - headWorld.X, 0f, floorPos.Z - headWorld.Z);
		_xrOrigin.GlobalPosition += delta;
		_lastAvatarFloorPos = floorPos;
	}

	static void ApplyRecenterInput()
	{
		if (!IsActive || uwsettings.instance.vr_mirror)
		{
			_recenterWasPressed = false;
			return;
		}

		var pressed = IsButtonPressed(_rightController, RecenterButtonActions);
		if (pressed && !_recenterWasPressed)
		{
			SnapRoomOriginToAvatar();
			GD.Print("[VR] View recentered (B button).");
		}

		_recenterWasPressed = pressed;
	}

	static void ApplyQuitInput()
	{
		if (!IsActive || uwsettings.instance.vr_mirror)
		{
			_quitWasPressed = false;
			return;
		}

		// Quest X is left-hand ax_button (right-hand ax_button is A / jump).
		var pressed = IsButtonPressed(_leftController, QuitButtonActions);
		if (pressed && !_quitWasPressed)
		{
			GD.Print("[VR] Quit requested (X button).");
			_sceneTree?.Quit();
		}

		_quitWasPressed = pressed;
	}

	/// <summary>
	/// Head height from OpenXR; room-scale X/Z is left alone so you can lean/walk in the play space.
	/// Press B to snap the view back onto the cyan avatar.
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
			GD.Print($"[VR debug] passthrough localXZ=({transform.Origin.X:F3},{transform.Origin.Z:F3}) rawY={transform.Origin.Y:F3} worldEyeY={_xrCamera.GlobalPosition.Y:F3} floorY={floorY:F3}");
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
		if (existing != null)
		{
			existing.Environment ??= new Godot.Environment();
			existing.Environment.BackgroundMode = Godot.Environment.BGMode.Color;
			existing.Environment.BackgroundColor = Colors.Black;
			return;
		}

		var worldEnvironment = new WorldEnvironment { Name = "VrWorldEnvironment" };
		worldEnvironment.Environment = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Color,
			BackgroundColor = Colors.Black,
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = Colors.White,
			AmbientLightEnergy = 2f,
		};
		underworld.AddChild(worldEnvironment);
	}

	static void LogVrSetupState(main gameRoot, Viewport rootViewport, string phase)
	{
		if (!uwsettings.instance.vr_debug)
		{
			return;
		}

		var flatCam = main.cameraPitchGimbal_world;

		GD.Print($"[VR debug] ========== VR SETUP ({phase}) ==========");
		GD.Print($"[VR debug] vr_mirror={uwsettings.instance.vr_mirror} openXrEnabled={_openXrOutputEnabled}");
		GD.Print($"[VR debug] Root UseXR={rootViewport?.UseXR} VrsMode={rootViewport?.VrsMode}");
		GD.Print($"[VR debug] SubViewport update={_gameViewport?.RenderTargetUpdateMode} size={_gameViewport?.Size}");
		GD.Print($"[VR debug] Flat camera Current={flatCam?.Current} inTree={flatCam?.IsInsideTree()}");
		GD.Print($"[VR debug] XROrigin inTree={_xrOrigin?.IsInsideTree()} path={_xrOrigin?.GetPath()}");
		GD.Print($"[VR debug] XRCamera inTree={_xrCamera?.IsInsideTree()} path={_xrCamera?.GetPath()} Current={_xrCamera?.Current}");
		GD.Print($"[VR debug] Mirror={_xrCamera?.GetNodeOrNull("VrMirrorScreen") != null}");
		GD.Print("[VR debug] ========================================");
	}

	static void LogVrRuntimeState()
	{
		var rootVp = _sceneTree?.Root?.GetViewport();
		GD.Print($"[VR debug] frame={_debugFrameCounter} UseXR={rootVp?.UseXR} xrInTree={_xrCamera?.IsInsideTree()} flatCam={main.cameraPitchGimbal_world?.Current} xrCam={_xrCamera?.Current}");
	}

	static void VrDebugLog(string phase, string message)
	{
		if (uwsettings.instance.vr_debug)
		{
			GD.Print($"[VR debug] {phase}: {message}");
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
		if (!IsActive || _leftController == null)
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

		ApplySnapTurn();
		ApplyJumpInput();
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
		playerdat.PlayerCameraYaw_dseg_8294 = (short)(playerdat.PlayerCameraYaw_dseg_8294 + (degrees / 180f * 32767f));
		_snapTurnCooldown = SnapTurnCooldownSeconds;

		if (uwsettings.instance.vr_mirror)
		{
			SyncXrOriginBodyFromGame();
		}
		else
		{
			SyncXrOriginFromGimbal();
		}
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

	public static void UpdateVisionHeadingFromYaw(short yaw)
	{
		playerdat.CameraYawHeadingRelated_2B52 = (short)(((1 + (yaw >> 0xD)) & 0x7) >> 1);
		playerdat.CameraPointer2C = (short)(yaw - motion.PlayerCardinalHeadingLookupTable[playerdat.CameraYawHeadingRelated_2B52]);
	}

	public static void UpdateVisionFromHead(short tileX, short tileY, short yaw)
	{
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
		GD.Print("[VR] Body marker created.");
	}

	static void UpdateBodyMarker()
	{
		if (_bodyMarker == null || !GodotObject.IsInstanceValid(_bodyMarker))
		{
			return;
		}

		var show = IsActive && !uwsettings.instance.vr_mirror && uwsettings.instance.vr_show_body;
		_bodyMarker.Visible = show;
		if (!show)
		{
			return;
		}

		var px = motion.playerMotionParams.x_0;
		var py = motion.playerMotionParams.y_2;
		var pz = motion.playerMotionParams.z_4;
		var feet = uwObject.XYZToVector3(px, py, pz);
		var eye = uwObject.XYZToVector3(px, py, pz + 0xA4);
		var bodyHeight = Mathf.Max(0.2f, eye.Y - feet.Y);
		var radius = Mathf.Max(0.08f, (motion.playerMotionParams.radius_22 / 8f) * tileMapRender.TileWidth);

		if (_bodyMarker.Mesh is CapsuleMesh capsule)
		{
			capsule.Radius = radius;
			capsule.Height = bodyHeight;
		}

		_bodyMarker.GlobalPosition = feet + Vector3.Up * (bodyHeight * 0.5f);
		var bodyYaw = (float)(-((float)playerdat.PlayerCameraYaw_dseg_8294 / 32767f) * Math.PI);
		_bodyMarker.Rotation = new Vector3(0f, bodyYaw, 0f);
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

	static void ApplyWorldPointerInput()
	{
		IsVrWorldPointerActive = false;
		IsVrWorldRightHeld = false;

		if (!IsActive || uwsettings.instance.vr_mirror || _rightController == null || _xrCamera == null)
		{
			_worldPointerLeftWasPressed = false;
			_worldPointerRightWasPressed = false;
			return;
		}

		if (_hudPanelVisible && _hudPointerHovering)
		{
			_worldPointerLeftWasPressed = false;
			_worldPointerRightWasPressed = false;
			return;
		}

		var rayOrigin = _rightController.GlobalPosition;
		var rayDir = GetControllerRayDir();
		var rightPressed = IsButtonPressed(_rightController, HudRightClickActions);
		var inAttackMode = uimanager.InteractionMode == uimanager.InteractionModes.ModeAttack;

		if (!_hudPanelVisible)
		{
			UpdatePointerLaser(rayOrigin, rayOrigin + rayDir * HudPointerMaxDistance, visible: true);
		}

		// Attack charge needs a steady grip hold; controller aim diverges from head look
		// when pointing at world targets, so skip the head-alignment gate in attack mode.
		if (inAttackMode)
		{
			IsVrWorldPointerActive = true;
			UpdateViewPortMouseFromControllerAim(rayDir);
			IsVrWorldRightHeld = rightPressed;
			_worldPointerRightWasPressed = rightPressed;

			var leftPressed = IsButtonPressed(_rightController, HudLeftClickActions);
			if (leftPressed && !_worldPointerLeftWasPressed && IsControllerAimedIntoView(rayDir))
			{
				QueueWorldInteract(rayOrigin, rayDir, leftClick: true);
			}
			_worldPointerLeftWasPressed = leftPressed;
			return;
		}

		if (!IsControllerAimedIntoView(rayDir))
		{
			_worldPointerLeftWasPressed = false;
			_worldPointerRightWasPressed = false;
			return;
		}

		IsVrWorldPointerActive = true;
		UpdateViewPortMouseFromControllerAim(rayDir);

		var leftPressedInteract = IsButtonPressed(_rightController, HudLeftClickActions);
		if (leftPressedInteract && !_worldPointerLeftWasPressed)
		{
			QueueWorldInteract(rayOrigin, rayDir, leftClick: true);
		}
		_worldPointerLeftWasPressed = leftPressedInteract;

		if (rightPressed && !_worldPointerRightWasPressed)
		{
			QueueWorldInteract(rayOrigin, rayDir, leftClick: false);
		}
		_worldPointerRightWasPressed = rightPressed;
	}

	static bool IsControllerAimedIntoView(Vector3 rayDir)
	{
		var headForward = -_xrCamera.GlobalTransform.Basis.Z;
		if (headForward.LengthSquared() < 0.0001f)
		{
			return false;
		}

		return headForward.Normalized().Dot(rayDir) > 0.25f;
	}

	static void UpdateViewPortMouseFromControllerAim(Vector3 rayDir)
	{
		var local = _xrCamera.GlobalTransform.Basis.Inverse() * rayDir;
		var vp = uimanager.instance.uwviewport;
		var xNorm = Mathf.Clamp(0.5f + local.X * 1.2f, 0f, 1f);
		var yNorm = Mathf.Clamp(0.5f - local.Y * 1.2f, 0f, 1f);
		uimanager.SetViewPortMouseFromUwLocal(new Vector2(xNorm * vp.Size.X, yNorm * vp.Size.Y));
	}

	static void QueueWorldInteract(Vector3 rayOrigin, Vector3 rayDir, bool leftClick)
	{
		_pendingWorldInteract = true;
		_pendingWorldInteractLeft = leftClick;
		_pendingRayOrigin = rayOrigin;
		_pendingRayDir = rayDir;
		_pendingWorldInteractDelayFrames = 1;
	}

	static void ProcessPendingWorldInteract()
	{
		if (!_pendingWorldInteract)
		{
			return;
		}

		if (_pendingWorldInteractDelayFrames > 0)
		{
			_pendingWorldInteractDelayFrames--;
			return;
		}

		_pendingWorldInteract = false;
		TryInteractObjectInfoPick(_pendingRayOrigin, _pendingRayDir, _pendingWorldInteractLeft);
	}

	static void TryInteractObjectInfoPick(Vector3 rayOrigin, Vector3 rayDir, bool leftClick)
	{
		var text = (Texture2D)uimanager.instance?.uwsubviewport_objectinfo?.GetTexture();
		if (text == null)
		{
			return;
		}

		var img = text.GetImage();
		var maxDist = uimanager.RayDistance;
		if (maxDist <= 0f)
		{
			maxDist = 3f;
		}

		maxDist *= tileMapRender.TileWidth;
		var step = tileMapRender.TileWidth * 0.2f;
		Vector2 bestPixel = default;
		var hasSample = false;

		for (var dist = step; dist <= maxDist; dist += step)
		{
			if (!TryUnprojectToObjectInfoPixel(rayOrigin + rayDir * dist, out var samplePixel))
			{
				continue;
			}

			hasSample = true;
			bestPixel = samplePixel;
			if (uimanager.TryGetObjectIndexFromImage(img, samplePixel, out _))
			{
				uimanager.SetViewPortMouseFromObjectInfoPixel(samplePixel);
				uimanager.ProcessObjectInfoPixelFromImage(img, samplePixel, leftClick);
				return;
			}
		}

		if (hasSample)
		{
			uimanager.SetViewPortMouseFromObjectInfoPixel(bestPixel);
			uimanager.ProcessObjectInfoPixelFromImage(img, bestPixel, leftClick);
			return;
		}

		if (TryGetHeadCenterObjectInfoPixel(out var headPixel))
		{
			uimanager.SetViewPortMouseFromObjectInfoPixel(headPixel);
			uimanager.ProcessObjectInfoPixelFromImage(img, headPixel, leftClick);
		}
	}

	static bool TryGetHeadCenterObjectInfoPixel(out Vector2 texPixel)
	{
		texPixel = default;
		if (_xrCamera == null || uimanager.instance?.uwsubviewport_objectinfo == null)
		{
			return false;
		}

		var lookPoint = _xrCamera.GlobalPosition + (-_xrCamera.GlobalTransform.Basis.Z) * 2f;
		return TryUnprojectToObjectInfoPixel(lookPoint, out texPixel);
	}

	static void TryInteractWorldRay(Vector3 rayOrigin, Vector3 rayDir, bool leftClick)
	{
		QueueWorldInteract(rayOrigin, rayDir, leftClick);
	}

	static Vector3 GetControllerRayDir()
	{
		var rayDir = -_rightController.GlobalTransform.Basis.Z;
		if (rayDir.LengthSquared() < 0.0001f)
		{
			rayDir = -_rightController.GlobalTransform.Basis.Y;
		}

		return rayDir.Normalized();
	}

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

	static void ApplyHudPointerInput()
	{
		IsHud3DViewportHovering = false;
		IsHud3DViewportRightHeld = false;

		if (!IsActive || uwsettings.instance.vr_mirror || _hudViewport == null || _hudPanel == null || _rightController == null)
		{
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

		if (!_hudPanelVisible)
		{
			_hudPointerHovering = false;
			_hudPointerLeftWasPressed = false;
			_hudPointerRightWasPressed = false;
			return;
		}

		var rayOrigin = _rightController.GlobalPosition;
		var rayDir = -_rightController.GlobalTransform.Basis.Z;
		if (rayDir.LengthSquared() < 0.0001f)
		{
			rayDir = -_rightController.GlobalTransform.Basis.Y;
		}
		rayDir = rayDir.Normalized();

		var hovering = TryGetHudPanelHit(rayOrigin, rayDir, out var viewportPos, out var hitWorld);
		var laserEnd = hovering ? hitWorld : rayOrigin + rayDir * HudPointerMaxDistance;
		UpdatePointerLaser(rayOrigin, laserEnd, visible: true);

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

		if (hovering)
		{
			var in3dViewport = TryMapToUwViewport(viewportPos, out var uwLocal);
			if (in3dViewport)
			{
				IsHud3DViewportHovering = true;
				uimanager.SetViewPortMouseFromUwLocal(uwLocal);

				var leftPressed = IsButtonPressed(_rightController, HudLeftClickActions);
				if (leftPressed && !_hudPointerLeftWasPressed)
				{
					uimanager.TriggerViewPortClick(uwLocal, leftClick: true);
				}
				_hudPointerLeftWasPressed = leftPressed;

				var rightPressed = IsButtonPressed(_rightController, HudRightClickActions);
				if (uimanager.InteractionMode == uimanager.InteractionModes.ModeAttack)
				{
					IsHud3DViewportRightHeld = rightPressed;
				}
				else if (rightPressed && !_hudPointerRightWasPressed)
				{
					uimanager.TriggerViewPortClick(uwLocal, leftClick: false);
				}
				_hudPointerRightWasPressed = rightPressed;
			}
			else
			{
				var leftPressed = IsButtonPressed(_rightController, HudLeftClickActions);
				if (leftPressed && !_hudPointerLeftWasPressed)
				{
					PushHudMouseClick(viewportPos, MouseButton.Left);
				}
				_hudPointerLeftWasPressed = leftPressed;

				var rightPressed = IsButtonPressed(_rightController, HudRightClickActions);
				if (rightPressed && !_hudPointerRightWasPressed)
				{
					PushHudMouseClick(viewportPos, MouseButton.Right);
				}
				_hudPointerRightWasPressed = rightPressed;
			}
		}
		else
		{
			_hudPointerLeftWasPressed = false;
			_hudPointerRightWasPressed = false;
		}
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

	static bool TryGetHudPanelHit(Vector3 rayOrigin, Vector3 rayDir, out Vector2 viewportPos, out Vector3 hitWorld)
	{
		viewportPos = default;
		hitWorld = rayOrigin + rayDir * HudPointerMaxDistance;
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
		if (t < 0f || t > HudPointerMaxDistance)
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

	static void UpdatePointerLaser(Vector3 from, Vector3 to, bool visible)
	{
		if (_pointerLaser == null || _pointerLaserMesh == null)
		{
			return;
		}

		_pointerLaser.Visible = visible;
		if (!visible)
		{
			return;
		}

		var delta = to - from;
		var length = delta.Length();
		if (length < 0.005f)
		{
			_pointerLaser.Visible = false;
			return;
		}

		var direction = delta / length;
		_pointerLaserMesh.Height = length;
		_pointerLaser.GlobalPosition = from + direction * (length * 0.5f);
		_pointerLaser.GlobalBasis = BasisWithYAxis(direction);
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

	static void ApplyDoorInteraction()
	{
		if (!IsActive || playerdat.ParalyseTimer > 0)
		{
			_doorUseWasPressed = false;
			return;
		}

		var pressed = IsButtonPressed(_leftController, DoorUseButtonActions);
		if (pressed && !_doorUseWasPressed && _doorUseCooldown <= 0f)
		{
			// Right trigger/grip on the HUD panel are reserved for UI clicks.
			if ((_hudPointerHovering && _hudPanelVisible &&
				(IsButtonPressed(_rightController, HudLeftClickActions) ||
				 IsButtonPressed(_rightController, HudRightClickActions)))
				|| IsHud3DViewportRightHeld
				|| IsVrWorldRightHeld)
			{
				_doorUseWasPressed = pressed;
				return;
			}

			var target = FindTargetDoor();
			if (target != null)
			{
				door.VrUse(target);
				_doorUseCooldown = DoorUseCooldownSeconds;
			}
		}

		_doorUseWasPressed = pressed;
	}

	static uwObject FindTargetDoor()
	{
		var fromRay = TryRaycastDoor();
		if (fromRay != null)
		{
			return fromRay;
		}

		return FindNearestDoorInTiles();
	}

	static uwObject TryRaycastDoor()
	{
		if (_xrCamera == null || _gameRoot == null)
		{
			return null;
		}

		var maxDistance = 3f * tileMapRender.TileWidth;
		var from = _xrCamera.GlobalPosition;
		var to = from + (-_xrCamera.GlobalTransform.Basis.Z) * maxDistance;
		var query = PhysicsRayQueryParameters3D.Create(from, to);
		query.CollideWithAreas = false;
		var result = _gameRoot.GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (result.Count == 0 || !result.ContainsKey("collider"))
		{
			return null;
		}

		if (result["collider"].AsGodotObject() is not Node node)
		{
			return null;
		}

		while (node != null)
		{
			if (TryGetObjectIndex(node.Name, out var index))
			{
				var obj = UWTileMap.current_tilemap?.LevelObjects[index];
				if (IsDoorObject(obj))
				{
					return obj;
				}
			}

			node = node.GetParent();
		}

		return null;
	}

	static uwObject FindNearestDoorInTiles()
	{
		var objList = UWTileMap.current_tilemap?.LevelObjects;
		if (objList == null)
		{
			return null;
		}

		var px = motion.playerMotionParams.x_0;
		var py = motion.playerMotionParams.y_2;
		var playerPos = uwObject.XYZToVector3(px, py, motion.playerMotionParams.z_4);

		var forward = Vector3.Zero;
		if (_xrCamera != null)
		{
			forward = -_xrCamera.GlobalTransform.Basis.Z;
			forward.Y = 0;
			if (forward.LengthSquared() > 0.0001f)
			{
				forward = forward.Normalized();
			}
			else
			{
				forward = Vector3.Zero;
			}
		}

		var maxRange = 2.5f * tileMapRender.TileWidth;
		uwObject best = null;
		var bestDist = maxRange;
		var centerX = playerdat.playerObject.tileX;
		var centerY = playerdat.playerObject.tileY;

		for (var dy = -1; dy <= 1; dy++)
		{
			for (var dx = -1; dx <= 1; dx++)
			{
				var tx = centerX + dx;
				var ty = centerY + dy;
				if (!UWTileMap.ValidTile(tx, ty))
				{
					continue;
				}

				var candidate = objectsearch.FindMatchInTile(tx, ty, 5, 0, -1);
				if (candidate == null)
				{
					candidate = objectsearch.FindMatchInTile(tx, ty, 7, 0, 0xF);
				}

				if (!IsDoorObject(candidate))
				{
					continue;
				}

				var doorPos = candidate.GetCoordinate();
				var offset = doorPos - playerPos;
				offset.Y = 0;
				var dist = offset.Length();
				if (dist >= bestDist)
				{
					continue;
				}

				if (forward != Vector3.Zero && dist > 0.1f)
				{
					var dot = offset.Normalized().Dot(forward);
					if (dot < 0.3f)
					{
						continue;
					}
				}

				best = candidate;
				bestDist = dist;
			}
		}

		return best;
	}

	static bool IsDoorObject(uwObject obj)
	{
		if (obj == null)
		{
			return false;
		}

		if (obj.majorclass == 5 && obj.minorclass == 0)
		{
			return true;
		}

		return obj.majorclass == 7 && obj.minorclass == 0 && obj.classindex == 0xF;
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
