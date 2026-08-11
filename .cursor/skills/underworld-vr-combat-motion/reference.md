# VR Combat Motion Log Reference

## CSV columns (0-based index after split on comma)

| Index | Name |
|-------|------|
| 2 | `event` — sample, pull_start, charge_start, release, jump_marker, combat_mode_on/off, session_start/end |
| 6 | `classifier_swing` — -1 none, 0 slash, 1 bash, 2 stab |
| 22–24 | `thrust_x/y/z` (release only) |
| 25 | `slash_side` |
| 26 | `up` |
| 27 | `back` |
| 30 | `peak_depth` |

Quoted `extra` field may contain commas — prefer filtering lines with `,release,` / `,jump_marker,` via regex.

## Example: segment releases by jump markers (PowerShell)

```powershell
$path = "$env:APPDATA/Godot/app_userdata/Underworld/vr_combat_motion.log"
$lines = Get-Content $path
# Find calibration session: last session_start followed by combat_mode_on
$calStart = 0
for ($i = 0; $i -lt $lines.Count; $i++) {
  if ($lines[$i] -match ',session_start,' -and $lines[$i+1] -match ',combat_mode_on,') { $calStart = $i }
}
$rows = @()
for ($i = $calStart; $i -lt $lines.Count; $i++) {
  if ($lines[$i] -match ',(release|jump_marker),') { $rows += ,@($lines[$i] -split ',') }
}
$jumps = @()
for ($j = 0; $j -lt $rows.Count; $j++) { if ($rows[$j][2] -eq 'jump_marker') { $jumps += $j } }
$labels = @('stab','slash','bash')
$prev = 0
$boundaries = $jumps + @($rows.Count)
$names = @{0='slash';1='bash';2='stab'}
for ($si = 0; $si -lt 3; $si++) {
  $end = $boundaries[$si]
  Write-Host "`n=== $($labels[$si]) ==="
  for ($i = $prev; $i -lt $end; $i++) {
    if ($rows[$i][2] -ne 'release') { continue }
    $r = $rows[$i]
    Write-Host (" got={0} up={1:F3} back={2:F3} side={3:F3} depth={4:F3}" -f `
      $names[[int]$r[6]], [double]$r[26], [double]$r[27], [double]$r[25], [double]$r[30])
  }
  $prev = $end + 1
}
```

## Gesture signatures (latest calibration style)

**Stab**: low `back` (0.03–0.13), low `|side|`, small positive `up`

**Slash**: negative `slash_side` (righty), `|side|` ≥ 0.08, `back` ≥ 0.10, small or positive `up`

**Bash**: `up` ≤ -0.25, `back` ≥ 0.16, low `|side|` (often slightly positive for righty)

Older sessions may use shallower slashes (low side) — use thrust fallback or stab pocket.

## Threshold constants map

All in `VrCombatMotion.cs`:

```
PullBackDetect, PullBackCharge, ReleaseForwardThreshold
WindUpMinMetric, BashNegativeUpMax, BashMinBack
StabPocketMaxBack, StabPocketMaxSide, StabPocketMinUp
SlashMinSide, SlashLateralSide, SlashMinBack
DegenerateSlashDepth
SlashThrustSideMin, SlashThrustSideMinShallow, SlashThrustSideOverForward, SlashThrustMaxBack
```

## charge_start depth audit (stab pullback)

Filter `charge_start` in stab segment; `peak_depth` column 30 should sit just above `PullBackCharge`. If player reports excessive pullback, compare median depth to threshold.
