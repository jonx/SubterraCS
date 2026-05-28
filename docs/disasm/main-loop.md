# Main game loop — `$D7FB..$D826`

The 14-call per-frame loop that drives everything during gameplay.
This file is the **dispatch map** — each line links to the
dedicated MD file where the called routine is fully traced.

## The loop

```
D7FB main loop                                            (jumped to from $F6F2 level-load)
  D7FB CALL $F868      page-advance gate            → [player.md](player.md)
  D7FE CALL $D827      scroll-progress ($EE74)      → [scroll-horizontal.md](scroll-horizontal.md)
  D801 CALL $D8C2      input + ambient fuel drain   → [input.md](input.md), [player.md](player.md)
  D804 CALL $DCAC      player sprite bank shifter   → [player.md](player.md)
  D807 CALL $DC5D      player attribute paint       → [player.md](player.md)
  D80A CALL $F1A5      entity dispatcher (decor)    → [entities.md](entities.md)
  D80D CALL $D9C8      horizontal scroll handler    → [scroll-horizontal.md](scroll-horizontal.md)
  D810 CALL $DCF5      player XOR draw + collide    → [player.md](player.md), [collision.md](collision.md)
  D813 CALL $DFAF      player-vs-scenery + pickup   → [ship-ai.md](ship-ai.md)
  D816 CALL $E248      player mini-map dot          → [hud.md](hud.md)
  D819 CALL $E8FD      entity supercaller (ships)   → [enemies.md](enemies.md)
  D81C CALL $DE2A      player bullets + fire        → [laser.md](laser.md)
  D81F CALL $EF02      worker schedule              → [workers.md](workers.md)
  D822 CALL $E046      HUD update + bar redraw      → [hud.md](hud.md)
  D825 JR  $D7FB       loop
```

## `$E8FD` sub-dispatcher chain

```
E8FD CALL $E213    mini-map dots for $E597 ships    → [enemies.md](enemies.md)
E900 CALL $E920    ship AI (4-cycle slice)          → [ship-ai.md](ship-ai.md)
E903 CALL $EC10    boss spawn + tick                → [boss.md](boss.md)
E906 CALL $E213    mini-map again (blink)           → [enemies.md](enemies.md)
E909 CALL $ED00    enemy bullet tick                → [enemies.md](enemies.md)
E90C CALL $DD4D    collision pass (DD4A entry+3)    → [collision.md](collision.md)
E90F RET
```

## Entry / exit

The main loop is JR back to itself at `$D825 JR $D7FB` (infinite
loop).  It's left via SP restoration in the death chain:

- Death: `$DBC8 → $D8A8` restores SP from `$E457`, returning to
  the loop's caller (= `$F6F2` for next-level or the title menu
  for game-over).  See [death.md](death.md).
- Level-advance: `$F868` calls `$F6F2` which re-runs the
  level-load chain then JPs back into `$D7FB`.  See
  [level-load.md](level-load.md).

## State the loop reads/writes per frame

Inputs (read by `$D8C2`):
- `$5BFE` keyboard half-rows (via IN $FE)
- Joystick port `$1F` (Kempston, via `$F14E`)

Outputs (written by various routines):
- `$E45F` (input flags) — by `($E461)` dispatcher
- `$E460` (latch flags) — fire-release detection
- `$E465` (fuel accum) — decrement per frame
- `$EE74` (scroll progress) — by `$D827` and `$EB5B`
- `$EE7A` (RNG state) — by `$E910` (ship AI)
- `$F593` (entity slice counter) — by `$F1A5`
- `$E48B` (ship cycle) — by `$E920`
- `$EE73` (every-other toggle) — by `$E920`
- `$E0EA`/`$E0EB` (HUD attribute flash) — by `$E046`
- `$5C79`/`$5C8F`/`$5C90` (system vars) — by `$E046`'s text print
- Bitmap `$4000..$57FF` — by everyone
- Attribute file `$5800..$5AFF` — by everyone

## C# port mapping

| Cassette | Native field/class | Notes |
| -------- | ------------------ | ----- |
| `$F868` | `World.TickPlaying` altitude check | matches `altitude>=$75 && workers cleared` |
| `$D827` | `World.ScrollProgress += step` | port of the level-scaled increment |
| `$D8C2` | `Sdl2InputPump` + `World.FuelAccum` | + low-fuel SFX |
| `$DCAC` | (skipped — no scanline-sub maintenance needed in C#) | |
| `$DC5D` | (handled inline in player draw) | |
| `$F1A5` | `World.PlaceWorkersForLevel` + entity iteration | partial (System A only) |
| `$D9C8` | `World.TickPlaying` Horizontal block | full port |
| `$DCF5` | `Blitters.DrawPlayerXor` | XOR-erase via undo buffer skipped |
| `$DFAF` | inline wall-collision in `TickPlaying` | |
| `$E248` | `EnemyShips.DrawMiniMapDots` indirectly | |
| `$E8FD` | `EnemyShipTable.Draw` + `Boss.Tick` + `EnemyShots.Tick` | |
| `$DE2A` | `World.TickPlaying` bullets block + `FireBullet` | |
| `$EF02` | `WorkerSchedule.Tick` + `Draw` | |
| `$E046` | `Hud.Draw` | |
