# Boxing game setup

1. Open the project in Unity 6000.1.5f1 (let it import / compile scripts).
2. Menu bar: **Boxing > Build Game Scene** (re-run it after every update of these scripts).
   It builds the Animator Controller, creates `Assets/Scenes/BoxingGame.unity` from the arena demo scene,
   adds the player, AI opponent, camera, game manager and the extra **Octagon Cage** map.
3. Press **Play**. Choose difficulty and arena, press Enter.

## Menu
| Keys | Action |
|------|--------|
| Left / Right or 1-5 | Difficulty |
| Up / Down or M | Arena |
| Enter / Space | Start |

## Difficulties
| # | Name | Opponent | Perks |
|---|------|----------|-------|
| 1 | EASY | Rookie Rico | slow reactions |
| 2 | NORMAL | Iron Jack | balanced |
| 3 | HARD | Savage Sam | sharp and relentless |
| 4 | EXTREME | The Butcher | 2x damage |
| 5 | IMPOSSIBLE | Prime Mike Tyson | 3x damage, +50% health |

(Edit names/values in the `Presets` array at the top of `GameManager.cs`.)

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
Blocking cuts damage heavily but drains stamina; empty stamina = guard break (stunned).
Dodge = quick sidestep/backstep with a short invulnerable window; costs stamina, has a cooldown.
