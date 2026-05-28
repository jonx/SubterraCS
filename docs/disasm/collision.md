# Ship collision — `$DD4A` / `$DD8C` / `$DDAA`

The per-frame "is the ship touching anything dangerous?" tester.
Walks two entity tables (`$E597+` stride 4, `$EE9E+` stride 6) plus
one special slot at `$EE7D`, and JPs to `$DBC8` (death) on any hit.

## `$DD4A` — entry

```
DD4A  CD C4 DD    CALL $DDC4              ; hit sound + shield-- chain
DD4D  NOP
DD4E  LD HL,($E583)            ; ($E583) = game-state lock word
DD51  LD A,$0F; ADD A,L; LD L,A ; HL = ($E583) + $0F
DD55  LD ($EE76),HL            ; cache as "player position reference"

DD58  LD A,($EE7C); AND A; JR Z,$DD65
DD5E  LD IX,$EE7D
DD62  CALL $DD8C               ; test special-slot entity

DD65  LD B,$07                  ; 7 stride-4 entities
DD67  LD IX,$E597
DD6B  LD DE,$0004
DD6E  BIT 7,(IX+$02)            ; alive flag
DD72  CALL NZ,$DD8C             ; test if alive
DD75  ADD IX,DE
DD77  DJNZ $DD6E

DD79  LD IX,$EE9E               ; 6 stride-6 entities (different table)
DD7D  LD B,$06
DD7F  LD E,B                    ; DE = 6
DD80  BIT 7,(IX+$04)            ; alive flag at offset +4
DD84  CALL NZ,$DDAA
DD87  ADD IX,DE
DD89  DJNZ $DD80
DD8B  RET
```

Surprising bit: `$DD4A` *unconditionally* calls `$DDC4` at the
start — which means the hit-sound is played and `$E463`/`$E464`
drained EVERY frame this tester runs.  Either the caller only
invokes `$DD4A` when an active collision is in progress, or my
read is missing context.  Caller-search for `CALL $DD4A` (`CD 4A DD`)
returns nothing in the at-f100 snapshot — so this routine is
probably called from outside the main game loop in a context I
haven't found yet (maybe the entity dispatcher).

## `$DD8C` — stride-4 entity collision check

```
DD8C  NOP
DD8D  LD HL,$EE76               ; player position cache
DD90  LD A,(IX+$00)             ; entity X (char-column units)
DD93  CP (HL)                    ; same column?
DD94  JR Z,$DD99
DD96  INC A; CP (HL)             ; or one column off?
DD98  RET NZ                     ; neither → no hit

DD99  NOP
DD9A  LD A,(IX+$01)             ; entity Y
DD9D  INC HL                     ; HL = ($EE77) = player Y
DD9E  SUB (HL)                   ; A = entity_y - player_y
DD9F  JP P,$DDA4
DDA2  NEG                        ; |delta|
DDA4  CP $08                     ; within 8 px?
DDA6  JP C,$DBC8                 ; HIT → DEATH
DDA9  RET
```

Detection box: **±1 column horizontally × ±8 pixels vertically**.
Very tight — basically "are the two 8×8 sprite cells overlapping?"
The 8-px vertical tolerance allows for the player and the entity
to be on adjacent scanlines and still collide.

## `$DDAA` — stride-6 entity collision check

```
DDAA  LD HL,$EE76
DDAD  LD A,(HL)
DDAE  CP (IX+$00)               ; same column?
DDB1  JR Z,$DDB8
DDB3  INC A; CP (IX+$00)        ; or one off?
DDB7  RET NZ

DDB8  LD A,(IX+$01)             ; entity Y
DDBB  INC HL
DDBC  SUB (HL)                   ; entity_y - player_y
DDBD  RET M                     ; negative (entity above player) → no hit
DDBE  CP $08                     ; within 8 px BELOW?
DDC0  JP C,$DBC8                 ; HIT → DEATH
DDC3  RET
```

Slightly different from `$DD8C`: this one only counts collisions
when the entity is BELOW the player (the negative delta is
rejected by `RET M`).  Used for ground-based entities that you
shouldn't be able to clip into from above.

## Entity table layouts

`$E597+` stride 4 (7 slots):
```
+0  X coordinate (char column? appears to be byte units)
+1  Y coordinate (pixels)
+2  bit 7 = alive flag; other bits TBD
+3  TBD
```

`$EE9E+` stride 6 (6 slots):
```
+0  X
+1  Y
+2..+3  TBD
+4  bit 7 = alive flag; other bits TBD
+5  TBD
```

Both tables are SEPARATE from the per-level entity records at
`$F2E8+` we already decoded — those records are the LEVEL
PLACEMENT (loaded once at level-load by `$F1BC`), whereas
`$E597`/`$EE9E` hold the LIVE per-frame state.

The live-table population mechanism (how `$F1BC`'s placements
end up in `$E597`/`$EE9E`) is not yet traced.

## Port notes

In our C# port we use a single `Entities[]` array and the
collision test in `TickPlaying`:

```csharp
if (!Invincible
    && Math.Abs(e.X - PlayerX) < 12
    && Math.Abs(e.Y - PlayerY) < 8)
```

±12 horizontally (slightly more permissive than $DD8C's "same
column or adjacent" to compensate for our pixel-X vs the
original's char-column-X) and ±8 vertically, matching `$DD8C`.

The original's *immediate* `JP $DBC8` for any collision is
heavier than our shield-decrement-then-maybe-die path — but in
practice the original ALSO drains shield via the separately-called
`$DDC4` chain, so the end behaviour is similar: a few collisions
empty the bar, then death animation fires.
