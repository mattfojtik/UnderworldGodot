---
name: underworld-vr-interaction
description: VR laser picking and HUD interaction in UnderworldGodot. Controller-ray world picks, tile look, conversations, inventory HUD. Use when fixing look, pickup, talk, conversation menus, laser pointer, or "you see nothing" in VR.
---

# Underworld VR Interaction

## Golden rule

**All world look/pick/use rays come from the right controller laser** (`_rightController.GlobalPosition` + `GetControllerRayDir()`). Do not use head/HMD position or gaze for picking — user explicitly rejected head aim.

```csharp
var rayOrigin = _rightController.GlobalPosition;
var rayDir = GetControllerRayDir(); // -Basis.Z, fallback -Basis.Y
```

`UpdateVisionFromHead` in look mode refreshes LOS/vision only; it does not define the pick ray.

## Pick pipeline (`TryInteractLaserPick`)

Order (closest hit wins):

1. `TryPickClosestObjectAlongRay` — object mesh quads/AABBs
2. `TryPickClosestTileSurfaceAlongRay` — tilemap mesh triangles → `uimanager.LookAtTile`
3. `TryPhysicsRayPick` — collider → object index from node name

Look mode with no hit → `SayYouSeeNothing()`.

Look mode with hit beyond vision → `IsWithinLookRange` → "you see nothing".

## HUD hand panel

- Ray: `TryGetHudPanelHit` from **dominant aim hand** in-game (`GetAimRayOrigin` / `GetControllerRayDir`); intro/menu TV still uses right-hand menu pointer.
- Clicks: `PushHudMouseMotion` / `PushHudMouseClick`
- **In-game left click** (inventory slots, menus on hand HUD): dominant **trigger or grip** while hovering the panel.
- **Head status inventory** (Y overlays): same bindings; clicks forward to the real HUD via `PushVrHudMouseClick`, which temporarily sets `ModePickup` when placing a held object (VR verbs do not toggle HUD interaction mode).
- **In-game right click**: off-hand grip while hovering.
- Intro/menu TV: right trigger = left click, right grip = right click (unchanged).
- 3D viewport hole: `TryMapToUwViewport` → `TriggerViewPortClick` (legacy flat path)
- Menu-only (`blockinput`): `ApplyHudMenuPointerClicks` only — no world passthrough

### Conversation options

- Options in **message scroll** (`uimanager.MessageScroll`), not 3D viewport
- `TrySelectConversationOption(hudViewportPos)` — line from `(pos - scroll.Position) / lineHeight`
- Use `scroll.Position`/`scroll.Size`, not `GetGlobalRect()` (SubViewport coords)
- Flat-screen path: `main._Input` + `HandleMessageScrollClick` when `CursorOverMessageScroll`

## Laser visibility

| State | Laser |
|-------|-------|
| HUD visible, pointing at panel | Laser to hit point |
| HUD visible, not on panel | Laser extends (world mode) or blocked if hovering HUD for menus |
| HUD hidden (`Y`) | Laser off; world pick still on trigger |
| Conversation (`blockinput`) | HUD forced visible; short stub if not on panel |

## Interaction modes

| Mode | Ray distance | Notes |
|------|----------------|-------|
| Look | `1.2 * (DistanceToWallOrDarkness + 1)` tiles | Refresh vision before pick |
| Pickup | 3 tiles (10 with telekinesis) | |
| Talk | 8 tiles | |
| Attack | Controller aim; skip head-alignment gate | |

## Common bugs

| Symptom | Check |
|---------|-------|
| Laser frozen in world | `ShouldTickVrInput()` false — likely `InConversation` but tick gated on `InGame` |
| Can't click conversation | HUD panel hidden; not pointing at scroll; `ConversationVM.WaitingForInput` false |
| Pick hits wrong thing | Tile vs object depth order; wall blocking behind |
| Black inventory squares | `uisprite.gdshader` palette path; missing `TextureFilter.Nearest` |
| Laser won't hide | `ApplyWorldPointerInput` re-showing after `SetHudPanelVisible(false)` |

## Testing checklist

- [ ] Pick up object with laser on sprite
- [ ] Look at wall/floor/ceiling (tile description)
- [ ] Far look → "you see nothing"
- [ ] Talk to NPC → HUD shows → select scroll option
- [ ] Y toggle hides laser
- [ ] Door with left grip

## See also

- [../underworld-vr-native/SKILL.md](../underworld-vr-native/SKILL.md)
- [../underworld-vr-native/open-issues.md](../underworld-vr-native/open-issues.md)
