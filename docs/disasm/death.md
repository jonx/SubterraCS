# Death + explosion — `$DDC4` → `$DBC8` → `$D8A8`

## Overview

When the player ship takes damage, three things happen in sequence:

1. **Hit sound** plays via `$DDC4` (32 cycles of OUT $FE).
2. **Shield** at `$E464` decreases by 1.
   - If it didn't reach zero: return.
   - If it reached zero: floor `$E464` at 1 and `JP $DBC8` (death).
3. **Death animation + post-death restore** via `$DBC8` (the explosion
   particle animation, 4 passes) then `JP $D8A8` (stack restore + lives
   check).

## `$DDC4` — hit sound + shield decrement

```
DDC4  3E 00       LD A,$00
DDC6  D3 FE       OUT ($FE),A            ; speaker low
DDC8  3D          DEC A
DDC9  20 FB       JR NZ,$DDC6            ; 256 OUTs, silent
DDCB  D3 FE       OUT ($FE),A            ; speaker high

DDCD  3A 63 E4    LD A,($E463)           ; load hit accumulator
DDD0  D6 40       SUB $40
DDD2  32 63 E4    LD ($E463),A
DDD5  D0          RET NC                 ; if no underflow, done

DDD6  3A 64 E4    LD A,($E464)           ; load shield
DDD9  3D          DEC A
DDDA  32 64 E4    LD ($E464),A           ; shield--
DDDD  C0          RET NZ                 ; still alive

DDDE  3C          INC A                  ; floor at 1
DDDF  32 64 E4    LD ($E464),A
DDE2  C3 C8 DB    JP $DBC8               ; → death
```

`$E463` is a *hit accumulator* — every collision drains $40 from it,
and only when it underflows is the actual shield ($E464) decremented.
This gives the player ~4 hits before the shield drops a notch, which
is why the bar doesn't visibly tick down on every contact.

## `$DBC8` — death/explosion animation

```
DBC8  CD DA DB    CALL $DBDA             ; one particle pass
DBCB  CD DA DB    CALL $DBDA             ; another
DBCE  CD 43 DC    CALL $DC43             ; descending whine sound
DBD1  CD DA DB    CALL $DBDA             ; another
DBD4  CD DA DB    CALL $DBDA             ; final
DBD7  C3 A8 D8    JP $D8A8               ; restore stack + lives test
```

Four particle passes bracketing a sound effect.  Each `$DBDA` pass:

```
DBDA  01 20 00    LD BC,$0020
DBDD  21 61 E8    LD HL,$E861            ; particle SEED table (32 bytes)
DBE0  11 81 E8    LD DE,$E881            ; live particle scratch
DBE3  ED B0       LDIR                   ; copy seeds → live

DBE5  3A 84 E5    LD A,($E584)           ; player altitude
DBE8  5F          LD E,A
DBE9  3E BF       LD A,$BF
DBEB  93          SUB E                  ; A = $BF - altitude
DBEC  21 81 E8    LD HL,$E881
DBEF  06 08       LD B,$08
DBF1  00          NOP
DBF2  23          INC HL
DBF3  77          LD (HL),A              ; particle.Y = $BF - altitude
DBF4  23 23 23    INC HL × 3             ; advance to next particle (stride 4)
DBF7  10 F8       DJNZ $DBF1             ; 8 particles seeded

DBF9  3A 7B E5    LD A,($E57B)           ; level colour
DBFC  08          EX AF,AF'              ; save in A'
DBFD  CD 99 E1    CALL $E199             ; paint particles with level colour

DC00  06 40       LD B,$40               ; 64-iteration animation
DC02  C5          PUSH BC
DC03  DD 21 81 E8 LD IX,$E881
DC07  3A 7B E5    LD A,($E57B)
DC0A  08          EX AF,AF'
DC0B  CD 99 E1    CALL $E199             ; paint with level colour

DC0E  11 04 00    LD DE,$0004            ; stride 4
DC11  DD 21 81 E8 LD IX,$E881
DC15  06 08       LD B,$08               ; 8 particles
DC17  00          NOP
DC18  DD 7E 01    LD A,(IX+$01)          ; particle.Y
DC1B  FE 41       CP $41
DC1D  38 12       JR C,$DC31             ; skip if Y < $41 (offscreen-ish)
DC1F  DD 7E 00    LD A,(IX+$00)
DC22  DD 86 02    ADD A,(IX+$02)         ; x += dx
DC25  DD 77 00    LD (IX+$00),A
DC28  DD 7E 01    LD A,(IX+$01)
DC2B  DD 86 03    ADD A,(IX+$03)         ; y += dy
DC2E  DD 77 01    LD (IX+$01),A
DC31  DD 19       ADD IX,DE
DC33  10 E2       DJNZ $DC17             ; next particle

DC35  3E 07       LD A,$07               ; white attribute
DC37  08          EX AF,AF'
DC38  DD 21 81 E8 LD IX,$E881
DC3C  CD 99 E1    CALL $E199             ; paint particles white
DC3F  C1          POP BC
DC40  10 C0       DJNZ $DC02             ; next anim frame
DC42  C9          RET
```

Eight particles seeded with positions from `$E861` (a 32-byte table
of (x, y, dx, dy) records) and the Y component overridden with the
player's current altitude.  Then 64 iterations of:

1. Paint each particle's 8×8 attribute cell with the level colour.
2. Step each particle (`x += dx`, `y += dy`).
3. Repaint with white — produces a flashy strobing trail.

The whole effect runs entirely in the **attribute file** at `$58xx` —
the bitmap is untouched.  This is a smart trick: zero bitmap damage to
clean up, just attribute flashes that revert when the next normal
attribute paint happens.

## `$DC43` — descending whine

```
DC43  0E 08       LD C,$08
DC45  CD 4E DC    CALL $DC4E
DC48  10 FE       DJNZ $DC48             ; busy wait
DC4A  0D          DEC C
DC4B  20 F8       JR NZ,$DC45
DC4D  C9          RET
DC4E  21 00 40    LD HL,$4000
DC51  11 00 10    LD DE,$1000
DC54  CB 3E       SRL (HL)               ; shift bitmap bytes right!
DC57  1B          DEC DE
DC58  7A B3       OR D,E
DC5A  20 F8       JR NZ,$DC54
DC5C  C9          RET
```

`$DC4E` shifts EVERY byte in the bitmap right by 1 — the screen fades
to black over 8 calls (one bit shifted out per call).  That's the
visual `screen dim` to accompany the sound effect.

## `$D8A8` — post-death restore + lives check

```
D8A8  21 91 5C    LD HL,$5C91
D8AB  CB 86       RES 0,(HL)
D8AD  CB 8E       RES 1,(HL)             ; clear keyboard state flags
D8AF  3A 88 E5    LD A,($E588)           ; load lives
D8B2  3D          DEC A
D8B3  20 03       JR NZ,$D8B8            ; if lives > 1, just restore
D8B5  CD 74 F9    CALL $F974             ; game-over screen
D8B8  21 58 27    LD HL,$2758
D8BB  D9          EXX
D8BC  ED 7B 57 E4 LD SP,($E457)          ; restore caller SP
D8C0  C9          RET
```

`$E588` is the **lives counter**.  The lives DEC must happen elsewhere
(possibly inside `$F974` or in a path I haven't traced yet).  Note
this routine restores SP from `$E457` — meaning death rewinds the call
stack to whatever was saved at `$E457` (the main game-loop entry).

## Address summary

| Address  | Role |
| -------- | ---- |
| `$E463`  | Hit accumulator (drained $40 per hit; underflow → shield-- ) |
| `$E464`  | Shield (0..$5F = 0..95; floored at 1 on death) |
| `$E466`  | Fuel (0..$5F) |
| `$E588`  | Lives counter |
| `$E584`  | Altitude (Y) — seeds death particle Y |
| `$E57B`  | Active level colour (attribute byte) |
| `$E861`  | 32-byte particle SEED table (8 records × 4 bytes: x, y, dx, dy) |
| `$E881`  | Live particle scratch (overwritten from seeds each pass) |

## C# port notes

In our port the explosion is a particle effect at the attribute level:
- 8 particles seeded from a captured `$E861` table
- Y override = $BF - altitude
- 64 anim iterations of: paint cell colour, step, paint white
- Followed by a bitmap dim (one SRL pass per shift), then restore.

We don't byte-extract the seed table — instead we use the same shape
(8 outward-fanning particles) with values that match the observable
pattern.  The animation count (64), particle count (8), and the
"paint colour A → paint colour B" alternation are exact.
