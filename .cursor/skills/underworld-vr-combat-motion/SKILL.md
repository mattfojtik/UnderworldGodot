---
name: underworld-vr-combat-motion
description: VR melee gesture classification and motion-capture tuning for UnderworldGodot. Covers VrCombatMotion, VrCombatMotionLog, stab/slash/bash thresholds, pullback sensitivity, and log analysis. Use when tuning VR combat gestures, misclassified swings, stab charge distance, or vr_combat_motion.log calibration.
---

# Underworld VR Combat Motion

Native VR melee uses **weapon-hand pullback → charge → thrust release** in torso-local space. Classification runs at charge start and release; `combat.WeaponSwingTypePlayer` is 0=slash, 1=bash, 2=stab.

## Key files

| File | Role |
|------|------|
| `src/vr/VrCombatMotion.cs` | State machine, thresholds, `ClassifySwing` / `ClassifyReleaseStroke` |
| `src/vr/VrCombatMotionLog.cs` | CSV log to `user://vr_combat_motion.log` |
| `src/vr/VrController.cs` | Calls `VrCombatMotion.Tick()`; A=jump marker; X=close log session |
| `src/interaction/combat/combat_input.cs` | Uses `VrCombatMotion.IsAttackHeldDown`; VR charge anim swing-type refresh |
| `src/utility/config.cs` | `vr_combat_motion_log` (default true) |

Combat mode: right B toggles `ModeAttack` (`uimanager_interaction.ToggleVrCombatMode`). Laser hidden while attacking unless a spell is armed.

## Coordinate frame

`VrController.WorldToTorsoLocal(controller.GlobalPosition)` — Z negative = pull toward body (back), Y up, X lateral.

Logged derived columns (right-handed):
- `slash_side` = `-windup_x` (leftward slash → negative)
- `up` = `windup_y`
- `back` = `-windup_z`

`GetSlashSideComponent` flips sign for lefties.

## Motion state machine

1. **Idle** → track forward guard Z; pull back **4 cm** from guard → **PullingBack** (stroke start = actual hand)
2. **PullingBack** → **6 cm** back from frozen guard Z → **Charging**
3. **Charging** → forward thrust past peak by `ReleaseForwardThreshold` → release

Guard pose tracks the most forward relaxed hand Z while idle. Charge distance is relative to guard, but **stroke start stays at the real hand position** for classification (do not set stroke start to the guard/rest pose).

Current thresholds in `VrCombatMotion.cs`:
- `PullBackDetect` 0.04 m, `PullBackCharge` 0.06 m, `ReleaseForwardThreshold` 0.08 m

## Classifier rules (priority order)

**Degenerate windup** (`back` and `up` both < 0.04 — peak ≈ stroke start):
- Release: lateral thrust or `peak_depth` ≥ 0.20 → slash; else stab (never bash)
- Do not use old “high side + high back = bash” — it misclassifies lateral slashes

**Bash**: `up ≤ -0.23` and `back ≥ 0.16` (strong downward windup)

**Stab pocket**: `back < 0.14`, `|side| < 0.12`, `up > -0.20` → stab before slash

**Slash**: lateral windup — righty `slash_side ≤ -0.075`, lefty `slash_side ≥ 0.075`, with `|side| ≥ 0.08` and `back ≥ 0.10`

**Release thrust fallback**: shallow stabs with strong lateral thrust (`|thrust_x|` vs `|thrust_z|`)

Default: stab.

## Calibration protocol

Ask the player to run in VR combat mode with weapon drawn:

1. **10 stabs** (short backward pull, thrust forward)
2. Press **A** (jump marker)
3. **10 slashes** (lateral windup)
4. Press **A**
5. **10 bashes** (upward/downward overhand windup)
6. Quit with **X** (closes log session)

Log path on PC: `%APPDATA%/Godot/app_userdata/Underworld/vr_combat_motion.log`

## Tuning workflow

1. Read latest session (after last `session_start` with `combat_mode_on`)
2. Segment `release` rows by `jump_marker` → intended stab / slash / bash
3. Compare `classifier_swing` vs intended; summarize `up`, `back`, `slash_side`, `peak_depth`, `thrust_x/y/z`
4. Adjust constants in `VrCombatMotion.cs`; rebuild; one calibration pass validates
5. Prefer rules that work on the **latest** session; note when older sessions disagree (gesture style drift)

Use PowerShell to parse CSV if Python is unavailable. See [reference.md](reference.md) for column indices and example scripts.

## Common failure modes

| Symptom | Likely cause | Fix |
|---------|--------------|-----|
| Slash → bash | Legacy high-side bash rule or zero-windup → bash | Bash only on negative `up`; degenerate → slash/stab |
| Bash → stab | `BashNegativeUpMax` too strict or `BashMinBack` too high | Lower magnitude of up threshold (e.g. -0.23) or back (0.16) |
| Stab → slash | High `|side|` noise or deep slash-like pull | Widen stab pocket or raise `SlashLateralSide` |
| Won’t charge | `PullBackCharge` too high | Lower toward ~0.07; check `charge_start` `peak_depth` in log |
| Wrong charge anim | Swing type set after charge anim started | `combat_input._vrChargeAnimSwingType` refresh on type change |

## Head pitch (Y)

Melee hit height uses head pitch via `VrController.GetHeadPitchUw()` / `SyncCombatAimFromHead()` at strike — separate from gesture classification.

## Deferred

VR ranged attacks and spellcasting — not in this skill.
