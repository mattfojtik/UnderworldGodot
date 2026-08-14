# VR Open Issues (UnderworldGodot)

Last updated Aug 2026. Canonical user-facing copy: `docs/vr-open-issues.md` — keep in sync.

## High priority

### Close object look misses ("you see nothing")
- Nearby objects (e.g. bedroll) sometimes miss in look mode.
- Controller laser only — no head aim.
- Ideas: floor-sprite AABB/quad pick; physics fallback; vision refresh before pick.

## Medium priority

### Automap keyboard occlusion
- Typed map text hidden behind on-screen keyboard — keep typing visible.

### Automap pointing: quill tip vs cursor
- Research DOS + Hank flat first; then align VR.

### Death / sapling / abyss windows
- VR cinema: black TV + cuts only (no HUD/status). Death/sapling cutscenes + look-stills (`cs400`).
- Color/palette bug — compare Hank flat before VR-only fix.

### Inventory / conversation hit targets
- Invalid inventory release keeps object in hand — keep or expand place hitboxes.
- Conversation lines hard to hit — widen strips / targeting assist.

### Telekinesis / poles
- VR reach + feedback for telekinesis and fishing/pole (`CanReach`, beam length, laser vs DOS range).

### Test lefty mode
- Full lefty / dominant-hand regression (lasers, HUD hand, combat/ranged, panels, menus, aim).

### Playtests
- Sleep / dreaming.
- Level 1 playthrough.

### Investigate all sound modes
- Audit every `synth` / music+SFX backend (OPL, soundfont, digital VOC, UW1 vs UW2) so cast/hit/UI sounds stay correct; no wrong VOC fallbacks; VR avatar-positioned playback for each mode.

### Load hang (lost repro)
- One save hung on load; file lost. Watch for recurrence; capture save + logs if it returns.

### Other medium
- Optional world laser when HUD hidden (Y toggle).
- Snap turn at physics rate (~10 Hz today).
- Chain vs flask click tuning (settled for now; re-open if needed).
- `UpdateViewPortMouseFromControllerAim` vs 3D laser consistency.

## Low priority / polish

- Laser reach vs `CanReach` Z/pole/swim edge cases.
- UW2 conversation UI on hand HUD.
- Document recenter / swim dunk.
- Attack-mode laser when HUD hidden (melee); ranged charge keeps laser on.

## Suggested additions

- Options/pause head-locked UX.
- Save/load from VR after hang awareness.
- Cutscene lighting/palette beyond death.
- Periodic Hank upstream merge + parity re-test.

## Recently landed (regression watch)

- Talk range × world scale; Talk/Look not body-sphere clamped
- Swim origin dunk + body marker; recenter swim-Y fix
- Use-on key = world sprite shader (Get-held look)
- Active spells + chain status widgets; mage cheat
- Chain crop tuned vs flasks
- **Aimed spells + ranged (complete):** laser absolute aim; cursor at 2 m; spawn 1 m along laser; cast SFX; ranged = stab-plane charge/release
- **Status-panel offset tuner** on `vr-status-panel-offset-tuner` (dialed offsets in settings; don’t merge tuner)

## Confirmed working

- Native VR object pickup / door / world interact via 3D laser pick
- VR inventory → held object on laser; throw/drop
- Chargen keyboard; intro menu TV
- Tile look; far "you see nothing"
- Conversation HUD + scroll selection
- Snap-turn-only play-space yaw; motionBlend locomotion
- Off-hand Look/Talk; status panels set
- Aimed projectile spells along dominant laser
- Ranged stab-plane charge + laser aim reticle

## Key files

| File | VR concern |
|------|------------|
| `src/vr/VrController.cs` | XR rig, laser, picking, HUD, origin sync, throw/spell aim |
| `src/vr/VrHudStatusPanels.cs` | Head-locked overlays / offsets |
| `src/vr/VrCombatMotion.cs` | Melee + ranged charge gestures |
| `src/magic/spellcasting*.cs` | Aimed projectile cast |
| `main.cs` | `ShouldTickVrInput`, cheats |
| `src/utility/config.cs` | VR settings / offsets |
