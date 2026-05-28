# Enemy ships — `$EE9E` table + `$EBB2` spawn + `$ED01` tick

Distinct from the static playfield decor (System A, `$F2EB` records)
and the rescuable-workers schedule (`$E75D`).  This is the third
entity subsystem — small, fast, attribute-flash enemies that chase
the player.

## Table layout — `$EE9E` (6 slots × 6 bytes)

| Offset | Meaning |
| ------ | ------- |
| +0     | X (world byte position, 0..255) |
| +1     | Y (pixel, full playfield) |
| +2     | DX (-1, 0, +1) toward player at spawn time |
| +3     | DY (-1, 0, +1) toward player at spawn time |
| +4     | Status: bit 7 = alive; bit 6 = ?; bit 5 = blink toggle |
| +5     | Lifetime counter (decrements every tick; expire at 0) |

Verified live: `at-down-f310.bin` has 2 alive entries
(rec[1] with `+4=$A0` and rec[0] mid-spawn).

## `$EBB2` — spawn

```
EBA8  LD A,($E587)        ; level
EBAB  LD B,A
EBAC  LD A,R              ; Z80 R-register (effectively random)
EBAE  AND $0F             ; A = random 0..15
EBB0  CP B
EBB1  RET NC              ; if random ≥ level, no spawn this call

EBB2  LD IX,$EE9E         ; find a free slot
EBB6  LD B,$06
EBB8  LD DE,$0006
EBBB  LD A,(IX+$04); BIT 7,A
EBC0  JR Z,$EBC7          ; (alive bit 0) → free
EBC2  ADD IX,DE; DJNZ $EBBB
EBC6  RET                 ; all 6 alive → spawn dropped

EBC7  LD (HL),$01         ; flag the spawn source
EBC9  OR (IX+$04),$80     ; set alive bit
EBD1..EBDB  read X/Y from HL[-2]/HL[-3]   ; (caller passes spawn coords)
EBDE..EBEC  +2 byte = sign(player_byte - enemy.X)
EBEF..EBFB  +3 byte = sign(altitude - enemy.Y)
EBFE  RET
```

**Spawn-rate gating**: at level 1, ~1/16 chance per call.  Level 5
gives ~5/16.  Combined with the call cadence (per-frame? per-N
frames?), this controls difficulty ramp.

**DX/DY initialisation**: the helpers at `$EBFF` / `$EC06` return
sign-of-difference between player and enemy → so the enemy is
spawned with a vector aiming **straight at the player's current
position**.

## `$ED01` — per-frame tick + draw

```
ED01  LD IX,$EE9E
ED05  LD B,$06           ; 6 slots
ED07  PUSH BC
ED08  LD A,(IX+$04); BIT 7,A
ED0D  CALL NZ,$ED19      ; if alive, tick this enemy
ED10  ADD IX,$0006; DJNZ $ED07
ED18  RET
```

Per-enemy `$ED19`:

1. Toggle bit 5 of `+4` (blink state).
2. Compute screen offset `(X − $E583)`; if in `[0, $20)`, erase
   the enemy's last position by painting the level's background
   colour back (`$ED95` with `D = $E57B`).
3. Decrement `+5` lifetime; if zero, **expire** (clear state via
   `$ED7C`).
4. `X += DX`, `Y += DY` (with `JP M` expiring on Y wrap).
5. Cache new (X, Y) at `($EE76, $EE77)`.
6. `CALL $EB62` — solid-pixel collision against scenery (returns
   NZ if blocked); on collision, expire.
7. Re-check horizontal range via `$ED8A`; if out, expire.
8. Draw at new position with attribute `$07` (bright white) via
   `$ED95`.
9. `CALL $EDC0` — **collide with player**.

### `$ED95` — single-byte attribute paint

```
ED95  LD HL,$E80F        ; same scanline-base table the laser uses
ED98  LD C,(IX+$01); SRL C × 3   ; char-row = Y / 8
EDA3  SLA C; ADD HL,BC
EDA6  LD C,(HL); INC HL; LD H,(HL); LD L,C
EDAA  LD C,A             ; C = X (offset from caller)
... (similar address resolution to $E1E4)
```

Resolves `(X − $E583, Y)` to a screen address and writes a single
attribute byte.  So enemies are drawn as **attribute-only flashes**
(blink colour cycling), NOT as 16×16 sprites.  Same technique as
the death-explosion particles in `$DBC8`.

### `$EDC0` — enemy-vs-player collision

```
EDC0  LD HL,($EE78)       ; the enemy's last screen address
EDC3  LD DE,$E8C9         ; player quadrant address table
EDC6  LD B,$04             ; 4 quadrants
EDC8  LD A,(DE); CP L     ; address low byte match?
EDCA  JR NZ,$EDD2
EDCC  INC DE; LD A,(DE); DEC DE; CP H   ; high byte match?
EDD0  JR Z,$EDD7
EDD2  INC DE × 2; DJNZ $EDC8
EDD6  RET

EDD7  CALL $DD4A          ; HIT → death chain ($DDC4 → $DBC8)
EDDA  RET
```

If the enemy's current screen address equals any of the player's
4 quadrant addresses, **fire the death routine**.  This is the
"the enemy ship rammed you" check.

## Why it doesn't show up in `at-f100.bin`

The `at-f100.bin` snapshots were captured with no movement input
(player idle at altitude 0).  Enemies need the level to scroll
(`$E583` to grow) for the spawn / range checks to be meaningful,
plus the spawn random gate.  `at-down-f310.bin` captured during
DOWN input shows live enemies in the table.

## Port status

Not yet ported.  The cassette's enemy system needs:

- A `EnemyTable` of 6 slots
- `EBB2`-style spawn callable from the game loop
- `ED01`-style per-frame tick (move toward player, lifetime,
  scenery-collision, blink draw)
- `EDC0`-style player-collision check that fires `TriggerDeath`

The current port renders System A static decor as 16×16 sprites
— those are the trees, pipes, stalactites you see now.  Enemy
ships will be SMALL attribute-flash dots that animate across the
screen with a chase pattern.
