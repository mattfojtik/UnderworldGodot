# Native VR Reference

## Scene / render path

```
Underworld/
  tilemap/          # absolute world positions
  main/
  SubViewportWorld/ # disabled in native VR (UpdateMode.Disabled)
  XROrigin/         # added at runtime under Underworld
    XRCamera3D      # becomes main.cameraPitchGimbal_world
    LeftController  # HUD quad child
    RightController # laser origin
  VrHudViewport/    # UI CanvasLayer at 1280×800
```

`SetupNativeWorldCamera()` sets `_xrCamera.Current = true`, root `UseXR = true`, disables world SubViewport render.

## Key functions (`VrController.cs`)

| Function | When |
|----------|------|
| `InitExplorePlayer` / `FinishActivation` | VR startup, HUD panel, laser, `SnapRoomOriginToAvatar` |
| `SyncXrOriginFromGimbal` | Every physics frame (native) |
| `ApplyMotionInputs` | Motion tick (~10 Hz) via `ProcessMotionInputs` |
| `UpdateHeadRelativeMotionYaw` | Every physics frame while stick active |
| `TryGetMotionYaw` | `motion_player` uses head yaw for walk direction |
| `ApplyHudPointerInput` | HUD ray → SubViewport mouse events |
| `ApplyWorldPointerInput` | World laser + `TryInteractLaserPick` |
| `TryPickClosestObjectAlongRay` | Quad/AABB mesh tests on object nodes |
| `TryPickClosestTileSurfaceAlongRay` | Tile mesh triangles, shader `tileflags` |
| `TrySelectConversationOption` | Click numbered lines in message scroll |

## `blockinput` sources (`uimanager.cs`)

`InConversation`, `InAutomap`, typed input, yes/no prompts, playing instrument, OPTIONS mode.

Gameplay (`combat`, motion tick, `RefreshWorldState`) stays gated on `!blockinput`. VR pointer is separate.

## Conversation flow

1. `conversationinitialisation.StartConversation` → `CurrentGameMode = CONVERSATION`
2. `VrController.OnConversationStarted()` shows HUD
3. Options appear in **bottom message scroll** (`1. ...`, `2. ...`) via `AddToMessageScroll`
4. Point right laser at left HUD scroll; right trigger selects
5. `TrySelectConversationOption` uses `scroll.Position` / `scroll.Size` (viewport-local, not `GetGlobalRect`)

## Inventory HUD after viewport move

- `ApplyInventorySprite()` in `uimanager_paperdoll.cs` — nearest filter on inventory TextureRects
- `VrHudViewport.CanvasItemDefaultTextureFilter = Nearest`
- Do not change `uisprite.gdshader` palette lookup without testing rainbow outlines

## Failed approaches (do not re-apply without new evidence)

| Approach | Why it failed / note |
|----------|----------------------|
| Gate `TickVrInput` on `InGame` only | Conversations use `CONVERSATION` mode → laser frozen |
| Head/camera ray for look | User rejected; breaks look — use controller laser only |
| Generic smooth lerp on XROrigin (no motion-step sync) | Did not fix forward-run jitter |
| Rotate XROrigin every frame with body yaw | Backward jitter; only snap-turn rotation now |
| Head XZ compensation on small yaw steps | Jitter; threshold 6000 for snap only |

**What fixed forward locomotion:** interpolate display floor position between DOS motion ticks (`EndMotionStep`, `GetDisplayFloorPos`, `motionBlend` from `main._PhysicsProcess`) — not a blind XROrigin lerp.
