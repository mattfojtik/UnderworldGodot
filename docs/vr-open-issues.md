# VR Open Issues

Canonical list for native Quest/OpenXR VR work. Agent skill copy: `.cursor/skills/underworld-vr-native/open-issues.md`.

## High priority

### Movement jitter when running forward
Regular forward run feels jaggy with occasional small backward hitch. Worse running in initial spawn direction than opposite. ~10 Hz motion sim vs XR frame rate is suspected. Smoothing XROrigin and head-aim picking were tried and reverted/did not help.

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

See `.cursor/skills/underworld-vr-native/open-issues.md` for full detail, file map, and failed approaches.
