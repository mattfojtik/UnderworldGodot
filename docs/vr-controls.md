# VR Controls (Native OpenXR)

Design spec for UnderworldGodot native VR (Quest 3 primary target).  
Implementation status: **in progress** — core verb bindings below are wired; see checklist for remaining items.

## Design principles

1. **Verbs on buttons, not HUD modes** — Look, Get, Use, and Talk are separate buttons while aiming with the controller laser. Players do not switch interaction modes on the HUD.
2. **Combat is a toggle, not a verb** — Entering combat blocks the four exploration verbs. Leaving combat restores them.
3. **Melee is motion-only** — Charge and release attacks use `VrCombatMotion` gesture planes only (no grip-hold to swing).
4. **Spells work in or out of combat** — Same as DOS. Rune bag, rune selection, and casting are always available via inventory UI.
5. **DOS UI in floating panels** — Map, stats, runes, and sleep use inventory / status overlays and laser clicks, not F-key shortcuts.
6. **Fixed locomotion, swapped interaction** — Left stick / right turn match Xbox/PS and most VR defaults. Handedness swaps **aim hand**, **HUD wrist**, and **grip/trigger verbs** only.

## Handedness

| | Right-handed (default) | Left-handed (`isLefty`) |
|---|------------------------|-------------------------|
| **Aim / laser / weapon hand** | Right | Left |
| **Get / Use** | Dominant grip / trigger | Same (dominant hand) |
| **Talk / Look** | Off-hand grip / trigger | Same (off-hand) |
| **Hand HUD panel** | Left wrist | Right wrist |
| **Move / snap turn** | Left / right stick | **Unchanged** (fixed) |
| **Face buttons** | Physical positions | **Unchanged** (fixed) |

Implement via `GetDominantController()` / `GetOffHandController()` (extend existing weapon-hand logic). Laser picking must follow the dominant controller, not a hardcoded right hand.

## Locomotion (everyone)

| Input | Action |
|-------|--------|
| Left stick | Move forward/back, strafe |
| Right stick X | Snap turn (45°, cooldown) |
| **Right stick click** | **Recenter view** (headset XZ over avatar) |
| Right A | Jump |

Movement uses head-relative yaw while the stick is active (`TryGetMotionYaw`).

**Not used:** left stick click (reserved; accidental presses while moving).

## Face buttons (everyone, fixed positions)

| Button | Action |
|--------|--------|
| Right A | Jump |
| Right B | **Combat toggle** (draw / sheathe) |
| Left Y | Toggle head-locked **status overlays** (flasks, scroll, inventory strip, etc.) |
| Left menu | Toggle **hand HUD** panel |
| Left X | Cast prepared spell when runes are ready |

**Removed / not in VR:**

- Left X quit game — exit via HUD menu.
- Left grip door cheat — doors use **Use** (trigger) on aimed door; keys still required where applicable.

## Exploration (out of combat)

Aim with the **dominant-hand laser** (cyan) for Get/Use, and the **off-hand laser** (green) for Look/Talk:

| Verb | Hand / laser | Binding (righty) | Binding (lefty) | Timing |
|------|--------------|------------------|-----------------|--------|
| **Get** | Dominant | Right grip | Left grip | **Press** picks up (world or inventory slot); **release** throws / drops / places in inventory (DOS-aligned) |
| **Use** | Dominant | Right trigger | Left trigger | **Release** (DOS-aligned) |
| **Look** | Off-hand | Left trigger | Right trigger | Press |
| **Talk** | Off-hand | Left grip | Right grip | Press |

**Get** is a hold gesture: keep grip held after grabbing, aim the laser, then release to place on the HUD inventory or throw/drop in the world. There is no second click.

If the player presses Get / Use / Look / Talk while **in combat**, block the action and show feedback (DOS did not allow those modes during attack).

**Cancel / back** (menus, yes/no, typed quantity, cutscenes, **automap**, **in-game options**): **left grip** when a prompt is open (fixed side, all players).

**Automap:** dominant cyan laser tracks the hand HUD; **trigger** clicks map controls. Click the map to start a note — an on-screen keyboard appears at the bottom of the HUD. **Done** saves the note; **left grip** cancels writing or closes the map.

## Combat

### Toggle

- **Right B** enters/exits combat (weapon draw / put away).
- Replaces legacy right-grip combat toggle.

### While combat is ON

| Action | Input |
|--------|--------|
| Melee charge / release | **Motion only** (`VrCombatMotion` — slash / bash / stab planes) |
| Get / Use / Look / Talk | **Blocked** |
| Spells (UI) | Laser on inventory / rune overlays (unchanged) |
| Spell targeting | See [Spellcasting](#spellcasting) |
| Toggle combat off | Right B |

Grip and trigger on the **dominant hand are not used for melee**. They are reserved for spell targeting while a spell is armed.

### While combat is OFF

Dominant grip/trigger = Get / Use (exploration verbs above).

### Ranged weapons (bow, sling, etc.)

Melee motion planes do not apply. **Exception:** dominant **trigger** (or a dedicated ranged flow) for aim/fire until a ranged-specific scheme exists.

### Desktop

Flat mouse/keyboard combat paths remain for non-VR. In native VR, melee charge/release should come **only** from `VrCombatMotion`, not mouse right-click.

## Spellcasting

Works **in or out of combat** (DOS behavior).

### UI flow (laser on overlays)

1. Open **rune bag** from inventory (same as flat — swaps inventory panel to runes via `SetPanelMode(1)`).
2. Laser-click runes in the bag to select (up to three on the shelf).
3. **Head-locked rune shelf** mirrors the paperdoll rune bag while it is open (`SetPanelMode(1)`); laser-click runes and cast area.
4. **Cast:** laser-click the cast area on that panel (same as `SelectedRunesClick` / flat game), or optional **left X** shortcut when runes are ready.

### After spell is armed (`SpellCasting.currentSpell != null`)

| Input | Action |
|-------|--------|
| Dominant **trigger** | Cast / apply at laser hit (targeted spells, projectiles) |
| Dominant **grip** | Cancel spell / clear selection |
| Off-hand grip/trigger | Unused in combat (Talk/Look disabled) |

**Rule of thumb:** arming or targeting a spell should block starting a new melee charge until cast or cancel (avoids accidental swings while aiming).

## Inventory & DOS panels (no extra face buttons)

| Feature | VR access |
|---------|-----------|
| **Map** | Click map/compass object in inventory overlay (same as flat). No dedicated map button. |
| **Stats** | Laser-click **pull chain** on floating inventory/runes/stats status panel → `ChangePanels()`. Head-locked stats panel appears when open. |
| **Runes** | Laser-click rune bag in inventory. Head-locked rune bag + selected-rune shelf overlays. |
| **Sleep / camp** | Use bedroll in world or click bedroll in inventory. No F10 equivalent in VR. |

F-keys in `main.cs` are dev/debug shortcuts only; VR does not replicate them.

## HUD & overlays

| Input | Action |
|-------|--------|
| Left menu | Hand HUD (1280×800 panel on off-hand controller) |
| Left Y | Head-locked status overlays (message scroll, flasks, gem, inventory strip, conversation portrait, rune shelf, …) |
| Dominant trigger (while pointing at inventory) | **Use** on **release** (always ModeUse) |
| Dominant trigger (other HUD chrome) | UI mouse left click on press |
| Off-hand grip (while pointing at HUD) | UI mouse right click |
| Dominant grip | **Get only** — press pick / release place when holding (not a generic HUD click) |

During quantity prompts (“Move how many?”), a **head-locked number pad** appears; laser + trigger to press keys; left grip cancels.

## OpenXR / multi-headset notes

- Bind by OpenXR action names (`trigger_click`, `grip_click`, `thumbstick_click`, `ax_button`, `by_button`, …) with aliases where vendors differ (`a_button`/`x_button`, `b_button`/`y_button`).
- Map controls by **tracker role** (`left_hand` / `right_hand`) and **dominant/off-hand**, not “Quest right controller.”
- Menu button exists on left controller on Quest; test WMR/Index fallbacks for `menu_button`.

## Implementation checklist

- [x] B = combat toggle; remove right-grip combat toggle
- [x] Dominant/off-hand grip/trigger verbs (exploration); Get = grip press/release
- [x] Block Get/Use/Look/Talk in combat
- [x] VR melee: `VrCombatMotion` only (no grip charge)
- [x] Spell targeting: dominant trigger/grip when `currentSpell != null`
- [x] Right stick click = recenter; remove B recenter
- [x] HUD on off-hand per `isLefty`
- [x] Laser from dominant controller everywhere
- [x] Remove `ApplyDoorInteraction` (left-grip door cheat)
- [x] Left X = cast when runes ready
- [x] Pull chain on inventory status overlay → stats
- [x] Rune shelf head-locked status panel + cast click
- [x] Conversation portrait head-locked status panel
- [x] Stats head-locked status panel

## Related docs

- [vr-interaction-parity.md](vr-interaction-parity.md) — DOS vs Hank flat UI semantics; keep VR robust when flat UI aligns to DOS
- [vr-open-issues.md](vr-open-issues.md) — open VR bugs and polish
- `.cursor/skills/underworld-vr-native/SKILL.md` — architecture and locomotion interpolation
- `.cursor/skills/underworld-vr-interaction/SKILL.md` — laser pick pipeline
- `.cursor/skills/underworld-vr-combat-motion/SKILL.md` — gesture planes and tuning

## Key source files

| File | Concern |
|------|---------|
| `src/vr/VrController.cs` | Bindings, laser, HUD, recenter, combat toggle |
| `src/vr/VrCombatMotion.cs` | Melee gesture charge/release |
| `src/ui/uimanager_interaction.cs` | Combat mode, interaction verbs |
| `src/ui/uimanager_panels.cs` | Inventory / stats / runes panel modes, chain |
| `src/ui/uimanager_runes.cs` | Rune selection and cast shelf |
| `src/vr/VrNumberPad.cs` | Stack quantity prompt |
| `src/vr/VrHudStatusPanels.cs` | Head-locked overlays |
