---
name: underworld-vr-input-changes
description: Safe workflow for changing VR button mappings, pointer hands, or menu laser behavior in UnderworldGodot. Use when remapping Quest controls, moving HUD/menu pointer between hands, fixing intro menu laser, or debugging VR regressions after input changes.
---

# Underworld VR Input Changes

Godot 4.3 C# — primary file `src/vr/VrController.cs`, scheme in `docs/vr-controls.md`.

## Why a "simple" remap breaks a lot

VR input here is **not** isolated button handlers. `VrController` couples:

| Layer | What breaks if you only swap bindings |
|-------|--------------------------------------|
| **Boot vs gameplay** | Intro uses Menu TV on `XRCamera`; in-game uses hand HUD. `IsActive`, `ShouldTickVrInput`, and `FinishActivation` paths differ. |
| **Pointer vs laser** | UI clicks use `GetMenuPointerRayOrigin` / `TryGetHudPanelHit`. The cyan beam is a separate mesh (`DrawMenuPointerLaser`). They must share the same world ray; visual-only offsets use `MenuPointerLaserAimOffset`. |
| **Hand roles** | Menu pointer = **right** (`GetMenuPointerController`). Dominant aim / world verbs = `GetDominantController()`. HUD mesh = **off-hand** (`GetHudHandController()`). Swapping buttons without updating these splits breaks one path but not the other. |
| **Tick timing** | `TickVrInput` runs in `main._Process` (XR poses). `TickRuntime` in `_PhysicsProcess`. Lasers and hits must not assume physics rate. |
| **Game mode gating** | `uimanager.InGame` is false on intro, MAIN, CUTSCENE, OPTIONS. `ShouldTickVrInput` must include menu/cutscene modes. Never gate laser on `blockinput` alone. |
| **World setup side effects** | `FinishActivation` calls camera/vision setup. `PositionPlayerCamera` → `UpdateVisionFromHead` throws before a level is loaded — guard with `uimanager.InGame`. |
| **Laser parenting** | Menu TV laser is parented to the **menu pointer controller** in local space. World-space under `Underworld` drifted; camera-local was head-locked. Wrong parent = frozen or misaligned beam. |

A button remap PR often touches `ApplyVrShortcutInput`, `ApplyExplorationVerbInput`, and comments — but regressions show up in **intro laser**, **body marker**, and **menu hit tests** because those share the same tick and pointer plumbing.

## Before changing bindings

1. Read `docs/vr-controls.md` and note which **hand** owns menu pointer vs dominant aim vs HUD.
2. Grep `VrController` for the old button action names and for `GetMenuPointerController`, `GetDominantController`, `GetHudHandController`.
3. Confirm `ShouldTickVrInput` still true for CUTSCENE, MAIN, OPTIONS, and `AtMainMenu`.

## Implementation rules

### Pointer and laser (must stay matched)

- **Menu TV (intro / MAIN / chargen)**: `ApplyHudPointerInput` when `IsHudOnMenuScreen()` or `NeedsFrontMenuLaser()` — calls `TryEnsureMenuTvScreen()` first, then hit test, `DrawMenuPointerLaser` (with `MenuPointerLaserAimOffset`), cursor, and clicks in one path. Never use world-space `UpdatePointerLaser` for menu TV.
- **In-game hand HUD**: `ApplyHudPointerInput` (dominant aim ray) when `!IsHudOnMenuScreen()`.
- **Thickness**: single constant `PointerLaserRadius` (2.5 mm). Do not add a thicker intro-only radius unless product asks.
- **Aim offset**: `MenuPointerLaserAimOffset` — visual start only; do not move the hit ray unless intentionally retuning both.
- **Fallback**: `UpdateMenuTvPointerLaser()` at end of `TickVrInput` when HUD path bails early.

### Hands (current scheme)

| Role | API |
|------|-----|
| Menu / intro laser + HUD clicks | `GetMenuPointerController()` → right |
| World aim, combat, exploration verbs | `GetDominantController()` |
| HUD panel mesh | `GetHudHandController()` → off-hand |

### Activation guards

- Skip `PositionPlayerCamera` / `UpdateVisionFromHead` when `!uimanager.InGame` (intro/menu).
- `ShouldShowBodyMarker()` — only live `GAME` mode, not menu TV.
- `HudPointerOwnsLaser()` — must be true for menu TV so gameplay laser does not clear the beam.

## After changing bindings — test checklist

1. **Intro cutscene** — laser tracks right hand, clicks match cursor, no body marker.
2. **MAIN / JOURNEY / CHARGEN** — menu TV laser + back on left grip.
3. **In-game** — hand HUD laser, world laser, combat toggle (right B), recenter (right stick click).
4. **Conversation / OPTIONS** — laser still ticks (`ShouldTickVrInput`).

## Diagnostics

Enable `vr_diag_log` / `vr_debug` in `user://settings.json`. Read:

- Workspace: `logs/vr_diag.log`
- User data: `%APPDATA%/Godot/app_userdata/Underworld/vr_diag.log`

Look for `[VR intro] snapshot` (`headRay`, `laserVis`, `rightPos`) and `laser visible ->`.

## Related skills

- `.cursor/skills/underworld-vr-native/SKILL.md` — XROrigin, locomotion, tick split
- `.cursor/skills/underworld-vr-interaction/SKILL.md` — world laser, pickup, conversations

## Do not

- Reparent menu laser to `XRCamera` for tracked-controller play (head-locks the beam).
- Use `controller.Position == 0` alone to detect tracking; prefer grip ray + `DrawMenuPointerLaser` with shared hit world.
- Run full `FinishActivation` vision path on intro boot without `InGame` guards.
- Gate `TickVrInput` on `uimanager.InGame` only.
