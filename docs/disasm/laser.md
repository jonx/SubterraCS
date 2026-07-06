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

The attribute site at `$DEC3` completes the picture
(disasm-verified, RE-LOG §66):

```
DEC3  LD A,R; AND $07    ; random ink 0..7
DEC7  JR NZ,$DECB        ; zero (black) →
DEC9  LD A,$43            ;   default = BRIGHT CYAN
DECB  OR $40              ; set the BRIGHT bit
```

So a masked-to-zero roll falls back to bright cyan and every beam
is BRIGHT — the port's `rand == 0 ? $43 : rand | $40` was correct.

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

**`$DF31` itself contains no hit logic — but the laser DOES kill.**
(An earlier revision of this file concluded "the laser hits
nothing"; that was wrong — see the correction below.)  The kill
mechanism is INVERTED from what we searched for: it lives in the
TARGETS' draw code, not in the beam's.

### `$E9F0` — the kill check inside the ship/boss blitter

`$E9AC` (the ship + boss self-draw) calls `$E9F0` per sprite
column.  Before drawing, it walks the 8 destination screen bytes:

```
E9F8  LD B,$08
E9FA  INC (HL); DEC (HL); JR Z,$EA1D    ; empty byte → skip
E9FE  LD A,(DE); CP $EF                  ; screen byte == $EF (BEAM)?
EA01  JR NZ,$EA1D
EA03  EXX; PUSH BC
EA05  LD B,$00                           ; ★ alt-B = 0 → mark DEAD
EA07  PUSH HL; EXX
EA09  CALL $F958                         ; 50%-random kill jingle ($F96A msg)
EA0C  POP DE; POP BC
EA0E  LD C,B; LD B,$00
EA11  LD HL,($E459); ADD HL,BC
EA15  LD ($E459),HL                      ; ★ SCORE += remaining alt-B
EA18  CALL $EDDB                         ; ★ 8-particle death explosion
```

If any screen byte under the entity equals `$EF` — the beam
pattern — the entity dies: alt-B (the per-entity life counter the
caller loaded — `$0F` for ships at `$E95A`, `$14` for the boss at
`$EC53`) is zeroed, the score gains that remaining counter
(≈15 for a ship, ≈20 for the boss), a kill jingle plays half the
time, and `$EDDB` runs an 8-particle explosion at the kill site
(`$EEC2` scratch, 31 paint/step iterations — same family as the
player-death `$DBDA`).

The boss's caller then sees alt-B == 0 at `$EC66` and runs the
`$EC6C` reset: randomize X/Y, deactivate (it can respawn later —
`$EE83` counts the spawns, and ≥10 disables the alternate-frame
throttle, making subsequent bosses relentless).

So the design mirrors the player-damage system in `$DCF5`: **the
bitmap IS the collision system**.  The player checks "did I draw
onto something?"; the enemies check "is a beam byte where I'm
about to draw?".  Nothing ever compares coordinates.

### Correction history

The earlier "laser hits nothing" verdict came from (a) finding no
entity-match logic in `$DF31` — true but irrelevant, and (b) a
search for `RES 7,(IX+d)` finding nothing — also true but
irrelevant: ship death is signalled through the EXX-bank B
register and written back to the slot status by the AI loop, not
by an indexed RES instruction.  Lesson recorded in RE-LOG §62:
absence of one specific opcode pattern is not absence of the
behaviour.

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
- `Bullet.Span ≤ 15` — bytes painted at fire time, self-limited
  at scenery (`$DEDA`) and the screen edge; `Length` counts down
  from `Span` as the tail recedes
- 4 slots, NO fire cooldown (a press is ignored only when all 4
  slots are alive — `$DE41`)
- `Bullet.DX` sign = firing direction (used by the draw routine;
  the bullet doesn't actually translate — head is anchored)
- `Bullet.Attr` randomized per shot from `_rng.Next(0, 8) | $40`
- HEAD anchored at `b.X + (MaxLength - 1) * 8 * dir` (= fire-time
  far end).  Each frame `Length--`; draw walks BACKWARD from head
  for `Length` bytes — so the TAIL (= ship-side end) appears to
  recede outward.  Matches the original's visual: beam appears at
  full length on fire, then the near-ship end fades first.

### Visual model — history: traveling bolt, now reverted

An earlier port pass replaced the faithful model with a
"traveling-bolt" (head advancing 8 px/frame, 4-byte trail, plus
an invented 8-frame fire cooldown, 8 slots, and ±10/±12 px hit
boxes), because the faithful tail-recede read ambiguously at
60 fps.  **The fidelity audit (RE-LOG §66) reverted this**: the
port is back to the cassette model — 4 slots, no cooldown, full
span painted at fire time (self-limited at scenery per `$DEDA`),
tail receding one byte per frame, and hits scored when a target's
cell lies within the LIVE span on a scanline it covers (the
`$E9F0` semantics).  The bolt model survives nowhere; if the
recede ever needs re-clarifying visually it should be done as a
modern-flag option, not a silent swap.

Bugs found and fixed along the way (all reported by the user):

1. **`Y = PlayerY` (= altitude, top of ship) — 4 px too high.**
   Original uses `altitude + 4`.  Fixed.
2. **Beam covered most of the screen.**  Initial draw walked
   `Length` bytes from `b.X` in the wrong direction — toward
   negative X.  Fixed by reversing the draw direction.
3. **Far end stayed put while ship-side tail receded — perceived
   as "approaching the ship".**  Final fix: switched to a
   traveling-bolt visual that unambiguously moves outward.
