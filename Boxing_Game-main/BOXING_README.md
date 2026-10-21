# Boxing game setup (3 steps)

1. Open the project in Unity 6000.1.5f1 (let it import / compile scripts).
2. Menu bar: **Boxing > Build Game Scene**. This builds the Animator Controller from the Kairo
   clips, copies the arena demo scene to `Assets/Scenes/BoxingGame.unity`, and adds the player,
   the AI opponent, camera and game manager.
3. Press **Play**. Pick difficulty (1/2/3), press Enter.

## Controls
| Action    | Keyboard / Mouse        | Gamepad          |
|-----------|-------------------------|------------------|
| Move      | W/S toward/away, A/D circle | Left stick   |
| Jab       | Left mouse / J          | West (X/Square)  |
| Cross     | Right mouse / K         | East (B/Circle)  |
| Uppercut  | E / L                   | North (Y/Triangle)|
| Dodge     | Ctrl / Space (+A/D/S for direction, default back) | South (A/Cross) |
| Guard     | Hold Shift              | LT / LB          |

## Rules
Best of 3 rounds, 60 s each. KO a fighter or have more health when time runs out.
Dodge = quick sidestep/backstep with a short invulnerable window; costs stamina, has a cooldown.
Blocking cuts damage heavily but drains stamina; empty stamina = guard break (stunned).

## Tuning (all in the Inspector on the Player / Opponent objects)
- `Fighter.punches`: damage, range, timing (`hitPoint`), stamina cost, stun, knockback.
- `Fighter.moveSpeed`, `maxHealth`, `maxStamina`.
- `GameManager`: round time, rounds, ring size / start distance.
- `BoxerAI.difficulty` (0-1) is set from the menu choice.
