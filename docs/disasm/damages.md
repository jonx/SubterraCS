# Damage system — how the player takes hits and dies

Cross-cuts the cassette's three damage *triggers* and the single
*damage chain* (`$DDC4`) they all funnel into.  Complements
[collision.md](collision.md) (which dissects the walker `$DD4A` /
`$DD4D` and the per-entity tests `$DD8C` / `$DDAA`).

## TL;DR

- **Three triggers, one chain.**  `$DCF5` (player-draw XOR-overlap),
  `$EB7A` (ship address-match), and `$EDC0` (bullet address-match)
  all call `$DD4A`.  `$DD4A` calls `$DDC4` (damage), then walks
  ship/bullet/boss tables for coord-overlap → instant death.
- **No invincibility.**  `$DDC4` has no per-hit cooldown.  Every
  frame of overlap drains the accumulator.  Death happens *fast*
  if you sit inside a hazard.
- **Two-stage drain.**  `$E463` (= `HitAccum`, init `$FF`) loses
  `$40` per hit.  Only the 4th hit underflows and decrements
  `$E464` (= `Shield`, init `$5F` = 95).  Visible shield ticks
  down ~once per 4 hits.

## Trigger 1 — `$DCF5` XOR-overlap (PRIMARY)

Inside the player's XOR-draw, every non-transparent sprite byte
checks the destination screen byte.  If the screen byte is
**non-zero** (scenery, ship pixel, bullet pixel, electric arc —
anything), a shadow carry flag latches.  After all 4 columns ×
8 rows are drawn, the post-draw test fires `$DD4A`:

```
DCF5  LD IX,$E8C9             ; 4 player quadrant addresses
DCF9  LD DE,$E8A9             ; 32 sprite bytes
DCFC  EX AF,AF'; XOR A; EX AF,AF'   ; shadow A = 0, carry' = 0
...
DD21  LD A,(DE)                ; sprite byte
DD22  AND A; JR Z,$DD2E        ; skip transparent rows
DD25  INC (HL); DEC (HL)       ; "is screen byte already non-zero?"
DD27  JR Z,$DD2C               ;   zero → skip flag
DD29  EX AF,AF'; SCF; EX AF,AF'    ;   non-zero → SET shadow carry
DD2C  XOR (HL); LD (HL),A      ; XOR-write the sprite byte
...
DD3A  EX AF,AF'
DD3B  CALL C,$DD4A             ; carry set → fire damage chain
```

The `INC (HL); DEC (HL); JR Z` idiom is the Z80 trick for
"test memory byte without trashing A": after the INC/DEC the
byte is restored, and the zero flag reflects whether the *original*
was `$00`.

This is the *main* collision system.  Scenery, electric arcs,
ship sprites already drawn on the bitmap, even decor entities — if
any of their pixels coincides with where the player draws, damage
fires.

## Trigger 2 — `$EB7A` ship address-match

Called from inside the ship-AI loop (`$E920` chain).  Compares
each ship's currently-drawn screen address to the player's
4-quadrant address table at `$E8C9`:

```
EB7A  PUSH HL; PUSH DE; PUSH AF
EB7D  LD DE,$E8C9             ; player's 4 quadrant addresses
EB80  LD B,$04
EB82  LD A,(DE); CP L         ; address low byte match?
EB84  JR NZ,$EB8C
EB86  INC DE; LD A,(DE); DEC DE; CP H   ; address high byte match?
EB8A  JR Z,$EB94
EB8C  INC DE × 2; DJNZ $EB82
EB90  POP AF; POP DE; POP HL; RET
EB94  CALL $DD4A
```

Backup trigger: catches a ship whose draw produces the exact
same bitmap address as one of the player's 4 cells.  In practice
this is the same condition as XOR-overlap on the ship's drawn
pixels, just detected slightly earlier in the frame.

## Trigger 3 — `$EDC0` bullet address-match

Mirror of `$EB7A` but called from the per-frame bullet tick
(`$ED01`):

```
EDC0  PUSH HL; PUSH DE; PUSH AF
EDC3  LD DE,$E8C9
EDC6  LD B,$04
... (same walk as $EB7A)
EDDA  CALL $DD4A
```

Catches bullets the same way `$EB7A` catches ships.

## The damage chain — `$DD4A` → `$DDC4`

```
DD4A  CALL $DDC4              ; HIT: sound + accum drain + maybe shield--
DD4D  NOP                     ; fall through to the entity-walk
DD4E  LD HL,($E583); ...      ; cache player_byte = ($E583)+$0F into $EE76
DD58..DD89  walk ships ($E597 ×7) + bullets ($EE9E ×6) + boss ($EE7D)
            with $DD8C / $DDAA / $DD8C respectively — any coord
            overlap → JP $DBC8 (instant DEATH)
```

`$DD4D` (entry +3) is the "walk without damaging first" door —
called per-frame from `$E8FD` (sub-dispatcher at `$E90C`).  It
runs the entity walk every frame purely for the instant-death
check.

### `$DDC4` — the actual hit chain

```
DDC4  LD A,$00; OUT ($FE),A          ; border off
DDC6  loop: DEC A; JR NZ,$DDC6        ; ~256-cycle "click" delay
DDCB  OUT ($FE),A                    ; border off again — net effect: 1 click
DDCD  LD A,($E463)                   ; HitAccum
DDD0  SUB $40
DDD2  LD ($E463),A
DDD5  RET NC                         ; no underflow → no shield decrement
DDD6  LD A,($E464); DEC A; LD ($E464),A    ; Shield--
DDDD  RET NZ                         ; shield > 0 → continue
DDDE  INC A; LD ($E464),A            ; pin shield at 1
DDE2  JP $DBC8                       ; DEATH
```

**No invincibility flag.** **No cooldown.** Every CALL to
`$DDC4` drains `$40` from `$E463`.  On underflow (4th hit, since
$FF / $40 = 3.98), `$E464` decrements.

When `$E464` hits 0, it's INC'd back to 1 (so the HUD shows the
last bar segment during the death animation) and the death
routine fires immediately.

### Initial values

`$E419 → $E446` (level/respawn init):
```
E419  LD A,$FF; LD ($E463),A; LD ($E465),A   ; HitAccum, FuelAccum
E446  LD A,$5F; LD ($E464),A; LD ($E466),A   ; Shield, Fuel = 95
```

So shield starts at 95.  Each shield decrement = 4 hits.  Full
shield → death in 4 × 95 = 380 hits.  At 60 fps with continuous
overlap that's 6.3 seconds of being stuck inside a hazard.

## How the player visibly "blinks"

The cassette has no invincibility-blink mechanism in the damage
path.  What the player *sees* as a blink is the **XOR
cancellation**: when the player sprite XORs into a non-zero
bullet/ship pixel, some of the player's sprite bits flip off
that frame.  The next frame, if the bullet has moved, the
player draws cleanly again.  So a single bullet that's
overlapping for 3 frames looks like a 3-frame ship flicker.

The flicker is the *visible signature of damage firing every
frame*.  Each flicker-frame contributes one `$DDC4` call, one
`$E463 -= $40`.

(There's a separate "respawn flash" sequence in `$DBC8` — the
8-particle death animation — but that's a death routine, not a
damage blink.)

## Hits the chain does NOT cover

`$DD4A`'s entity walk only tests ships, bullets, and the boss.
**System A decor entities (`$F2EB+`) are NOT in the walker.**  So
in the cassette, drones / rocks / electric arcs / etc. only
damage you via the `$DCF5` XOR-overlap trigger — because their
sprites are on the bitmap when the player draws over them.

Scenery (cave walls) — same story: scenery is on the bitmap, so
`$DCF5` flags overlap.  Plus the explicit `$DFAF + $EB62` probe
that runs per-frame for "instant-death on solid-wall tile 01"
(see [collision-matrix.md](collision-matrix.md)).

## C# port mapping

| Cassette | C# location | Notes |
| -------- | ----------- | ----- |
| `$DCF5` shadow-carry SCF | `Blitters.DrawPlayerXor` returns `bool overlap` | reads pre-XOR bitmap byte; sets if `sp != 0 && screen != 0` |
| `$DD3B CALL C,$DD4A` | `World.DrawPlaying` sets `_playerXorOverlap = true` when draw returns true | consumed in next `TickPlaying` |
| `$EB7A` ship address match | `EnemyShips.TickAi.LastTickHits` (coord proxy `$DD8C` window) | left-biased ±1 byte, ±8 px Y |
| `$EDC0` bullet address match | `EnemyBullets.Tick` return value (coord proxy `$DDAA` window) | left-biased ±1 byte, 0..7 px below |
| `$DDC4` HitAccum drain | `World.TickPlaying` damage block | `HitAccum -= 0x40`; on `< 0` → `HitAccum &= 0xFF; Shield--` |
| `$DBC8` DEATH | `TriggerDeath` → `GameState.Dying` → `Respawn` | shield pinned to 0 here (we don't model the "INC A; LD ($E464),A" pin-at-1) |
| `$DD4D` per-frame death walker | `World.TickPlaying` `deathHits != 0 && !Invincible` → `TriggerDeath` | coord overlap = instant death (separate path from damage drain).  `LastTickHits` (ships, $DD8C window) and `EnemyShots.Tick` return (bullets, $DDAA window) feed this. |
| `$E419..$E446` init | `Respawn` / `LoadLevel` set `HitAccum=0xFF`, `Shield=BarMax=$5F` | |
| (none — cassette has no per-hit invincibility) | `Invincible` is set only by `Respawn(100)` / `LoadLevel(60)` | initially I added `SetInvincible(20)` inside the damage block — that's a non-cassette artifact and was removed once `$DDC4` was decoded properly |

## History — why this took multiple sessions

1. First pass modeled damage as coord-overlap (`s.X == playerByteX && |s.Y - playerY| < 16`).  Window was speculative and the X-exact match almost never fired → "nothing hurts me except walls".
2. Second pass widened the window to ±1 byte without consulting the ASM.  Still based on the wrong model (coord-overlap) instead of the cassette's pixel-overlap.
3. Third pass disassembled `$DCF5` and discovered the shadow-carry SCF + `CALL C,$DD4A`.  XOR-overlap is the cassette's *primary* trigger; the coord checks are at best a backup.  Implemented `DrawPlayerXor` overlap flag + reordered `DrawPlaying` so the player draws after entities (the flag now catches ship/bullet/arc pixels).
4. Fourth pass: shield still didn't drop in tests.  Disassembling `$DDC4` revealed the cassette has **no per-hit invincibility** — my `SetInvincible(20)` after each damage was throttling hits to ~1/sec from the cassette's ~60/sec.  Removed it; `Invincible` retained only as a respawn/level-load grace period.
5. Fifth pass: separated coord-overlap (`$DD4D` walker) from XOR-overlap (`$DCF5` shadow carry).  In the cassette these have *different consequences*: coord = instant DEATH, XOR = damage drain.  The port had been routing both into the damage chain.  Now coord overlap triggers `TriggerDeath` directly, matching the cassette's `JP $DBC8`.
