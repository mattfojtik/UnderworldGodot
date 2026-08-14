# VR Open Issues

Canonical list for native Quest/OpenXR VR work. Agent skill copy: `.cursor/skills/underworld-vr-native/open-issues.md`. Keep both in sync when priorities change.

## Diagnostics

VR sessions write readable log files (also mirrored to Godot console):

| Log | Workspace (agent-readable) | Godot user data |
|-----|---------------------------|-----------------|
| General VR / intro laser | `logs/vr_diag.log` | `%APPDATA%/Godot/app_userdata/Underworld/vr_diag.log` |
| Combat motion CSV | `logs/vr_combat_motion.log` | `%APPDATA%/Godot/app_userdata/Underworld/vr_combat_motion.log` |

Native VR always mirrors `VrDiagLog.Print`/`Warn` to those diag files (console + file). Use `VrDiagLog` for all VR diagnostics — see `.cursor/rules/vr-debug-logging.mdc`.

Settings in `user://settings.json`: `vr_diag_log` (default true), `vr_debug`, `vr_intro_debug` (intro/menu snapshots), `vr_combat_motion_log`.

---

## High priority

### Close object look misses
Nearby floor objects (e.g. bedroll) sometimes return "you see nothing" when laser should hit. Must fix with controller laser only (not head gaze).

---

## Medium priority

### Automap on-screen keyboard occlusion
When the map keyboard sits over typed text / notes, you can’t see what you’re typing. Reposition, fade, or peek-through so input stays readable.

### Automap pointing: quill tip vs cursor
Decide whether map paint/write should use the **quill tip** or the generic pointer. **Check DOS and Hank flat first**, then match VR to the chosen semantic.

### Death / sapling / abyss windows
VR cinema TV: black backdrop + cuts only (no HUD/status chrome). Death `0x103`, sapling `0x102`, and look-stills (e.g. `cs400` windows). Palette/color issues still TBD vs Hank flat.

### Inventory / conversation hit targets
- **Get + release** over inventory chrome that isn’t a valid slot: object stays in hand (maybe keep; or expand valid place hitboxes).
- Conversation option lines are hard to hit consistently — widen hit strips / scroll targeting.
- Possibly broaden other status-panel aim assists where laser precision hurts UX.

### Telekinesis / poles
VR reach and feedback for telekinesis and fishing/pole use (beam length vs `CanReach` messages, laser vs DOS range parity).

### Test lefty mode
Full lefty / dominant-hand regression: lasers, HUD hand, combat/ranged gestures, status panels, menus, and throw/spell aim after panel offsets landed.

### Playtests
- **Sleep and dreaming** (bedroll / dream sequences in VR).
- **Level 1** full playthrough (interactions, combat, spells, UI, saves).

### Investigate all sound modes
Audit every `synth` / music+SFX backend combination (OPL, soundfont, digital VOC, UW1 vs UW2) so cast/hit/UI sounds stay correct and nothing falls back to wrong assets (e.g. intro VOC). Confirm VR avatar-positioned playback for each mode.

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

- **Options / pause** head-locked UX parity with exploration HUD
- **Save/load from VR** (hand HUD path) after load-hang awareness
- **Lighting / palette** on cutscenes beyond death (intro already had special cases)
- Periodically **merge Hank upstream** and re-run interaction parity checklist

---

## Recently landed (keep for regression)

- Talk range scaled with world size; Talk/Look pick length not body-sphere clamped
- Swim play-space dunk + body marker; recenter no longer stacks swim Y
- Use-on key uses world sprite shader (same as Get-held)
- Head-locked active spells + dedicated chain widget
- Mage cheat: `'` / `` ` `` / `~` while in-game (message scroll confirms)
- Chain crop tuned for flask click separation
- **Aimed spells + ranged (complete):** laser absolute aim; targeting cursor on laser; spawn ~2 m along laser; caster self-hit only when world-near (DOS run-into preserved; VR tile-AABB false positives ignored); cast SFX on launch; ranged charge/release = stab-plane gesture
- **Status-panel offset tuner (side branch `vr-status-panel-offset-tuner`):** in-headset nudge; dialed offsets persisted to `settings.json` — do not merge the tuner; copy final defaults when ready

## Confirmed working

- 3D laser pickup, doors, world interact
- Tile look (walls/floor/ceiling) and far "you see nothing"
- Inventory sprites after HUD viewport move
- Conversations via hand HUD + message scroll
- VR input during conversation/automap (`ShouldTickVrInput`)
- Smooth forward locomotion (XROrigin interpolates between ~10 Hz DOS motion steps at XR frame rate)
- Off-hand Look/Talk laser; dominant Get/Use
- Head-locked status panels (flasks, inventory, runes, stats, conversation, spells, chain)
- Aimed projectile spells (magic missile, fireball, …) along dominant laser
- Ranged weapons: stab-plane draw charge + forward thrust release; laser aim reticle while charging

## Related tracking

| Doc | Role |
|-----|------|
| [vr-controls.md](vr-controls.md) | Binding scheme + **done** implementation checklist |
| [vr-interaction-parity.md](vr-interaction-parity.md) | DOS vs Hank semantics; re-test when Hank lands DOS alignment |
| [../backlog.md](../backlog.md) | Hank upstream feature/bug backlog (not VR-specific) |
| Cursor session todos | Ephemeral per-chat only — not the source of truth |

See `.cursor/skills/underworld-vr-native/open-issues.md` for file map and failed approaches.
