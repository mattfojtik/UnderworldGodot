# VR Open Issues (UnderworldGodot)

Last updated Aug 2026. Canonical user-facing copy: `docs/vr-open-issues.md` — keep in sync.

## High priority

### Close object look misses ("you see nothing")
- Nearby objects (e.g. bedroll) sometimes miss in look mode.
- Controller laser only — no head aim.
- Ideas: floor-sprite AABB/quad pick; physics fallback; vision refresh before pick.

### Live status-panel offset debug mode
- In-headset X/Y/Z nudge for every head-locked widget; write through to settings without restart.
- Widgets: health/mana/compass/inventory/runes/stats/shelf/spells/chain/conversation/weapon/gem/eyes + global Y.

## Medium priority

### Automap keyboard occlusion
- Typed map text hidden behind on-screen keyboard — keep typing visible.

### Automap pointing: quill tip vs cursor
- Research DOS + Hank flat first; then align VR.

### Death / sapling screens
- Show death animation only (no full menu/status clutter).
- Color/palette bug — compare Hank flat before VR-only fix.

### Inventory / conversation hit targets
- Invalid inventory release keeps object in hand — keep or expand place hitboxes.
- Conversation lines hard to hit — widen strips / targeting assist.

### Playtests
- Sleep / dreaming.
- Level 1 playthrough.

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

- Telekinesis / pole reach feedback.
- Options/pause head-locked UX.
- Save/load from VR after hang awareness.
- Cutscene lighting/palette beyond death.
- Lefty/dominant regression after offset debug.
- Periodic Hank upstream merge + parity re-test.

## Recently landed (regression watch)

- Talk range × world scale; Talk/Look not body-sphere clamped
- Swim origin dunk + body marker; recenter swim-Y fix
- Use-on key = world sprite shader (Get-held look)
- Active spells + chain status widgets; mage cheat
- Chain crop tuned vs flasks
- **Aimed spells + ranged (complete):** laser absolute aim; cursor at 2 m; spawn 1 m along laser; cast SFX; ranged = stab-plane charge/release

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
