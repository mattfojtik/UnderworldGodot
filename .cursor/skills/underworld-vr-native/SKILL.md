---
name: underworld-vr-native
description: Native OpenXR VR for UnderworldGodot (Quest 3). Covers VrController, XROrigin sync, HUD hand panel, input tick gating, conversations, and locomotion. Use when working on VR, VrController.cs, OpenXR, HUD panel, or blockinput menus.
---

# Underworld Native VR

Godot 4.3 C# fork with native VR (`vr: true`, not `vr_mirror`). Primary file: `src/vr/VrController.cs`.

## Architecture

| Piece | Role |
|-------|------|
| `XROrigin3D` + `XRCamera3D` | Player view in root viewport; `main.cameraPitchGimbal_world` points at `_xrCamera` in native mode |
| `Underworld/tilemap` | World geometry at absolute coords (not gimbal-parented) |
| `SubViewport` HUD | 1280×800 UI reparented into `VrHudViewport`; textured quad on **off-hand controller** |
| Cyan laser | Right-hand menu pointer + dominant-hand world ray; `DrawMenuPointerLaser` / `UpdatePointerLaser` (`PointerLaserRadius` 2.5 mm) |

**Tick split:**
- `TickRuntime(delta)` — `_PhysicsProcess`: XR origin sync, snap turn, stick yaw, body marker
- `TickVrInput()` — `_Process`: HUD/world laser, door use, clicks

## Critical: When VR Input Runs

`uimanager.InGame` is **false** during `GameModes.CONVERSATION` and `AUTOMAP`.

```csharp
// main.cs — correct pattern
if (VrController.ShouldTickVrInput())  // InGame || InConversation || InAutomap
    VrController.TickVrInput();

if (uimanager.InGame && !uimanager.blockinput)
    { combat, PlayerTimedLoop, RefreshWorldState }
```

**Never gate `TickVrInput` on `blockinput` alone** — conversations set `blockinput` true and the laser freezes if VR input stops.

`ShouldTickVrInput()` also returns false for `vr_mirror` mode.

## VR debug logging

Always use `VrDiagLog.Print` / `Warn` / `Debug` for VR diagnostics (never bare `GD.Print` / `Debug.Print`). Native VR mirrors to console **and** `logs/vr_diag.log` + `user://vr_diag.log`. See `.cursor/rules/vr-debug-logging.mdc`.

When a fix fails or behavior is wrong, add **extensive** `VrDiagLog` lines and **grep the log files yourself** — do not ask the user to paste logs. See `.cursor/rules/extensive-diagnostics.mdc`.

## XROrigin / Locomotion

- Position: `SyncXrOriginFromGimbal()` follows avatar floor by **delta** each physics frame (preserves B-recenter sticky offset).
- **Motion interpolation:** DOS sim runs ~10 Hz; XR runs 72–90 Hz. `main._PhysicsProcess` passes `motionBlend` (0→1 within each motion tick) to `TickRuntime`. `GetDisplayFloorPos()` lerps between `EndMotionStep` prev/curr floor samples so the play space moves smoothly between sim steps.
- Rotation: `_xrPlaySpaceYawRadians` updates **only on snap turns** (≥6000 yaw units, ~45°). Do not rotate play space on gradual `PlayerCameraYaw` alignment during walking — that causes backward jitter.
- Snap turn: right stick X, 45°, cooldown 0.35s; head XZ compensated on snap only.
- B button: `SnapRoomOriginToAvatar()` was moved to **right stick click**; **right B** toggles combat.

**Do not** use head position for play-space rotation during normal locomotion.

## HUD Panel

- Toggle: left **Y** (`ApplyHudMenuToggleInput`)
- Hidden panel: laser off; world interact still works on trigger (no visible laser)
- Conversations: `OnConversationStarted()` → `SetHudPanelVisible(true)`; `ShouldUseHudMenuPointerOnly()` when `blockinput` — no world raycast, short laser stub when not on panel

## Quest Controls

| Input | Action |
|-------|--------|
| Left stick | Move (head-relative when moving) |
| Right stick X | Snap turn 45° |
| Right stick click | Recenter view |
| Right trigger / grip (HUD) | UI left / right click (fixed right hand) |
| Dom grip / trigger | Get / Use (exploration) |
| Off-hand grip / trigger | Talk / Look |
| Left grip | Cancel when prompt open |
| Left Y | Toggle status overlays |
| Left menu | Toggle hand HUD |
| Left X | Cast spell (runes ready) |
| Right B | Combat toggle |
| Right A | Jump |

See `docs/vr-controls.md` for full scheme.

## Settings (`src/utility/config.cs`)

- `vr`, `vr_mirror`, `vr_hud_panel`, `vr_hud_panel_width`, `vr_show_body`, `vr_world_scale`, `vr_tmap_wall_offset_m`, `vr_invert_stick_y`, `vr_debug`

## Lighting (VR-specific, unchanged by interaction work)

- Native VR: `final_color_pass = true`; palette in spatial shaders
- `VrViewDistance = 512` vs flat ~33.6 → brighter at distance
- `VrWorldEnvironment` ambient energy 2.0

## Debugging

- `vr_debug: true` — runtime logs, mirror screen visible
- Cyan body marker when `vr_show_body`
- Conversation softlock symptom: laser frozen in world = `TickVrInput` not running

## Additional Resources

- Interaction/picking: [../underworld-vr-interaction/SKILL.md](../underworld-vr-interaction/SKILL.md)
- Button remap / intro laser safety: [../underworld-vr-input-changes/SKILL.md](../underworld-vr-input-changes/SKILL.md)
- Open issues: [open-issues.md](open-issues.md)
- Architecture detail: [reference.md](reference.md)
