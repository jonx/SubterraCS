# Memory map — Subterranean Stryker (running snapshot)

This file accumulates every named address we identify while reversing.
Group by region. New entries get added as we go.

## 16 K ROM (Spectrum 48 K system ROM, not in snapshot)

| Address  | Name        | Notes |
| -------- | ----------- | ----- |
| `$1601`  | CHAN-OPEN   | Open a channel; A = channel number. Standard ROM routine. |
| `$1F3D`  | PAUSE-1     | Inner loop of the BASIC `PAUSE` command — snapshot's PC sits here. |
| `RST 10` | PRINT-A     | Print the character in A using the currently open stream. |

## RAM ($4000–$5AFF) — Spectrum video memory

Loaded with the game's title screen at boot (the `LOAD "" CODE` that
puts the bitmap at `$4000` and attributes at `$5800`). At gameplay
time the game's own draw routines write here directly.

## RAM ($5B00–$5BFF) — printer buffer

Spectrum system area. Game may repurpose this.

## RAM ($5C00–$5CB5) — system variables

Standard 48 K Spectrum sysvars. Notables touched by the game:

| Address  | Name      | Notes |
| -------- | --------- | ----- |
| `$5C7B`  | STKBOT    | Bottom of the BASIC calculator stack. Game sets it to `$E62B` (giving itself the run of `$E62B-$FFFF`). |
| `$5C8D`  | ATTR-P    | Permanent attributes. BASIC loader pokes 71 here (bright white on black). |
| `$5CBB`  | FLAGS2    | Game pokes 111 / 244 here to suppress the "press any key" prompt during LOAD. |

## RAM ($5CB6–$5CCA) — channels / data area

## RAM ($5CCB–$5D32) — BASIC loader program

Line 10 of the loader: BORDER NOT PI / POKE / CLEAR / LOAD "" CODE /
RANDOMIZE USR 28350 / POKE / LOAD "" CODE / POKE / PAUSE NOT PI /
RANDOMIZE USR 62973. See [RE-LOG.md §7](RE-LOG.md).

## RAM ($5E88–$E62A) — game data and code, block A

| Address  | Name             | Notes |
| -------- | ---------------- | ----- |
| `$6EBE`  | PreGameEntry     | First `RANDOMIZE USR` from BASIC; runs once before the main game starts. |
| `$E3B2`  | InitHelper       | Called twice early in MainEntry, presumably resets a UI/state structure. |

## RAM ($E45F)  — current frame's player input flags

The selected control method (set up by the title-screen menu via
the dispatch table at `$F741` / `$E461`) writes a packed bitmask
into `($E45F)` each frame:

| Bit | KEYBOARD option (the one the user picked) |
| --- | ----------------------------------------- |
| 0   | Enter — FIRE                              |
| 1   | L — horizontal move                       |
| 2   | row 0 (CAPS/Z/X/C/V) — ?                  |
| 3   | row 1 (A/S/D/F/G) — DOWN                  |
| 4   | row 2 (Q/W/E/R/T) — UP                    |
| 5   | row 0 again — ?                           |

The vertical-movement routine at `$D95D` consumes bits 3 and 4 to
update player altitude (`$E584`).

`($E461)` is a 16-bit pointer to the currently-selected input
handler routine; the input dispatcher at `$D8F0` does
`LD HL,($E461); JP (HL)`.

## RAM ($E583)  — game state lock

If non-zero, the main loop's pre-step routine at `$F868` returns
immediately; no scrolling, no enemy updates, no level advance. Used
to freeze the world during animations / death / level-complete.

## RAM ($E584)  — player altitude / depth counter

Range 0–120 (`$00`–`$78`). Pushing the DOWN key adds to it (one
unit per frame at base speed), pushing UP subtracts. The main loop
at `$F868` checks `CP $75; RET C` — i.e. the level only starts
*scrolling* and the world only advances when the altitude reaches
`$75` (117). At `$78` the player has reached the bottom of the
current section; the game resets `$E584` to 0 and the next page of
the level scrolls into view.

**Practical gameplay note:** at the start of a new section the sub
sits at altitude 0 and the world is static. The player must HOLD
the DOWN key for ~2 seconds to dive deep enough for the level to
start scrolling.

## RAM ($E585)  — vertical-speed shift

Used as `B = (SRL E585) | 1` to compute how many altitude units to
add per frame. With $E585 = 1 we add 1/frame.

## RAM ($E587)  — current level/page index

Word-sized; indexes into a level table.

## RAM ($6000–$60xx) — Follin music data (best guess)

A hex dump shows neat repeating *word* patterns at `$6000`:

```
6000  96 00 E6 00 1E 00 5E 01  1E 00 E6 00 1E 00 5E 01
6010  1E 00 E6 00 1E 00 5E 01  1E 00 FD 00 1E 00 5E 01
6020  1E 00 FD 00 1E 00 5E 01  1E 00 FD 00 1E 00 5E 01
```

That looks like (pitch, duration) pairs for the Spectrum beeper —
which lines up with Tim & Mike Follin doing the music. We had
initially mistaken this for a font because the 8×8 visualisation
of random word data accidentally resembles digit shapes; in fact
the real font lives inside the tile bank at `$B0F4` (numbers and
letters appear among the tiles there).

## RAM ($B0F4–$BFFF) — sprite tile bank

This is the master 8 × 8 tile sheet from which every in-game object
(player sub, enemies, terrain, explosions, HUD icons) is composed.

The draw routine at `$DAF2` is the key:

```
DAF3  6E          LD L,(HL)        ; read tile index from sprite-composition table
DAF4  26 00       LD H,$00
DAF6  29          ADD HL,HL        ; ×2
DAF7  29          ADD HL,HL        ; ×4
DAF8  29          ADD HL,HL        ; ×8 (bytes per tile)
DAF9  01 F4 B0    LD BC,$B0F4      ; tile bank base
DAFC  09          ADD HL,BC        ; HL = $B0F4 + index*8
DAFD  06 08       LD B,$08         ; 8 rows
DAFF  7E          LD A,(HL)
DB00  12          LD (DE),A
DB01  14          INC D            ; advance scanline within band
DB02  23          INC HL
DB03  10 FA       DJNZ $DAFF
```

So the bank is an 8 byte-per-cell flat array, indexed from
`$B0F4`. ~390 distinct 8×8 tiles are stored before the data drifts
into other purposes. Decoded sheet:
[`renders/scan-$B0F4-8x8_20260527-234030.png`](../renders/).

Recognisable contents:

* Rows 1-3: cave walls, dripping ceilings, stalactites.
* Row 4: trees, mountains, surface decoration.
* Row 5: buildings / structures (early-level surface).
* Row 6: small humanoid figures (the "RESCUED" people!).
* Row 7-8: vehicles / equipment / power-ups.
* Row 9: projectiles and sparks.
* Row 10+: misc creature/enemy parts.

## RAM ($E03D–$E045)  — overwrite tile-draw inner loop (used for HUD)

```
E03D  LD B,$08      ; 8 rows
E03F  LD A,(DE)     ; sprite byte
E040  LD (HL),A     ; overwrite (not XOR)
E041  INC H         ; next scanline (within an 8-row band)
E042  INC DE
E043  DJNZ $E03F
E045  RET
```

The 8-row block-copy used for *static* content. The caller sets DE
via the helper at `$E030`:

```
E030  LD BC,($5C36) ; tile-bank base
E034  LD H,$00; LD L,A
E036-E039  ADD HL,HL × 3   ; HL = tile index × 8
E03A  ADD HL,BC            ; HL = base + index*8
```

So *which* tile bank gets used depends on `($5C36)`. In our running
game it's `$3C00` — that's the Spectrum **ROM system font** (96
characters × 8 bytes at `$3C00-$3FFF`). So the HUD labels (DEPTH /
SCORE / SHIELD / FUEL / RESCUED) are drawn using the stock ROM font
through this routine. Game-specific graphics use the `$B0F4` bank
via the indirection at `$DAF2` (see below).

## RAM ($E1C1–$E1DD)  — 2×2 XOR sprite wrapper

The wrapper that drives `$E1DE` to draw the four corners of a 2×2
pixel-byte sprite (with bounds-check on B for row 0x3F..0xBC):

```
E1C1  LD A,B
E1C2  CP $BD; RET NC       ; off-screen bottom
E1C5  CP $3F; RET C        ; off-screen top
E1C8  PUSH BC; CALL $E1DE  ; draw at (B  , C  )
E1CC  POP BC; INC B
E1CE  PUSH BC; CALL $E1DE  ; draw at (B+1, C  )
E1D2  POP BC; INC C
E1D4  PUSH BC; CALL $E1DE  ; draw at (B+1, C+1)
E1D8  POP BC; DEC B
E1DA  CALL $E1DE           ; draw at (B  , C+1)
E1DD  RET
```

`A` is not modified between the four calls — each "2×2 sprite" is
either a single byte stamped four times in a square (chunky pixel),
or the caller updates A between two halves of an object. The
`scrwrite-trace` of frame 300 shows pairs like `80 80 40 40` —
i.e. the same byte is XOR'd twice (erase + redraw at the *other*
position), then a new byte is XOR'd twice. So a single moving
object = two XOR pairs = 4 writes total.

## RAM ($E1DE–$E1E3)  — XOR sprite-draw inner loop

```
E1DE  CD E4 E1    CALL $E1E4      ; compute screen address into HL
E1E1  AE          XOR (HL)        ; A = sprite-byte XOR screen
E1E2  77          LD (HL),A       ; write back
E1E3  C9          RET
```

**This is the master sprite-draw primitive for moving objects.** Every
in-game sprite (player sub, enemies, projectiles, ...) ends up being
drawn by repeatedly calling `$E1DE` with a single byte of sprite data
in `A`. Because it's XOR, the same call both *draws* (when the screen
byte was empty) and *erases* (when called again with the same bytes).
That's also exactly why the game flickers — between the erase pass
and the redraw pass, the sprite is briefly absent.

The `subterra scrwrite-trace` command confirmed it at runtime: out of
158 bitmap writes during one gameplay frame, **all 158** came from
the `LD (HL),A` instruction at `$E1E2`, with PC=`$E1E3` (the
following `RET`).

`$E1E4` is the geometry helper: given `B` (row in screen-space) and
`C` (column in screen-space), it produces the correct interleaved
Spectrum bitmap address via the screen-row → high-byte table at
`$E80F`. The two `SRL E` × 3 shifts divide by 8 to convert pixel
coordinates into character cells.

## RAM ($F5A0–$F5DF)  — entity-type table (4 bytes × ~16 types)

The master lookup that connects an entity *kind* to its sprite
graphics and colour. Each entry is 4 bytes:

| Offset | Meaning |
| ------ | ------- |
| +0..1  | Little-endian pointer to the 16-frame sprite bank for this entity type |
| +2     | Max frames in the bank (`$10` for full-size entities, `$08` or `$04` for smaller ones) |
| +3     | Attribute byte to paint into the entity's 8×8 cell (ink + paper + bright) |

First entries decoded from `build/post-game.bin`:

| Type | Sprite ptr | Frames | Attr | Decoded contents (from `entity-bank` PNG) |
| ---- | ---------- | ------ | ---- | ----------------------------------------- |
| 0    | `$B8F4`    | 16     | `$43` bright magenta | **The player submarine** — side view, drill, descent / explode frames |
| 1    | `$BAF4`    | 16     | `$42` bright red     | Lava / molten droplets, ascend & fall |
| 2    | `$BCF4`    | 16     | `$43` bright magenta | Cave-roof stalactite formations |
| 3    | `$BEF4`    | 16     | `$44` bright green   | Falling rocks / cave debris |
| 4    | `$C0F4`    | 16     | `$43` bright magenta | (TBD) |
| 5    | `$C2F4`    | 8      | `$46` bright yellow  | (TBD) |
| 6    | `$C354`    | 5      | `$02` red            | (TBD — note the unusual 5-frame count and the offset that isn't `…F4`) |
| …    | up to slot 22 (`$D6F4`) before the table fizzles into other purposes |

So the game has a clean **type → bank → 16 animation frames**
indirection. Adding a new enemy is just an entry in this table.

## RAM (entity sprite banks at $B8F4 onwards)

Each entity-type bank is **16 frames × 32 bytes/frame = 512 bytes**.
Per frame, the 32 bytes are laid out in a **column-major quadrant**
form:

| Byte offset | Maps to |
| ----------- | ------- |
| 0  – 7      | Top-left 8 rows × 1 byte (8 × 8 pixels) |
| 8  – 15     | Top-right 8 rows × 1 byte |
| 16 – 23     | Bottom-left 8 rows × 1 byte |
| 24 – 31     | Bottom-right 8 rows × 1 byte |

i.e. the four 8 × 8 quadrants are stored *vertically* (one byte per
scanline) rather than row-major. The decoder lives in
`QuadrantSpriteRenderer` in `Subterra.Assets`; the corresponding
CLI command is `subterra entity-bank`.

## RAM ($F1EF–$F2E1)  — per-entity 16 × 16 sprite draw

The routine that consumes the table above and produces actual
pixels on screen. Per entity:

```
F1F0  LD IY,$F5A0           ; entity-type table base
F1F4  LD E,(IX+$00)         ; entity type ID
F1F9  SLA E; SLA E          ; × 4 (each type-entry is 4 bytes)
F1FD  ADD IY,DE             ; IY = $F5A0 + type*4

F1FF  LD L,(IX+$02)         ; sprite frame index
...
F228  LD L,(IX+$02); LD H,$00
F22D-F231  ADD HL,HL × 5    ; HL = frame_index × 32
F232  LD E,(IY+$00); LD D,(IY+$01)
F238  ADD HL,DE             ; HL = sprite_bank_base + frame*32  → sprite source
```

Then `EX DE,HL` and four calls to `$F2BC`, each drawing one
8-row × 1-byte quadrant of the 16 × 16 picture to the screen
address held in (IX+3..4) for the top half and (IX+5..6) for the
bottom half. Attribute byte from `(IY+$03)` is written to the
appropriate cell at the end of every `$F2BC` call.

The IX entity layout (8 bytes per slot):

| Offset | Meaning |
| ------ | ------- |
| +0     | Type id (×4 → index into `$F5A0` type table) |
| +1     | `y` coordinate (used by `$F1F4` cheap "on-screen" check `CP $41`) |
| +2     | Current animation frame index (×32 → offset into sprite bank) |
| +3, +4 | Screen address (lo, hi) for the top half of the sprite |
| +5, +6 | Screen address (lo, hi) for the bottom half |
| +7     | (TBD — likely a flag bit, or x coordinate retained for redraw) |

## RAM (`($F1B9)`, `($F1BB)`)  — active entity list pointer + count

Inside the entity dispatcher at `$F1A5`:

```
F1AE  LD A,($F1BB)          ; B = entity count
F1B2  LD IX,($F1B9)         ; IX = entity list base
```

The pointer + count are patched per "slice" — the dispatcher cycles
through 4 slices (`$F593 = 0..3`), processing a different sub-list
per frame to time-slice the workload across four 50 Hz frames.

## RAM ($E881–$E8A0)  — particle / bullet table (8 × 4 bytes)

A small entity list of **8 slots × 4 bytes**:

| Offset | Meaning |
| ------ | ------- |
| +0     | `x` (pixel column / 1 — used directly as `C` in `$E1E4`) |
| +1     | `y` (pixel row — used directly as `B` in `$E1E4`) |
| +2     | `dx` (signed velocity, added to x each frame) |
| +3     | `dy` (signed velocity, added to y each frame) |

The draw loop at `$E199` walks IX through this table:

```
E199  LD IX,$E881
E19D  LD B,$08            ; 8 entities
E19F  PUSH BC
E1A0  LD A,(IX+$01); CP $41
E1A5  JR C,$E1B7          ; off-screen, skip
E1A7  LD C,(IX+$00)       ; x → C
E1AA  LD B,(IX+$01)       ; y → B
E1AD  CALL $E1C0           ; falls into $E1C1 → 2x2 XOR wrapper
E1B0  CALL $DB0E          ; compute attribute address
E1B3  LD H,A
E1B4  EX AF,AF'           ; swap to shadow A (the colour byte)
E1B5  LD (HL),A           ; paint the attribute cell
E1B6  EX AF,AF'
E1B7  LD BC,$0004; ADD IX,BC; DJNZ $E19F
```

Each entity is XOR-drawn as a **single byte**, with A being whatever
the caller set up before invoking the draw pass. In practice the
captured byte stream shows single-bit values like `$80, $40, $20,
$10, …` — i.e. the entities are 1-pixel dots that move along
trajectories given by `(dx, dy)`. Bullets and particles, in other
words.

The update loop sits immediately after (`$DC11`+):

```
DC11  LD IX,$E881; LD B,$08; LD DE,$0004
DC18  LD A,(IX+$01); CP $41; JR C,$DC31    ; off-screen → skip
DC1F  LD A,(IX+$00); ADD A,(IX+$02); LD (IX+$00),A   ; x += dx
DC28  LD A,(IX+$01); ADD A,(IX+$03); LD (IX+$01),A   ; y += dy
DC31  ADD IX,DE; DJNZ $DC18
```

So `$E881` is the **particle simulation buffer**. The player ship
and enemy sprites — being larger than one byte — must live in a
different table (TBD); the entity list here is dedicated to flying
dots.

## RAM ($E579–$E57A) — current sprite composition table pointer

Holds the base address of the array the sprite-draw routine at
`$DAA9` walks. Each entry in that array is a *tile index* (a single
byte) that the routine multiplies by 8 and adds to `$B0F4` to find
the 8 × 8 graphic. Sprite objects are stored as small 2D arrays of
these indices (a 16×16 game sprite is a 2 × 2 block of tile
indices — 4 bytes per logical sprite).

In the running game we measured `$E579 → $60F4`, i.e. the composition
table sits right after the game font in the same `$6000` block.

## RAM ($E62B–$F4FF) — buffers (STKBOT moved here)

The game points STKBOT (`$5C7B`) at `$E62B`, giving the BASIC
calculator stack ~3.5 KB. The game itself likely doesn't use the
calculator stack but moves it out of the way of its own data.

## RAM ($F5FD–$FFFF) — game code, block B

| Address  | Name             | Notes |
| -------- | ---------------- | ----- |
| `$F5FD`  | MainEntry        | Real game entry. Sets up screen, prints title, polls keyboard. |
| `$F82B`  | TitleStringTable | `AT 8,8 INK 0 PAPER 0 "BY  MIKE FOLLIN"` then UDG decorations, `$FF`-terminated. |
| `$FF57`  | Flag57           | First touch in `MainEntry`: `RES 1,(HL)` clears bit 1 of `$FF57`. Purpose TBD. |
