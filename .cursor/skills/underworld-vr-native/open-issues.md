# VR Open Issues (UnderworldGodot)

Last updated from native VR work through conversation fixes (commit `3289f01` on `main`).

## High priority

### Movement jitter when running forward
- **Symptom:** Regular forward run (e.g. game start) feels jaggy; occasional small backward hitch. Worse in initial spawn-facing direction than opposite.
- **Likely cause:** ~10 Hz DOS motion sim vs 72–90 Hz XR; `SyncXrOriginFromGimbal` applies discrete floor deltas. Possible interaction with body-yaw alignment in `motion_player.cs`.
- **Tried (did not fix):** Snap-turn-only play-space rotation; head XZ compensation threshold; smooth XROrigin lerp; per-physics-frame snap turn.
- **Next ideas:** Profile motion tick vs physics frame order; interpolate display position without changing sim; run VR motion input sampling every physics frame while keeping `PlayerMotion` at 10 Hz; inspect collision micro-corrections on forward axis.

### Close object look misses ("you see nothing")
- **Symptom:** Nearby objects (e.g. bedroll at feet) sometimes return "you see nothing" in look mode.
- **Constraint:** Must use **controller laser ray only** — no head aim.
- **Next ideas:** Improve quad/AABB pick for low floor sprites; ensure physics pick runs when geometric pick misses; verify `RayDistance` / vision refresh before pick; laser must actually point at object (user aims with controller).

## Medium priority

### Laser visibility when HUD hidden
- Current: Y toggle hides HUD and laser; world interact works without visible laser.
- May want optional world laser when HUD hidden for targeting feedback.

### Snap turn at motion tick rate
- `ApplySnapTurn` runs inside `ApplyMotionInputs` (~10 Hz). Could move to `TickRuntime` for snappier feel (verify no double-fire on motion tick frames).

### `UpdateViewPortMouseFromControllerAim` vs laser
- World pointer still feeds flat viewport mouse from controller aim for some legacy paths; ensure consistency with 3D laser pick.

## Low priority / polish

- **VR laser reach vs `CanReach` (minor):** Laser tip uses avatar-centered sphere radius from `PickupDistance`/`UseDistance`; game `CanReach` also checks Z height and pole/swim offsets. Edge-case mismatch with "you cannot reach that" possible — refine if needed.
- Automap in VR (`InAutomap` — `ShouldTickVrInput` includes it but UX untested)
- UW2 conversation UI layout on hand HUD panel
- Attack mode laser when HUD hidden (no visible laser today)
- Document B-recenter behavior for new players

## Confirmed working

- Native VR object pickup / door / world interact via 3D laser pick
- VR gameplay laser reach anchored to avatar `CanReach` radius (arm extension shortens beam)
- VR inventory → held object on laser; laser-only throw/drop direction
- VR chargen on-screen keyboard; intro menu TV brightness
- Wall/floor/ceiling look via tile surface raycast
- Far look → "you see nothing" when beyond vision
- Inventory rainbow outlines after HUD SubViewport move
- Conversation UI: HUD auto-show, laser on panel, numbered scroll selection
- Play-space rotation decoupled from gradual body-yaw drift (snap turn only)

## Key files

| File | VR concern |
|------|------------|
| `src/vr/VrController.cs` | XR rig, laser, picking, HUD, origin sync |
| `main.cs` | `ShouldTickVrInput`, motion tick gate |
| `src/ui/uimanager.cs` | `InGame`, `blockinput`, `InConversation` |
| `src/conversation/conversationinitialisation.cs` | `OnConversationStarted` |
| `src/physics/motion_player.cs` | VR yaw via `TryGetMotionYaw`, body alignment |
| `src/player/playerdatcamera.cs` | Gimbal vs head vision in VR |
| `src/utility/config.cs` | VR settings |
