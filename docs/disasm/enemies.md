# Enemy systems — `$E597` ships + `$EE9E` bullets

User feedback caught my earlier mistake: I had labelled `$EE9E`
as "enemy ships", but those are **bullets fired by the actual
enemies**.  The real ships live at `$E597`.

The main game loop calls `$E8FD` once per frame which dispatches:

```
E8FD  CALL $E213    ; mini-map dot draw (for the $E597 entries)
E900  CALL $E920    ; ENEMY SHIP per-frame AI + spawn (4-cycle slice)
E903  CALL $EC10    ; ?? (uses $EE7C / $EE74 / $EE83)
E906  CALL $E213    ; another mini-map pass (blink/erase)
E909  CALL $ED00    ; ENEMY BULLET tick ($EE9E processor — what I was wrongly calling "ships")
E90C  CALL $DD4D    ; collision pass entry+3
E90F  RET
```

`$EBB2` (spawn into `$EE9E`) and `$ED01` (per-frame tick) are
reachable as part of this chain, not via direct CALL — which is
why my pattern-match search for callers came up empty.

---

## `$E597` — enemy ship table (4 bytes × 7 slots)

| Offset | Meaning |
| ------ | ------- |
| +0     | World X (0..255 byte position along the level) |
| +1     | Y (pixel — mini-map row is `30 - Y/4`, playfield draw uses Y direct) |
| +2     | Status (bit 7 = alive) |
| +3     | TBD (frame counter? AI sub-state?) |

Verified: in `at-f100.bin` level 1 these are pre-loaded from the
init data at `$E48D + level*32` (8 records × 4 bytes copied at
level-load by `$E319`).

`$E213` walks them once per frame and draws a single-byte mini-map
dot per alive entry.  `$E920` is the per-frame AI/animation —
indexed by a 4-cycle counter at `$E48B` so each entity gets
different code per slice (typical Z80 time-sliced AI).  Ships
fire bullets by calling `$EBB2` to spawn an entry into `$EE9E`
(the exact call path inside `$E920` is still TBD — it goes
through `$EA…` helpers and uses a per-cycle parameter table at
`$E5DB`).

---

## `$EE9E` — enemy BULLETS (not ships) — 6 slots × 6 bytes

What I'd previously documented here.  This is the table for the
**projectiles fired by the `$E597` ships**.  Each bullet is born
aimed at the player's current world position (DX/DY = sign of
difference), has a short lifetime (`$40` ticks), and is drawn as
a single-byte attribute flash that travels until either lifetime
expires or it hits the player's bitmap.

---

## `$EC10` — boss / special-entity spawn + tick

A single special entity slot at `$EE7D..$EE84` (8 bytes).  Lives
parallel to the ship/bullet tables; processed by `$EC10` once per
frame from the main loop's `$D81C CALL $DE2A` chain (actually
through `$E8FD CALL $EC10` per re-check).

```
EC10  LD A,($EE7C); AND A
EC14  JR NZ,$EC32           ; already spawned → jump to tick path

EC16  LD HL,($EE74)          ; scroll-progress counter
EC1A  LD HL,$4A38; XOR A; SBC HL,DE
EC20  RET NC                 ; not far enough yet → bail

EC21  LD A,R; CP $78; RET C  ; ~50% random gate

EC26  CALL $F8F9              ; print "BOSS ALERT" message (print stream)
EC29  LD HL,$EE83; INC (HL)   ; kill-count++
EC2D  LD A,$01; LD ($EE7C),A  ; mark active

EC32  LD A,($EE83); CP $0A
EC37  JR NC,$EC42            ; if killed >= 10, skip throttle
EC39  LD A,($EE82); XOR $01; LD ($EE82),A
EC41  RET Z                   ; alternate frame skip

EC42  CALL $EC4C              ; main boss-tick subroutine
EC45  LD A,R; CP $16
EC49  CALL C,$EC4C           ; extra tick with ~10% chance
```

So the boss is **scroll-progress-gated** (must travel `$4A38`
units in `$EE74` before spawn becomes possible), then has a
~50% random spawn chance per frame.  Once active, ticks via
`$EC4C` (which walks `$EE7D..$EE8E`, the boss slot's 20-byte
state).

### Boss-related state addresses

| Addr   | Meaning |
| ------ | ------- |
| `$EE7D..$EE84` | Boss slot (8 bytes: X, Y, status, sub, +4..+7) |
| `$EE7C` | Boss-active flag (0 = not spawned, 1 = active) |
| `$EE74` (word) | Scroll-progress counter; boss eligible when ≥ `$4A38` |
| `$EE83` | Boss kill-count / cycle |
| `$EE82` | Alternate-frame flag (toggles 0/1 each tick) |
| `$EE7E..$EE8D` | Extended boss state (read at `$EC4D`) |

`$F8F9` is the message-print routine called on first spawn.

---

## Complete inventory — main-loop entry-point map

```
D7FB main game loop
  D7FE CALL $D827        scroll-distance accumulation ($EE74 update)
  D801 CALL $D8C2        input + L-key fuel drain
  D804 CALL $DCAC        sprite-context maintenance
  D807 CALL $DC5D        player attribute paint
  D80A CALL $F1A5        STATIC DECOR draw     (system A, $F2EB)
  D80D CALL $D9C8        horizontal scroll
  D810 CALL $DCF5        player XOR draw
  D813 CALL $DFAF        ???
  D816 CALL $E248        player MINI-MAP dot
  D819 CALL $E8FD        ENTITY SUPERCALLER ← see below
  D81C CALL $DE2A        player BULLETS ($E46B + $DE41 fire)
  D81F CALL $EF02        WORKER SCHEDULE ($E75D) — pickup + mini-map dots
  D822 CALL $E046        HUD attribute flash + bar update

$E8FD entity supercaller
  E8FD CALL $E213        mini-map dot draw for $E597 ships
  E900 CALL $E920        SHIP per-frame AI (4-cycle slice, $E48B/$E5DB)
  E903 CALL $EC10        BOSS spawn + tick (single slot at $EE7D)
  E906 CALL $E213        mini-map again (alternation for blink)
  E909 CALL $ED00        BULLET tick ($EE9E processor)
  E90C CALL $DD4D        collision pass
```

---

## C# port status

| Subsystem | C# class | Status |
| --------- | -------- | ------ |
| `$F2EB` static decor | `LevelEntities` + `World.PlaceWorkersForLevel` | **DONE** |
| `$E75D` workers | (TBD — currently shoehorned into `Entities[]`) | partial |
| `$E597` ships | `EnemyShips` | **STUB** |
| `$EE9E` bullets | `EnemyBullets` (was `EnemySwarm`) | **DONE** (rendering only — caller chain TBD) |
| `$EE7D` boss | `BossEntity` (inside `EnemyShips.cs`) | **STUB** |

Next implementation pass:
1. Load `$E48D + level*32` asset into `EnemyShipTable.LoadFromInit`.
2. Implement `$E213`'s mini-map dot draw.
3. Implement `$E920` AI: 4-cycle dispatch, per-cycle sprite data
   bank at `$E5DB`, ship movement, bullet firing via `$EBB2`.
4. Implement `$EC10` boss spawn + `$EC4C` tick.
5. Add `$EE74` scroll-progress counter (per `$D827`).

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

Ported as `EnemySwarm` in `native/SubterraCS.Core/EnemySwarm.cs`:

- 6-slot table matching `$EE9E` layout (X, Y, Dx, Dy, Status, Lifetime)
- `TrySpawn` mirrors `$EBB2`: random gate `rng.Next(0,16) >= level`,
  finds free slot, places enemy 16..31 bytes ahead of the player's
  world byte, computes Dx/Dy = sign-toward-player.  (The cassette
  reads spawn X/Y from a caller-supplied pointer; the caller logic
  is TBD, so we substitute a "spawn just off the right edge"
  position for now.)
- `Tick` mirrors `$ED01`: blink toggle, lifetime decrement, X += Dx
  / Y += Dy, expire on Y wrap, scroll-window cull, player-collision
  via byte-and-Y match — returns a hit bitmask the caller uses to
  fire the damage chain.
- `Draw` mirrors `$ED95` direction: single-byte bitmap dot + bright
  white attribute cell at the resolved screen address.

Wired in `World.TickPlaying` (calls `TrySpawn` + `Tick`, applies
HitAccum damage on player-collision matching `$DDC4`'s drain
semantics).  Reset on `LoadLevel`.

What's NOT ported:
- The exact spawn-source pointer chain (HL[-3], HL[-2] read by
  `$EBB2`).
- The blink-attribute cycling — we always draw bright white.
- The `$EB62` scenery-collision check (currently the enemy passes
  through cave walls).
- The 4-slot blink-state animation at `$ED19`'s bit-6/bit-5 logic;
  we just toggle bit 5.
