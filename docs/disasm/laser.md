# Laser beam — `$DE41` / `$DEF0`

The ship's laser is a horizontal beam up to 15 bytes (120 pixels)
wide, drawn directly into the bitmap with XOR / OR semantics.  The
fire button creates one beam record at `$E46B` (4 slots × 4 bytes
each); a per-frame update routine advances the beam and erases
its tail.

## `$DE41` — fire-key handler

```
DE41  3A 5F E4    LD A,($E45F)
DE44  CB 47       BIT 0,A
DE46  C8          RET Z            ; fire not pressed → done

DE47  DD 21 6B E4 LD IX,$E46B      ; bullet table base
DE4B  11 04 00    LD DE,$0004       ; record stride = 4 bytes
DE4E  06 04       LD B,$04          ; up to 4 slots
DE50  DD 7E 00    LD A,(IX+$00)
DE53  CB 7F       BIT 7,A          ; alive flag?
DE55  28 05       JR Z,$DE5C       ; free slot found
DE57  DD 19       ADD IX,DE
DE59  10 F5       DJNZ $DE50
DE5B  C9          RET              ; all slots full → bail
```

Walks the 4 bullet records at `$E46B+0`, `+4`, `+8`, `+12`,
looking for one whose status byte (`+0`) has bit 7 = 0 (free).
If all 4 slots are alive, the fire button is ignored.

## Fire sound — `$DE5C..$DE6E`

```
DE5C  PUSH BC
DE5D  LD C,$14          ; $14 = 20 outer iters
DE5F  LD A,$10          ; speaker output
DE61  LD B,$C8
DE63  OUT ($FE),A
DE65  XOR $10           ; toggle
DE67  DJNZ $DE67        ; busy-wait inner
DE69  OUT ($FE),A
DE6B  DEC C
DE6C  JR NZ,$DE5F
DE6E  POP BC
```

20 cycles of speaker toggle with a `$C8`-iter delay — a short
high-pitched zap.

## Bullet status byte (offset +0)

```
bit 7   alive
bit 6   ?
bit 5   facing-right (0=left, 1=right)
bits 4..0  attribute (bright | ink) — set per-shot
```

Computed at `$DE6F..$DE7F`:

```
DE6F  LD A,($5C79)      ; sysvar / Z80 R register variant
DE72  AND $07            ; A = random 0..7
DE74  EX AF,AF'          ; save in A'
DE75  LD A,($E586)       ; DirectionState
DE78  AND $01            ; facing bit
DE7A..DE7C  RRCA × 3       ; shift bit 0 to bit 5
DE7D  OR $80             ; set alive bit (bit 7)
DE7F  LD (IX+$00),A      ; store status byte
```

Random color per shot — this is **why the laser changes color
every time you fire**.

## Bullet Y origin (offset +1)

```
DE82  LD A,($E584)       ; altitude
DE85  ADD A,$04           ; A = altitude + 4
```

The Y origin is `altitude + 4` — the MIDDLE of the 8-pixel-tall
ship sprite.  My port's previous "PlayerY" (= altitude, top of
ship) was 4 pixels too high, exactly matching the user's "the
laser is slightly too high" feedback.

## Bullet screen address (offsets +2/+3)

`$DE82..$DEBD` resolves the (altitude+4) Y coordinate via a table
at `$E80F` (parallel to the player's quadrant table at `$E8C9`) +
pixel-offset arithmetic, plus an X offset of 15 bytes in the
facing direction.  Final screen address gets stored as (L, H) in
`(IX+$02)`, `(IX+$03)`.

## Initial beam paint — `$DED4..$DEE4`

```
DED4  LD B,$0F          ; 15 bytes = 120-pixel max beam length
DED6  LD A,($00EF)       ; (zero — ROM at $00EF)
DEDA  INC (HL); DEC (HL) ; test if (HL) = 0
DEDC  JR NZ,$DEE9       ; non-zero (collision) → bail
DEDE  LD (HL),$EF        ; paint beam byte
DEE0  CD 28 DF    CALL $DF28  ; paint attribute cell
DEE3  ADD HL,DE          ; advance (DE = ±1)
DEE4  DJNZ $DED9        ; loop up to 15 times
```

Paints up to 15 bytes of `$EF` (= `11101111` = 7 lit pixels with
one dark gap) walking sideways from the ship.  Each byte covers 8
horizontal pixels and 1 vertical pixel.  Stops early on collision
(detects any non-zero pixel = scenery or entity).

## What does the laser do during travel? (collision behaviour)

Two separate self-collision mechanisms are in play, both of which
make the beam interact with scenery rather than just punch
through it:

**1. Self-limit at fire time** — the `$DEDA INC (HL); DEC (HL); JR
NZ,$DEE9` test inside the initial paint loop bails the loop as
soon as any non-zero pixel is encountered.  So if the player is
firing into a wall 5 bytes away, the beam only gets 5 bytes
painted, not 15.  The stored length `(IX+$01)` reflects this
shorter actual paint:

```
DEE9  LD A,$0F          ; load 15
DEEB  SUB B              ; minus remaining loop count
DEEC  LD (IX+$01),A     ; store ACTUAL painted bytes (≤ 15)
```

**2. `$DF31` — beam erase/redraw bracket around horizontal
scroll (FULLY DECODED — it is NOT collision logic).**

Callers (the only four in the binary):

```
DA25  LD C,$00; CALL $DF31    ; scroll-left, BEFORE the LDIR shift
DA49  LD C,$EF; CALL $DF31    ; scroll-left, AFTER the shift
DA64  LD C,$00; CALL $DF31    ; scroll-right, BEFORE the LDDR shift
DA88  LD C,$EF; CALL $DF31    ; scroll-right, AFTER the shift
```

The routine walks all 4 beam slots at `$E46B` (stride 4); for
each alive beam (bit 7 of +0), it walks the `(IX+$01)` painted
bytes from address `(IX+$02/$03)`, direction ±1 from bit 5:

- **C = $00 (erase pass, pre-scroll):** at each byte, if
  `(HL) == $EF` (still beam), write C=0 (erase) and call `$DFA1`
  → restore the cell's attribute to the level colour `($E57B)`.
  Beam bytes already overdrawn by something are left alone.
- **C = $EF (redraw pass, post-scroll):** at each byte, test
  `INC (HL); DEC (HL)` — if the screen byte is non-zero
  (scenery scrolled into the beam's path), skip; else write
  `$EF` (redraw beam) and call `$DF7C`.

`$DF7C` is attribute management, not a hit chain: it probes
scanline 0 (`H ← $40` or `$48` keeping band bit 3) and scanline 7
(`H += 7`) of the char cell containing the beam byte; only if
BOTH are zero (nothing else lives in that cell) does it write the
beam's own colour (`(IX+$00) AND $47`) into the attribute —
otherwise the cell keeps its colour.  A colour-clash-avoidance
trick: the beam only recolours cells it has to itself.

**Consequence — the cassette's laser hits NOTHING.**  There is no
entity-match logic anywhere in `$DF31`/`$DF7C`/`$DFA1`, and a
full-binary search finds no `RES 7,(IX+d)` instruction at all —
nothing ever clears a ship's alive bit at `$E597+2`.  Enemy
ships are unkillable in the original game; the laser is purely
visual (it stops at scenery at fire time per `$DEDA`, and gets
overdrawn by sprites, but damages nothing).

The C# port's laser-kills-ships (+50 score, respawn delay) and
laser-vs-boss (3 hits) are **port-only embellishments** — now
confirmed as such by this trace rather than suspected.

## Per-frame update — `$DEF0..$DF1B`

```
DEF0  LD A,(IX+$00)
DEF3  LD DE,$FFFF        ; default: -1 byte (move left)
DEF6  BIT 5,A
DEF8  JR Z,$DEFD
DEFA  LD DE,$0001        ; bit 5 set → +1 (move right)

DEFD  LD L,(IX+$02); LD H,(IX+$03)  ; HL = current beam tail address
DF03  LD A,(IX+$01)
DF06  AND A
DF07  JR Z,$DF23         ; length=0 → bullet expired
DF09  DEC A; LD (IX+$01),A  ; length--

DF0D  LD A,$EF
DF0F  CP (HL)
DF10  JR NZ,$DF1B        ; tail no longer $EF (already overdrawn) → skip
DF12  LD (HL),$00         ; erase tail byte
DF14  LD A,($E57B); LD C,A  ; restore level colour
DF17  CALL $DF28          ; paint attribute back to level colour
DF1B  ADD HL,DE           ; advance tail position
DF1C  LD (IX+$02),L
```

Each frame the beam:
- Advances by 1 byte (= 8 px) in the firing direction
- Erases the tail byte (writes `$00` and restores the level's
  attribute colour where the beam used to be)
- Decrements length counter; when it hits 0 the beam is gone

So the visual effect is a horizontal stripe of `$EF` bytes
that *advances forward and shortens from the back* — appearing
to "shoot" across the screen.

## Port summary

In our C# port:

- `Bullet.Y = altitude + 4` (middle of the 8-px-tall ship sprite —
  fixes "laser is slightly too high")
- `Bullet.Pattern = $EF` (7-pixel stripe, matches original)
- `Bullet.Length = MaxLength = 15` (full beam at fire time)
- `Bullet.DX` sign = firing direction (used by the draw routine;
  the bullet doesn't actually translate — head is anchored)
- `Bullet.Attr` randomized per shot from `_rng.Next(0, 8) | $40`
- HEAD anchored at `b.X + (MaxLength - 1) * 8 * dir` (= fire-time
  far end).  Each frame `Length--`; draw walks BACKWARD from head
  for `Length` bytes — so the TAIL (= ship-side end) appears to
  recede outward.  Matches the original's visual: beam appears at
  full length on fire, then the near-ship end fades first.

### Visual model: traveling bolt vs. faithful tail-recede

The original's "all 15 bytes paint at fire, tail erases one byte
per frame from the ship side" is faithful to the cassette, but
at 60 fps it reads as "the back end is moving away from the
ship while the front sits still" — which is visually ambiguous
and the user repeatedly reported as "shooting from outside
toward the ship".

After three port attempts (top-of-sprite Y, then ship-anchored
forward draw, then head-anchored backward draw — each less
ambiguous than the last but still off), the final port uses a
**traveling-bolt model**: the head `b.X` advances by 8 px per
frame with a 4-byte trail behind, expiring after `MaxLength`
travel.  Same duration and similar hit footprint as the
original, but unambiguously "shoots out from ship".

Bugs found and fixed along the way (all reported by the user):

1. **`Y = PlayerY` (= altitude, top of ship) — 4 px too high.**
   Original uses `altitude + 4`.  Fixed.
2. **Beam covered most of the screen.**  Initial draw walked
   `Length` bytes from `b.X` in the wrong direction — toward
   negative X.  Fixed by reversing the draw direction.
3. **Far end stayed put while ship-side tail receded — perceived
   as "approaching the ship".**  Final fix: switched to a
   traveling-bolt visual that unambiguously moves outward.
