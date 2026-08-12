# VR Status Panel Offset Tuner (branch-only)

**Branch:** `vr-status-panel-offset-tuner`  
**Not for merge into shipping.** Copy dialed-in values from `user://settings.json` into defaults / main when done.

## Enable

Tuner is **forced on** for this branch once you are in-game with status panels. It also writes `vr_status_panel_offset_tune: true` into settings if the key was missing (JSON bool defaulted to false).

While active:

- All head-locked status panels stay visible (including conversation)
- Demo content: 3 shelf runes, 3 active spells, looping sword stab, conversation chrome on
- World Look/Talk/Get/Use/combat gestures are suppressed (locomotion + snap turn still work)
- Debug labels show each panel’s X/Y/Z (billboard, fixed-size, near the face for the HUD card)

## Controls (Quest)

| Input | Action |
|-------|--------|
| Dominant laser + **trigger** | Select panel under reticle |
| Off-hand **trigger** / **grip** | Next / previous panel (includes **GlobalY**) |
| Dominant **grip** | Cycle axis X → Y → Z (GlobalY is Y-only) |
| Right stick **Y** (past threshold) | Discrete nudge ± on selected axis |
| Hold left **grip** while nudging | Fine step **1 mm** (default coarse **10 mm**) |
| Left **X** | Hide currently selected panel |
| Left **Y** | Show all panels again |
| Left stick **click** | Save all offsets to `settings.json` |

Nudges are edge/repeat on the stick, not continuous hand tracking — hand jitter cannot drift values.

## On-screen text

- Per-panel `Label3D` (camera-parented, billboard): name + `X/Y/Z` (metres). Selected panel is highlighted yellow.
- Head-locked HUD card (~0.75 m ahead): selected target, axis, step size, control reminder.

## Files

- `src/vr/VrStatusPanelOffsetTuner.cs`
- `src/utility/config.cs` → `vr_status_panel_offset_tune`
- Hooks in `VrController.TickVrInput` / `VrHudStatusPanels`
