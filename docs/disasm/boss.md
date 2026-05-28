# Boss entity — `$EC10` / `$EC4C` / `$EE7D` slot

Single special entity that spawns once the player has scrolled far
enough into a level.  Lives parallel to the ship/bullet tables.

## Address inventory

| Addr | Meaning |
| ---- | ------- |
| `$EE7D` | Boss X (world byte) |
| `$EE7E` | Boss Y (pixel) |
| `$EE7F` | AI direction-state byte (cycled by `$ECBD`) |
| `$EE80` | Last X-direction sign cache |
| `$EE81` | Movement cycle counter (1..12) |
| `$EE82` | Alternate-frame toggle |
| `$EE83` | Kill count (how many times this boss has died this level) |
| `$EE84..$EE87` | Per-cycle speed table (indexed by `$EE81 / 4`) |
| `$EE8E..$EE91` | Mirrored state for next-frame draw |
| `$EE7C` | Active flag (0 = not spawned, 1 = active) |
| `$EE74` (word) | Scroll-progress counter — see [scroll-horizontal.md](scroll-horizontal.md) |

## `$EC10` — spawn check + tick dispatcher

```
EC10  LD A,($EE7C); AND A
EC14  JR NZ,$EC32                     ; already active → tick path

EC16  LD HL,($EE74); EX DE,HL          ; DE = scroll-progress
EC1A  LD HL,$4A38; XOR A; SBC HL,DE
EC20  RET NC                           ; if $4A38 ≥ progress, not yet

EC21  LD A,R; CP $78; RET C            ; ~50% random gate

EC26  CALL $F8F9                       ; play boss-spawn alert (music data)
EC29  LD HL,$EE83; INC (HL)            ; kill-count += 1
EC2D  LD A,$01; LD ($EE7C),A           ; activate

; --- active path (every frame after spawn) ---
EC32  LD A,($EE83); CP $0A
EC37  JR NC,$EC42                     ; if killed ≥ 10, skip throttle
EC39  LD A,($EE82); XOR $01; LD ($EE82),A
EC41  RET Z                            ; alternate-frame skip

EC42  CALL $EC4C                       ; boss tick
EC45  LD A,R; CP $16                    ; ~10% chance:
EC49  CALL C,$EC4C                     ;   double-tick (faster boss)
```

So:
- Boss spawn requires both `($EE74) > $4A38` AND a ~50% random gate.
- Once active, ticks every-other-frame (gated by `$EE82`), with a
  10% chance per frame to double-tick for extra speed.
- After 10 kills, throttle disabled (boss becomes relentless).

## `$EC4C` — boss tick body

```
EC4C  EXX
EC4D  LD HL,$EE7D            ; alt-bank HL' = boss slot
EC50  LD DE,$EE8E             ; DE = extended state
EC53  LD B,$14                ; B = 20 bytes
EC55  EXX

EC56  CALL $EAB2              ; range check
EC59  JP NC,$EC81             ; if out of range, jump to movement-only path

EC5C  EX AF,AF'
EC5D  CALL $EABD              ; setup blit
EC60  CALL $E9AC × 2          ; blit sprite (same blitter as ships)

EC66  EXX; INC B; DEC B; EXX
EC6A  JR NZ,$EC81             ; if alt B != 0, continue to movement
EC6C  ; otherwise — boss died / reset
EC6D  LD C,$7C; LD A,R; LD ($EE7E),A   ; randomize Y
EC75  LD HL,($EE7A); ADD A,(HL); LD ($EE7D),A   ; randomize X via RNG state
EC7C  XOR A; LD ($EE7C),A     ; deactivate
EC80  RET

; --- movement path ---
EC81  NOP
EC82  LD A,($EE81); DEC A
EC85  JR NZ,$EC8A
EC88  LD A,$0C                ; cycle counter 1..12
EC8A  LD ($EE81),A

EC8D  LD C,A; DEC C; SRL C × 2; LD B,$00
EC95  LD HL,$EE84; ADD HL,BC  ; HL = $EE84 + (cycle/4)
EC99  LD A,(HL)                ; A = per-cycle speed/direction byte
EC9A  LD HL,$EE8F; LD (HL),A; INC HL; LD (HL),A   ; mirror to draw slot

; X chase
ECA0  LD HL,$EE7D; LD A,(HL); LD C,A         ; C = boss.X
ECA5  LD A,($E583); ADD A,$10                ; A = player byte (= scroll+16)
ECAA  CP C
ECAB  JR Z,$ECD7                              ; same column → handle Y
ECAD  CALL C,$EBFF                            ; A = -1 if player < boss
ECB0  CALL NC,$EC06                           ; A = +1 if player > boss
ECB3  LD C,A
ECB4  LD HL,$EE7F                             ; AI direction-state
ECB7  LD A,($EE80); XOR C                     ; compare last dir to new dir
ECBA  JR Z,$ECC6                              ; same dir → accumulate
ECBD  DEC (HL); JR NZ,$ECE6                  ; different dir → decrement persistence
ECC0  LD A,C; LD ($EE80),A                   ; persistence ran out → adopt new dir
ECC4  JR $ECE6

ECC6  ; same direction
ECC7  LD A,(HL); INC A; AND $3F; LD (HL),A   ; persistence ++
ECCC  INC HL; LD C,(HL)                       ; (next byte = speed?)
ECCE  LD A,($EE7D); ADD A,C; LD ($EE7D),A    ; boss.X += C  (apply move)
ECD5  JR $ECE6

ECD7  ; same-column path: move in Y
ECD8  INC HL; LD C,(HL)
ECDA  LD A,($E584); CP C
... (Y-axis movement, similar pattern)
```

So the boss moves in 1-of-4 "speed phases" cycled through `$EE84..$EE87`,
chases the player horizontally with a "persistence" counter that
resists rapid direction changes (so it doesn't jitter when the
player passes through its column), and tracks Y only when in the
same column.

## Visual

Drawn via `$E9AC` (the same blit used by ships) — 16×8 area, with
attribute from the level color.  The sprite data location for the
boss-specific frames is TBD (different from `$E5DB` which is for
regular ships).

## C# port

`BossEntity` in `EnemyShips.cs`:

- `Tick(scrollProgress, scrollCursor, playerByteX, playerY, rng)`:
  spawn-gate matching `$EC10` (scrollProgress ≥ `$4A38` + ~50%
  random); on spawn, set `Active`, place off the right edge.
  Once active, calls `TickActive`.
- `TickActive`: chase the player in X (1 byte/frame toward
  `scrollCursor + 16`); when in same column, track Y.  Simpler
  than the cassette's 4-phase speed cycling.
- `Draw`: small XOR'd 8x8 'X' pattern at the boss's screen
  position.  Bright yellow attribute.

The full `$EC82..$ECCE` 4-phase speed cycling + persistence
counter isn't ported yet.  The boss-specific sprite data location
is also TBD.
