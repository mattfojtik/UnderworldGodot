# VR Interaction Parity (DOS vs Hank flat UI)

Reference for native VR interaction design. Captures Ultima Underworld **DOS** mouse semantics (playtested), how **Hank’s Godot flat UI** currently behaves, and rules so VR controller code stays robust when Hank later aligns flat UI with DOS.

**Project stance (this fork / VR work):** Match **DOS functionality when possible**. Adapt when VR feel requires it. Do **not** hard-code Hank’s current flat-UI quirks as the long-term contract.

Related: [vr-controls.md](vr-controls.md) (VR binding scheme), Hank’s [`backlog.md`](../backlog.md) (explicit unfinished DOS UI items).

---

## Intent

| Source | Role |
|--------|------|
| **DOS UW1/UW2** | Canonical interaction semantics for verbs, inventory, press vs release. |
| **Hank flat UI (today)** | Incomplete / simplified port. Not the target schema. |
| **Native VR** | Map DOS *semantics* onto controllers; change bindings or timing only when VR feel demands it. |

Hank’s own docs state the engine should “act more like the Underworld games originally worked” and that controls are “mainly based on the original.” Gaps below are unfinished work, not a permanent redesign. Several are on his backlog:

- `mouse based movement using 3d window`
- `clickable movement buttons in uw1 located above the compass`
- `Picking up and other interaction mode changes. -> Solution is likely to track Action separate from interaction mode.`
- `drag&drop objects on paperdoll`
- `Different mouse cursors for interaction modes`

---

## DOS — 3D world

Left mouse is **always** for movement (click regions in the 3D view). Interaction verbs use **right mouse**, with press/release semantics that matter.

| Mode | Right mouse | Notes |
|------|-------------|--------|
| **Talk** | **Release** talks | |
| **Get** | **Down** picks up into hand; **release** throws / drops / places in inventory | Held object + release is the place/throw path |
| **Look** | **Down** looks | |
| **Combat** | **Down** charges; **release** attacks | |
| **Use** | **Release** uses | Exception: key on door unlocks on **mouse down**, not up |

Left click does **not** fire Talk/Get/Look/Use/Combat verbs in the world.

---

## DOS — inventory panel

| Input | Behavior |
|-------|----------|
| Left or right **click-and-drag** | Moves items: pick up on **mouse down**, place on **mouse release** |
| Click **without** dragging | Mode-dependent; action on **mouse release** |

### Click (no drag) by mode

| Mode | Left click | Right click |
|------|------------|-------------|
| **No mode** / Talk / Get / Look / Combat | Use | Look |
| **Use** | Use | Use |

DOS has a true **no interaction-mode** state (neither Talk/Get/Look/Combat/Use selected). Inventory left/right still do Use / Look in that state.

---

## Hank flat UI (current) — 3D world

Observed in current UnderworldGodot (not VR):

| Topic | Behavior |
|-------|----------|
| Movement | **No** mouse movement in 3D window (WASD / keys). Backlog item. |
| Verb timing | Most verbs fire on **mouse down** (`Pressed`), not release |
| Left vs right | Left **or** right down generally runs the same world interact path |
| **Talk** | Left or right **down** talks |
| **Get** | Left or right **down** puts object in hand; release does nothing. **Second** click down throws/drops/places; release does nothing |
| **Look** | Left or right **down** looks |
| **Combat** | Right **down** charges; **release** attacks (**matches DOS**) |
| **Use** | Left or right **down** uses |

Implementation touchpoints: `main._Input` (viewport clicks only on `Pressed`), `uimanager.ClickOnViewPort` / `InteractWithObjectCollider`, `combat_input.cs` (hold right / release for attack).

---

## Hank flat UI (current) — inventory

| Topic | Behavior |
|-------|----------|
| Modes | **Always** in a mode; default is often **Use**. No DOS “no mode.” |
| Timing | Actions on **mouse down** (`_paperdoll_gui_input` requires `Pressed`) |
| Drag-drop | **Not** implemented (backlog: `drag&drop objects on paperdoll`) |

### Clicks by mode (Hank today)

| Mode | Left click down | Right click down |
|------|-----------------|------------------|
| **Talk / Get** | Place object in hand | Use |
| **Look** | Look | Use + switch to **Use** mode |
| **Combat / Use** | Use | Place in hand + switch to **Get** mode |

Mode auto-toggles on some right-clicks are part of the “action vs interaction mode” mess Hank noted in the backlog.

---

## Side-by-side summary

### World

| Concern | DOS | Hank today |
|---------|-----|------------|
| LMB in 3D | Movement | Unused for move; often same as RMB interact |
| Verb button | RMB | LMB or RMB |
| Talk / Use timing | Mostly **release** (key-on-door: down) | **Down** |
| Get | Down = pick; release = throw/drop/place | Down = pick; second down = throw/drop/place |
| Look | Down | Down |
| Combat | Down charge / release attack | Same (aligned) |

### Inventory

| Concern | DOS | Hank today |
|---------|-----|------------|
| Drag-drop | Yes (down pick / release place) | No |
| Click timing | **Release** | **Down** |
| No mode | Yes (L=Use, R=Look) | No — always in a mode |
| Mode L/R map | See DOS table | See Hank table (differs; toggles modes) |

---

## VR design rules (this fork)

See also [vr-controls.md](vr-controls.md). Principles that keep VR robust against flat-UI churn:

### 1. Prefer semantic verbs over Hank’s current mouse wiring

VR should call **verb-level APIs** (`PerformVrObjectInteraction`, pickup/use/look/talk helpers, inventory slot handlers with an explicit verb) rather than:

- Synthesizing left/right mouse and hoping Hank’s mode table stays stable
- Assuming “second click throws” instead of “release / place verb”
- Assuming inventory left = get and right = use forever

When Hank splits **Action** from **InteractionMode**, VR code that already passes an explicit verb will need less rework.

### 2. Treat DOS press/release as the semantic model; map to VR buttons

| DOS semantic | VR-friendly mapping (current scheme) |
|--------------|--------------------------------------|
| Get (pick into hand) | Dominant **grip press** while aiming |
| Get (throw / drop / place) | Dominant **grip release** — HUD if laser on panel, else world throw/drop |
| Use | Dominant **trigger release** while aiming (world or inventory) |
| Look | Off-hand **trigger** |
| Talk | Off-hand **grip** |
| Combat charge/release | Motion (`VrCombatMotion`), not mouse hold |
| Inventory Use (HUD) | Dominant **trigger release** → always ModeUse (`PushVrHudUseClick`) |
| Inventory Get (HUD) | Dominant **grip** press/release (`PushVrHudGetClick`) |

If flat UI later moves Use/Talk to **release**, VR can keep **button edge** (press or release) as a deliberate VR feel choice — document the choice; do not silently follow Hank’s down-only path.

### 3. Isolate Hank quirks behind adapters

Keep a thin boundary in `VrController` / UI helpers:

- **World verbs:** aim ray + `InteractionModes` (or future Action enum) → one call site
- **HUD / inventory clicks:** UV hit → `PushHudMouseClick` / `PushVrHudMouseClick` **or** direct inventory APIs when available
- **Mode coupling:** if empty-slot place still requires `ModePickup`, wrap it locally (`PushVrHudMouseClick` pattern) and comment that it is a **compatibility shim** until Hank’s inventory matches DOS

Avoid scattering `InteractionModeToggle(ModePickup)` across VR paths.

### 4. Do not depend on “always in a mode”

DOS has no-mode inventory Use/Look. Hank does not. VR exploration already avoids HUD mode buttons. When inventory click semantics change, prefer:

- Explicit “place held object in this slot”
- Explicit “use / look at this slot”

over reading `uimanager.InteractionMode` as the player’s intent.

### 5. Combat is already the good template

Flat combat hold/release matches DOS. VR melee should stay on **motion charge/release**, not on inventing a second mouse-like path that will fight future flat-UI changes.

### 6. Movement is out of scope for mouse parity in VR

DOS LMB movement will not return as “laser left click = walk.” Locomotion stays sticks ([vr-controls.md](vr-controls.md)). When Hank adds mouse move for flat, ignore it for VR.

---

## What to re-test when Hank lands DOS alignment

When backlog items for pickup/action-vs-mode, drag-drop, or mouse movement land:

1. **World Get** — VR uses grip press/release (DOS-aligned). If flat UI gains release-to-throw, keep VR on grip release; do not reintroduce a second click.
2. **World Use / Talk** — Press vs release; key-on-door still special-cased on down?
3. **Inventory** — Drag-drop + release-to-place; no-mode Use/Look table
4. **Mode toggles** — Do right-clicks still force Get/Use mode switches? VR must not inherit those toggles
5. **Empty slot place** — Still requires `ModePickup`, or works from any mode / no mode?

Update the Hank tables in this file when behavior changes; keep the DOS tables as the baseline unless DOS playtesting corrects them.

---

## Code map (current)

| Area | Primary files |
|------|----------------|
| Flat viewport click (down) | `main.cs` `_Input`, `uimanager_views.cs` |
| World verbs by mode | `uimanager_interaction.cs` |
| Inventory slot clicks | `uimanager_paperdoll.cs`, `uimanager_inventory.cs` |
| Combat hold/release | `src/interaction/combat/combat_input.cs` |
| VR verbs / HUD clicks | `src/vr/VrController.cs`, `VrHudStatusPanels.cs` |
| VR bindings spec | `docs/vr-controls.md` |
| Hank unfinished UI | `backlog.md` |

---

## Changelog

| Date | Note |
|------|------|
| 2026-08-11 | Initial write-up from DOS playtest notes + Hank flat observation + backlog/README review. |
