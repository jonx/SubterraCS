# Horizontal scroll — `$D9C8` → `$DA23` / `$DA62`

## Entry

`$D9C8` is the L-key handler.  When L is held, it picks one of two
scroll routines based on the current facing bit (`$E586` bit 0):

```
D9C8  21 5F E4    LD HL,$E45F
D9CB  CB 4E       BIT 1,(HL)         ; bit 1 of $E45F = L key
D9CD  23          INC HL              ; HL = $E460
D9CE  C2 F1 D9    JP NZ,$D9F1        ; if L pressed, scroll
D9D1  CD 15 DA    CALL $DA15          ; (no-op delay)
D9D4  CB 4E       BIT 1,(HL)
D9D6  C0          RET NZ
D9D7  21 01 58    LD HL,$5801         ; not pressed: paint top attr strip
...

D9F1  CB 8E       RES 1,(HL)          ; clear bit 1 of $E460
D9F3  3A 66 E4    LD A,($E466)        ; load fuel
D9F6  A7          AND A
D9F7  CA 15 DA    JP Z,$DA15         ; if fuel=0, can't scroll
D9FA  3A 86 E5    LD A,($E586)        ; load DirectionState
D9FD  E6 01       AND $01             ; mask facing bit
D9FF  3D          DEC A
DA00  CA 23 DA    JP Z,$DA23         ; facing=1 → scroll right
DA03  C3 62 DA    JP $DA62           ; facing=0 → scroll left
```

So pressing L drains fuel and scrolls the level one byte in the
current facing direction.  The facing bit *persists* — pressing L
again scrolls in the same direction.  Direction-change keys are
handled elsewhere (likely `$DCC0..` block).

## `$DA23` — scroll the bitmap one column LEFT (ship moves right)

```
DA23  LD C,$00
DA25  CALL $DF31              ; (sound effect: scroll click)
DA28  LD HL,$4001             ; src = bitmap[0] + 1 (skip leftmost col)
DA2B  LD DE,$4000             ; dst = bitmap[0]
DA2E  LD B,$00
DA30  LD A,$80                ; A = $80 = 128 scanlines (whole top half)
DA32  LD C,$1F                ; C = 31 (one scanline worth)
DA34  LDIR                    ; copy 31 bytes → shifts scanline left by 1 byte
DA36  INC HL; INC DE           ; advance past the gap to next scanline
DA38  DEC A; JP NZ,$DA32      ; 128 iterations
DA3C  LD HL,$5801             ; same for attribute file
DA3F  LD DE,$5800
DA42  LD BC,$01FF
DA45  LDIR                    ; shift attributes left
DA47  LD C,$EF; CALL $DF31    ; another sound
DA4C  LD A,($E586); OR $01; LD ($E586),A    ; set facing = 1
DA54  LD E,$01; CALL $DB06    ; advance scroll source pointer
DA59  LD DE,$401F             ; DE = rightmost col of top scanline
DA5C  LD BC,$001F
DA5F  JP $DAA9                ; paint freshly-exposed column
```

128 iterations × 31 bytes = 3968 bytes shifted left + the attr
file (511 bytes) → the entire playfield (bitmap top half + all
24 attr rows) slides one tile-column to the left.  The rightmost
column then receives fresh tile data from the source pointer.

## `$DA62` — scroll the bitmap one column RIGHT (ship moves left)

Symmetric to `$DA23` but uses `LDDR` (descending) to shift the
bitmap right by 1 byte per scanline.  After scrolling it sets
`$E586` bit 0 = 0 (facing left) and paints the leftmost column.

## `$DAA9` — paint freshly-exposed column

Reads 16 tiles from the source pointer (`$E579`) — one per char
row, stride 256 — and blits them into the column at `DE`.  This
is the new content "coming into view" as the player scrolls.

The source data is the per-level tile-index buffer (e.g. `$60F4`
for level 1).  Each row has 256 tile-index bytes; the visible
window is 32 cols at any time.  So a level is effectively 256
tile-columns wide (8 screens) and can be traversed by repeated
L-press scrolling.

## Can the cassette move horizontally at pixel precision?  NO — verified

Question raised while reviewing the port's Shift precision modifier
("the original looked like it could position precisely — maybe a mix
of ship movement and level movement?").  Checked three ways:

1. **No pixel-scroll routine exists.**  A bitmap scroll finer than
   one byte would need `RL (HL)` / `RR (HL)` rotate loops.
   `find-bytes` for `CB 16` and `CB 1E` over the whole 48 K program:
   **zero hits**.  The only horizontal scrolls are the byte-granular
   `LDIR`/`LDDR` routines `$DA23`/`$DA62`, each with exactly one
   caller (`$D9CB` dispatch, verified by `find-bytes` on the jump
   targets).

2. **The ship's screen X is hard-fixed.**  The player draw reads the
   4-quadrant screen addresses at `$E8C9`; the only writers are:
   * `$E2FC` (level init) — copies the static 8-byte home table at
     `$E8A1` = `$400F $4010 $402F $4030` → columns 15/16, x=120..135;
   * `$DDEB` (per-char-row recompute) — `row base + $10` with the
     `$0010` as an immediate (`LD BC,$0010` at `$DDFF`); `find-bytes`
     shows no self-modifying writes to `$DE00`.

3. **The "precision mix" is real but VERTICAL.**  The ship moves
   1 px/frame vertically (`$D95D` altitude + the `$DCAC` staged-
   sprite shifter, see [player.md](player.md)), and the *level*
   scrolls a page when altitude crosses `$75`.  Horizontally the
   roles never mix: the ship never moves, the level steps 8 px.

So the cassette's horizontal quantum is **8 px** (one byte-column
per frame while the scroll key is held); the port's Shift 1-px
sub-byte scroll remains a port-only extension with no cassette
counterpart (see [input.md](input.md) "Port-only addition").

## Port notes

In `LevelScroll.PaintLevelAtOffset(tileBank, levelBuffer, offsetX)`
we just track the offset and re-paint the entire scenery — simpler
than the original's LDIR-shift-then-paint-new-column dance, and
equivalent in result.

In `World.TickPlaying`:
- LEFT key sets `FacingLeft = true` (`$E586` bit 0 = 1)
- RIGHT key sets `FacingLeft = false` (`$E586` bit 0 = 0)
- L key (or either arrow) holds `Horizontal = true`
- When `Horizontal && Fuel > 0`, advance `ScrollOffsetX` by ±1
  each frame and call `PaintLevelAtOffset`.

`ScrollOffsetX` resets to 0 on level load.
