# VR Open Issues

Canonical list for native Quest/OpenXR VR work. Agent skill copy: `.cursor/skills/underworld-vr-native/open-issues.md`.

## Diagnostics

VR sessions write readable log files (also mirrored to Godot console):

| Log | Workspace (agent-readable) | Godot user data |
|-----|---------------------------|-----------------|
| General VR / intro laser | `logs/vr_diag.log` | `%APPDATA%/Godot/app_userdata/Underworld/vr_diag.log` |
| Combat motion CSV | `logs/vr_combat_motion.log` | `%APPDATA%/Godot/app_userdata/Underworld/vr_combat_motion.log` |

Settings in `user://settings.json`: `vr_diag_log` (default true), `vr_debug`, `vr_intro_debug` (intro/menu snapshots), `vr_combat_motion_log`.

## High priority

### Close object look misses
Nearby floor objects (e.g. bedroll) sometimes return "you see nothing" when laser should hit. Must fix with controller laser only (not head gaze).

## Medium priority

- Optional world laser when HUD is hidden (Y toggle)
- Snap turn sampled at physics rate instead of motion tick
- Automap VR UX untested

## Low priority

- **VR laser reach vs `CanReach` (minor):** Laser tip uses avatar-centered sphere radius from `PickupDistance`/`UseDistance`; game `CanReach` also checks Z height and pole/swim offsets. Edge-case mismatch with "you cannot reach that" possible — refine if needed.

## Confirmed working

- 3D laser pickup, doors, world interact
- Tile look (walls/floor/ceiling) and far "you see nothing"
- Inventory sprites after HUD viewport move
- Conversations via hand HUD + message scroll
- VR input during conversation/automap (`ShouldTickVrInput`)
- Smooth forward locomotion (XROrigin interpolates between ~10 Hz DOS motion steps at XR frame rate)

See `.cursor/skills/underworld-vr-native/open-issues.md` for full detail, file map, and failed approaches.
