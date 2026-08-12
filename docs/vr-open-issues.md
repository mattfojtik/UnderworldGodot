# VR Open Issues

Canonical list for native Quest/OpenXR VR work. Agent skill copy: `.cursor/skills/underworld-vr-native/open-issues.md`. Keep both in sync when priorities change.

## Diagnostics

VR sessions write readable log files (also mirrored to Godot console):

| Log | Workspace (agent-readable) | Godot user data |
|-----|---------------------------|-----------------|
| General VR / intro laser | `logs/vr_diag.log` | `%APPDATA%/Godot/app_userdata/Underworld/vr_diag.log` |
| Combat motion CSV | `logs/vr_combat_motion.log` | `%APPDATA%/Godot/app_userdata/Underworld/vr_combat_motion.log` |

Settings in `user://settings.json`: `vr_diag_log` (default true), `vr_debug`, `vr_intro_debug` (intro/menu snapshots), `vr_combat_motion_log`.

---

## High priority

### Aimed spells / ranged go the wrong way
~~Magic missile, fireball, and similar aimed projectiles launch in the wrong direction.~~ **Fixed:** player spells (`ProjectileSpell`) and ranged `MissileRelease` use `VrController.ApplyLaserAimToProjectile` (same absolute laser heading/pitch as object throw). Viewport mouse / head aim no longer drives VR projectile yaw.

### Close object look misses
Nearby floor objects (e.g. bedroll) sometimes return "you see nothing" when laser should hit. Must fix with controller laser only (not head gaze).

### Live status-panel offset debug mode
Need a debug mode to nudge **X/Y/Z offsets for every head-locked status panel** live in-headset (no JSON edit + restart between trials). Persist to `settings.json` / schema when dialed in. Covers flasks, inventory, runes, stats, chain, spells, conversation, compass, gem, eyes, weapon anim, global Y, etc.

---

## Medium priority

### Automap on-screen keyboard occlusion
When the map keyboard sits over typed text / notes, you can’t see what you’re typing. Reposition, fade, or peek-through so input stays readable.

### Automap pointing: quill tip vs cursor
Decide whether map paint/write should use the **quill tip** or the generic pointer. **Check DOS and Hank flat first**, then match VR to the chosen semantic.

### Death / sapling screens
On death (and sapling), don’t show the full VR menu/status clutter — **death animation only**. Also a **color/palette bug**; verify whether Hank flat has the same issue before fixing VR-only.

### Inventory / conversation hit targets
- **Get + release** over inventory chrome that isn’t a valid slot: object stays in hand (maybe keep; or expand valid place hitboxes).
- Conversation option lines are hard to hit consistently — widen hit strips / scroll targeting.
- Possibly broaden other status-panel aim assists where laser precision hurts UX.

### Playtests
- **Sleep and dreaming** (bedroll / dream sequences in VR).
- **Level 1** full playthrough (interactions, combat, spells, UI, saves).

### Load hang (lost repro)
A particular save once hung the game on load; file is gone but **may recur**. If it returns: capture save + `vr_diag.log`, note last UI/mode, check conversation/`blockinput`/level-load paths. Don’t over-invest without a repro.

### Existing medium polish
- Optional world laser when HUD is hidden (Y toggle)
- Snap turn sampled at physics rate instead of motion tick
- Chain crop / flask click-target tuning (mid-crop between full Chains rect and tight crop)

---

## Low priority

- **VR laser reach vs `CanReach` (minor):** sphere vs Z/pole/swim edge cases → “you cannot reach that”
- UW2 conversation layout on hand HUD
- Document recenter / swim dunk for new players
- Attack-mode laser visibility when HUD hidden

---

## Suggested additions (not yet prioritized)

- **Combat gesture vs laser conflict** while casting / holding missile weapon — confirm modes don’t steal aim
- **Telekinesis / pole** reach feedback in VR (beam length vs messages)
- **Options / pause** head-locked UX parity with exploration HUD
- **Save/load from VR** (hand HUD path) after load-hang awareness
- **Lighting / palette** on cutscenes beyond death (intro already had special cases)
- **Lefty / dominant-hand** full regression after panel offset debug lands
- Periodically **merge Hank upstream** and re-run interaction parity checklist

---

## Recently landed (keep for regression)

- Talk range scaled with world size; Talk/Look pick length not body-sphere clamped
- Swim play-space dunk + body marker; recenter no longer stacks swim Y
- Use-on key uses world sprite shader (same as Get-held)
- Head-locked active spells + dedicated chain widget
- Mage cheat: `'` / `` ` `` / `~` while in-game (message scroll confirms)
- Chain crop halfway between flat Chains rect and prior tight crop

## Confirmed working

- 3D laser pickup, doors, world interact
- Tile look (walls/floor/ceiling) and far "you see nothing"
- Inventory sprites after HUD viewport move
- Conversations via hand HUD + message scroll
- VR input during conversation/automap (`ShouldTickVrInput`)
- Smooth forward locomotion (XROrigin interpolates between ~10 Hz DOS motion steps at XR frame rate)
- Off-hand Look/Talk laser; dominant Get/Use
- Head-locked status panels (flasks, inventory, runes, stats, conversation, spells, chain)

## Related tracking

| Doc | Role |
|-----|------|
| [vr-controls.md](vr-controls.md) | Binding scheme + **done** implementation checklist |
| [vr-interaction-parity.md](vr-interaction-parity.md) | DOS vs Hank semantics; re-test when Hank lands DOS alignment |
| [../backlog.md](../backlog.md) | Hank upstream feature/bug backlog (not VR-specific) |
| Cursor session todos | Ephemeral per-chat only — not the source of truth |

See `.cursor/skills/underworld-vr-native/open-issues.md` for file map and failed approaches.
