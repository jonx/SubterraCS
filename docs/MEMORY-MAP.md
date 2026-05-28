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

## RAM ($E583)  — game state lock / entity vertical-shift cursor

Dual purpose:

1. **Lock**: if non-zero, the main loop's pre-step routine at
   `$F868` returns immediately; no scrolling, no enemy updates,
   no level advance. Used to freeze the world during animations
   / death / level-complete.

2. **Entity draw offset**: `$F222 SUB B` uses `($E583)` as the
   value B, subtracting it from each entity record's +1 byte to
   produce the per-entity bitmap-offset.  So a non-zero `$E583`
   shifts ALL entity sprites by the same amount within their
   char-row.  Normally 0 during gameplay (verified across all
   `at-fXXX.bin` snapshots f50..f400).  Only seen non-zero
   pre-gameplay (e.g. `at-f40.bin` = $20 during title
   transition).

## RAM ($E584)  — player altitude **= ship screen Y**

Range 0–120 (`$00`–`$78`). Pushing the DOWN key adds to it (one
unit per frame at base speed), pushing UP subtracts.

**`$E584` is literally the ship's screen Y coordinate.**  Verified
by inspecting `$E8C9` (the 4 quadrant bitmap addresses used by
`$DCF5` to draw the player) across capture states:

| Capture            | `$E584` | `$E8C9` decode      |
| ------------------ | ------- | ------------------- |
| `at-down-f100.bin` | `$00`   | (120, 0)            |
| `at-down-f310.bin` | `$51`   | (120, 80)           |

So the ship MOVES on screen with altitude — it is NOT fixed at
the top of the playfield.  X is fixed at 120, Y = altitude.
The death-anim formula at `$DBEB` (`$BF - altitude`) places
particles at Y = 191 − altitude, which is below the ship (when
altitude is small) — i.e. particles spawn "where the ship would
crash" if it kept descending.

At `$78` the player has reached the bottom of the playfield (one
row above the HUD strip at y=128); `$F868`'s gate (`CP $75; RET C`)
opens and the world advances to the next page via `$F6F2`.

The level scenery itself is STATIC per page — there's no
continuous scroll.  Each level page is painted once by `$DB1A`
at level-load and stays static while the ship traverses the
page.

## RAM ($E585)  — vertical-speed shift

Used as `B = (SRL E585) | 1` to compute how many altitude units to
add per frame. With $E585 = 1 we add 1/frame.

## RAM ($E587)  — current level/page index

Word-sized; indexes into a level table.

## RAM ($5E88–$~$6FFF) — Follin music data + player routine

The music data lives **above CLEAR'd RAMTOP at `$5E88`** — a long
stream of 16-bit little-endian period values (one note per pair).
The player routine at `$FA32` is a hand-tight Spectrum beeper
driver:

```
FA32  LD IX,$5E88      ; music data base
FA36  LD HL,$FF51      ; working buffer
FA39  LD ($FF54),HL
FA3C  DI               ; precise timing — interrupts off
FA3D  LD L,(IX+$00)    ; period lo
FA40  LD H,(IX+$01)    ; period hi
FA43  INC IX; INC IX   ; next note
FA47  LD DE,$00FF      ; pulse counter
FA4A  LD A,$00; OUT ($FE),A   ; speaker low
FA4E  LD B,D; DJNZ $FA4F      ; delay
FA51  LD A,$10; OUT ($FE),A   ; speaker high (bit 4)
FA55  LD B,E; DJNZ $FA56      ; delay
... INC E; DEC D pitch-glide ...
```

That `INC E; DEC D` pair is the **Follin sweep trick** — by slowly
shifting the proportion of pulse-low to pulse-high time, the
Follins gave a single channel a pitch glide that sounded like
multiple notes. Called from the title screen at `$F64E` and
`$F65D`.

The earlier hex dump of `$6000` (`96 00 E6 00 1E 00 5E 01 …`) is
mid-stream music data — *not* a separate font. Apologies to my
earlier self.

## RAM ($E56D–$E580) — per-level sprite-table pointer

A 6-entry table of 16-bit pointers, one per level (0..5). Decoded:

| Level | Sprite ptr |
| ----- | ---------- |
| 0     | `$B0F4`    ← the master tile bank |
| 1     | `$60F4`    |
| 2     | `$70F4`    |
| 3     | `$80F4`    |
| 4     | `$90F4`    |
| 5     | `$A0F4`    |

Read by `$E2C6` and stored into `$E579` (the active sprite
composition table pointer).

## RAM ($E57C–$E582) — per-level "speed/colour byte"

6 bytes, one per level: `07 04 03 06 02 01`. Stored into `$E57B`,
which the rest of the engine uses as an attribute / animation
multiplier.

## RAM ($E58B–$E596) — per-level second pointer

6 more 16-bit pointers (purpose: TBD — probably the level-specific
particle / projectile bank).

## RAM ($E69D–$E75C) — per-level enemy-spawn schedule

**The level "design".** A 6-entry × 32-bytes-per-level table. Each
32-byte block is 8 entries × 4 bytes:

| Byte | Meaning |
| ---- | ------- |
| +0   | Spawn timer (lo) |
| +1   | Spawn timer (hi) |
| +2   | Entity-type index (into `$F5A0` table) |
| +3   | Flags (bit 5/7 used by the executor at `$EF02`) |

The level loader at `$F6F2` copies the active block to
**`$E75D..$E77C`** (32 bytes), where the spawn-executor at
`$EF02` walks the 8 entries each frame, decrements the timer,
and spawns when it reaches zero. This is the entire level
format. The visible cave is *not* a pre-drawn map — it's
**procedurally composed** from timed entity spawns (stalactites,
falling rocks, lava, enemies, mine carts, …) drawn through the
entity system documented above.

That's why the game data is so compact: 6 levels × 32 bytes =
**192 bytes of level design**, plus the per-level pointer
tables, plus the sprite banks. No tile maps, no compressed
levels, no streamed terrain.

## RAM ($E75D–$E77C) — live spawn-executor state (32 bytes)

The per-level schedule from `$E69D + level*32`, copied here at
level start. Walked each frame by `$EF02`.

## RAM ($FA32–$FA9x) — Follin beeper player routine

See the `$5E88` entry. Entry point `$FA32`; uses the data at IX
($5E88) as a stream of (period-lo, period-hi) pairs. The pitch-
slide is the (INC E ; DEC D) loop.

## Main game loop ($D7FB–$D826) — phase-by-phase

The 12 calls in order, with what each phase does:

| Address | Phase                              | Notes |
| ------- | ---------------------------------- | ----- |
| `$F868` | Pre-step gate                     | Returns immediately unless player altitude `$E584 ≥ $75` and game-state lock `$E583 == 0`. Once open, advances the world via `JP $F6F2`. |
| `$D827` | Scroll counter update             | Maintains `$EE74` against the current level index. Drives the vertical-scroll animation. |
| `$D8C2` | Input snapshot + dispatch         | Copies current player state to backup vars (`$E45B`, `$E45C`), then `CALL $D8F0` — the input dispatcher that fans out via `($E461)` to the active control method. |
| `$DCAC` | Player sprite stage               | Calls `$E3F4` to copy the new directional frame into the working buffer at `$E8A9`; then handles altitude-mod-8 logic. |
| `$DC5D` | Player attribute paint           | Walks IX = `$E8F1` (the 4-quadrant address table again, sister of `$E8C9`), writing the level-coloured attribute byte (from `$E57B`) into each cell the player occupies. |
| `$F1A5` | **Entity dispatcher**            | 4-frame time-slicer (`$F593`) → walks the active entity list (`$F1B9`/`$F1BB`), draws each via `$F1EF` (which decodes type from `$F5A0` table and blits 32 bytes through `$F2BC`). |
| `$D9C8` | Horizontal-move logic            | Reads `$E45F` bit 1 (the "L" key), updates horizontal position, then paints the colour strip at `$5801` (attribute row 0) with the player's level colour. |
| `$DCF5` | **Player draw (XOR)**            | The dedicated player drawer — see its own MEMORY-MAP entry above. The source of the in-game ship flicker. |
| `$DFAF` | "Effect" tick                    | Two `CALL $EB62` invocations with `$EE76` — almost certainly the explosion / particle ageing tick. |
| `$E248` | Dual coordinate transform        | Calls `$E25E` twice to map both old and new `(altitude, frame)` pairs into screen-byte addresses. Used to compute the player's footprint for collision / scroll. |
| `$E8FD` | Compound bullet/projectile pass  | Chains 6 sub-calls (`$E213`, `$E920`, `$EC10`, `$E213`, `$ED00`, `$DD4D`) — the projectile / fire-button handler. |
| `$DE2A` | Workers / rescue pass            | Walks IX = `$E46B` for 4 entries (8 bytes apart? — table TBD), tests bit 7. Together with `$E45F` bit 0 (fire key) this is almost certainly the **rescue-pickup mechanic**. |
| `$EF02` | **Spawn-schedule executor**      | Walks the 8-entry list at `$E75D`, decrements per-entry timers, spawns the indicated entity when each timer rolls over. The heartbeat of the level. |
| `$E046` | HUD draw                         | Renders SCORE / FUEL / SHIELD / DEPTH / RESCUED via the ROM-font copy at `$E03D`. |

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

## RAM ($E63B–$E66B)  — **the player Stryker's sprite source**

Two 16-byte directional frames of the player's craft, used by the
dedicated XOR-draw at `$DCF5`:

| Address | Contents |
| ------- | -------- |
| `$E63B` | 16 bytes — Stryker pointing *right* (eight scanlines × 2 columns = top half only; the lower half of the 16×16 buffer is intentionally blank — the ship is 16 × 8 pixels) |
| `$E64B` | 16 bytes — Stryker pointing *left*  |
| `$E65B+` | Effect / explosion / damage frames (checkerboard `55 AA …`-style patterns) |

The direction is selected by `($E586) BIT 0`. The frame is copied
into the working buffer at `$E8A9` each cycle by the routine at
`$E3F4`:

```
E3F4  LD HL,$E8A9; LD DE,$E8AA; LD BC,$001F
E3FD  LD (HL),$00; LDIR                       ; clear 32-byte sprite buffer
E401  LD DE,$E8A9
E404  LD HL,$E63B                              ; source = player bank
E407  LD BC,$0000
E40A  LD A,($E586); BIT 0,A
E40F  JR NZ,$E413
E411  LD C,$10                                  ; if facing-flag set, advance to $E64B
E413  ADD HL,BC; LD C,$10; LDIR                 ; copy 16 bytes into the buffer
```

So the Stryker is **not** in the entity table at `$F5A0`. The
player has its own purpose-built draw path at `$DCF5` and its own
source-frame bank at `$E63B`. This is the routine that produces
the flicker we documented in [RE-LOG §12](RE-LOG.md): each frame
it XOR-redraws the ship in place, which means the ship is
*absent* in the framebuffer for exactly the window between the
erase pass and the redraw pass.

## RAM ($E8A9–$E8C8)  — player working sprite buffer (32 bytes)

Holds the current frame of the player sprite that `$DCF5` is
about to XOR onto the screen. Only the top 16 bytes (TL + TR
quadrants) are populated for the standard side-view ship; the
lower 16 bytes are intentionally zero. Replaced each cycle from
the `$E63B` bank by the routine at `$E3F4`.

## RAM ($E8C9–$E8D0)  — player 4-quadrant screen-address array

Four little-endian 16-bit screen addresses (TL / TR / BL / BR)
that `$DCF5` reads via IX to figure out where on the bitmap the
sprite should be XOR'd. Updated by the input/movement code as the
player flies around.

## RAM ($DCF5–$DD49)  — **player XOR sprite drawer**

The custom draw routine for the Stryker:

```
DCF5  LD IX,$E8C9      ; the four screen addresses
DCF9  LD DE,$E8A9      ; the 32 sprite bytes
...
DD17  LD C,$04         ; four columns
DD19  LD H,(IX+$01); LD L,(IX+$00)
DD1F  LD B,$08         ; eight rows per column
DD21  LD A,(DE)
DD22  AND A; JR Z,$DD2E                ; skip transparent rows
DD25  INC (HL); DEC (HL); JR Z,$DD2C   ; "is this screen byte zero?"
DD29  EX AF,AF'; SCF; EX AF,AF'        ; flag "we wrote ink here"
DD2C  XOR (HL); LD (HL),A              ; XOR-draw
DD2E  INC H; INC DE
DD30  DJNZ $DD21
DD32  INC IX; INC IX
DD36  DEC C; JP NZ,$DD19
DD3A  EX AF,AF'; CALL C,$DD4A          ; if any pixel landed, post-process
DD3E  LD BC,$0028; LD HL,$E8A9; LD DE,$E8D1; LDIR
                                       ; copy current sprite to "previous" buffer
                                       ; so next frame's XOR-erase has the right pattern
```

The four-iteration column loop is what makes the player a 16 × 16
sprite (the `INC IX; INC IX` advances the screen-address pointer
between columns); the inner loop draws an 8-row column. Note the
shadow-A trick: the shadow carry flag is set whenever any
non-transparent byte was actually drawn, then `$DD4A` is invoked
to do attribute / collision handling.

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
| 0    | `$B8F4`    | 16     | `$43` bright magenta | **Workers' digging-tool animation** (shovel/pickaxe heads swung mid-air, dirt particles scattered). Originally mis-identified as the player — that was wrong; the player has its own draw path. The rescued workers swing tools as their idle animation. |
| 1    | `$BAF4`    | 16     | `$42` bright red     | Lava / molten droplets — rising & falling frames |
| 2    | `$BCF4`    | 16     | `$43` bright magenta | Cave-roof stalactite formations |
| 3    | `$BEF4`    | 16     | `$44` bright green   | Falling rocks / cave debris |
| 4    | `$C0F4`    | 16     | `$43` bright magenta | Magenta flying drones / beetles |
| 5    | `$C2F4`    | 8      | `$46` bright yellow  | Yellow mine cart / train carriages |
| 6    | `$C354`    | 5      | `$02` red            | Red wagons (note: 5 frames, unusual offset `…54` not `…F4`) |
| 7    | `$C3F4`    | 16     | `$45` bright cyan    | Cyan dust / sparks / debris cloud |
| 8    | `$C5F4`    | 16     | `$46` bright yellow  | Yellow drill-tip explosion / impact cloud |
| 9    | `$C7F4`    | 8      | `$45` bright cyan    | Cyan flame / liquid drips |
| 10   | `$C8F4`    | 8      | `$45` bright cyan    | Cyan branching trees / roots |
| 11   | `$C9F4`    | 8      | `$44` bright green   | Green spider/octopus creatures |
| 12   | `$CAF4`    | 16     | `$45` bright cyan    | Cyan bubbles emerging / popping |
| 13   | `$CCF4`    | 16     | `$01` blue           | Blue radial pattern (force-field?) |
| 14   | `$CEF4`    | 16     | `$43` bright magenta | Magenta tubes / pipe segments |
| 15   | `$D0F4`    | 16     | `$46` bright yellow  | Yellow bow-tie / interlocking shapes |
| 16   | `$D2F4`    | 16     | `$45` bright cyan    | Cyan robot-style figures |
| …    | up to slot 22 (`$D6F4`) before the table fizzles into other purposes |

So the game has a clean **type → bank → 16 animation frames**
indirection for monsters, decor, and effects. **The player Stryker is
not in this table** — see `$E63B` above for its source bank and
`$DCF5` for its dedicated draw routine.

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

## RAM ($E785–$E7FF) — HUD layout string table

The bytestream the HUD-painter at `$E347` walks via `RST 10` to
draw the bottom-strip chrome. Spectrum control codes inline with
literal characters and `$90` filled-block characters:

```
E785  10 06 11 00 16 10 00              ; INK 6 (yellow), PAPER 0, AT 16,0
E78C  "DEPTH :" 0D                       ; row 16 + newline
E794  "SCORE :"                          ; row 17
E79B  16 11 16 "RESCUED:" 0D             ; AT 17,22 — "RESCUED:" anchor
E7A7  "SHIELD:" 10 00 11 00 20            ; row 18, INK 0 PAPER 0, gap
E7B3  10 02 90×5  10 03 90×5  10 06 90×5  10 05 90×5  10 04 90×4 0D
      ; SHIELD stripe bar: 5 red + 5 magenta + 5 yellow + 5 cyan + 4 green
      ; = 24 cells × 8px = full width
E7D6  11 00 10 06 "FUEL  :" 11 00 10 00 20
      10 02 90×5  10 03 90×5  10 06 90×5  10 05 90×5  10 04 90×?
      ; same stripe pattern for FUEL on row 19
E7FE  $FF                                ; terminator
```

The bars *deplete* by the game overwriting trailing cells with
the PAPER-0 attribute, which turns INK-X-on-PAPER-0 cells into
INK-0-on-PAPER-0 → invisible. So full strength = rainbow, drain
from the right.

## RAM ($E48D–$E54C) — per-level mini-map+collision init data

Six 32-byte blocks, one per level.  `$E319` copies the active
level's block to `$E597` at level-load.  Format: **8 records ×
4 bytes** per level, where each record is:

| Offset | Meaning |
| ------ | ------- |
| +0     | X (compared to player at `$DD8C` collision; passed as the mini-map column to `$E235`) |
| +1     | Y (used by `$E235` as `30 - Y/4` to map to mini-map row) |
| +2     | Status (bit 7 = alive) |
| +3     | Reserved (always 0 in observed data) |

`$E235` draws a single byte per record onto the **mini-map
strip** (y=160..191), via `$E1E4` math that resolves the
scanline to `161 + Y/4` — so this is NOT a playfield draw.
The records double-serve as collision points: `$DD8C` matches
their X against `($E583) + $0F`.

This is distinct from the `$F2EB`-style records (fixed playfield
decor; see [`disasm/entities.md`](disasm/entities.md)).

## RAM ($E48B) — enemy-ship AI 4-cycle counter

Increments 0→1→2→3→0 each frame inside `$E920`.  Used to index
the per-cycle sprite-data table at `$E5DB`.

## RAM ($E5DB–$E5FA) — enemy-ship animated sprite (4 frames × 8 bytes)

The 4 animation frames of the alien-ship 8×8 sprite.  Verified
contents (from `at-f100.bin`):

```
bank 0: 3C FF DB 7E 66 A5 42 00
bank 1: 3C FF DB 7E 66 81 66 00
bank 2: 3C FF DB 7E 66 81 42 42
bank 3: 3C E7 FF 7E 66 81 42 81
```

Decoded as 8×8 glyphs, these are the 4 animation frames of a
flapping-wing alien ship.  Used by `$E9AC` (called from the
`$E920` AI chain) to draw one frame per cycle into the bitmap.

## Code ($D827) — scroll-progress counter update

Called every frame from the main loop (`$D7FE`).  Updates
`($EE74)` by adding a level-scaled step:

```
HL = ($EE74); DE = ($E587)  ; DE = (level, ?)
DE.H = 0
DE.L = ((level + 3) >> 3) + 1   ; step = 1 at level 1, 2 at level 6
HL += DE  (saturated)
($EE74) = HL
```

Used by `$EC10` for the boss-spawn gate: boss eligible once
`$EE74 > $4A38`.

## RAM ($EE73) — every-other-frame toggle for $E920

XOR'd with `$01` at the top of `$E920`; RET Z makes the AI
process only on alternate frames (`$E924..$E92C`).

## RAM ($EE74) — scroll-progress counter (16-bit)

Saturating counter incremented by `$D827` every frame.  Boss at
`$EC10` becomes eligible when `($EE74) > $4A38` (~19000 ticks).

## RAM ($EE9E–$EEC1) — enemy ship live table

6 slots × 6 bytes.  Spawned dynamically by `$EBB2`, ticked by
`$ED01`.  Each record:

| Offset | Meaning |
| ------ | ------- |
| +0     | X (world byte position 0..255) |
| +1     | Y (pixel) |
| +2     | DX (-1, 0, +1) — aim toward player at spawn |
| +3     | DY (-1, 0, +1) — aim toward player at spawn |
| +4     | Status (bit 7 = alive; bit 5 = blink toggle) |
| +5     | Lifetime counter (DEC per tick; expire at 0) |

Empty across all `at-fXXX.bin` idle snapshots (no input); 2
records alive in `at-down-f310.bin` (player held DOWN).

Drawn as **single-byte attribute flashes** at the resolved
screen address (no 16×16 sprite); blink toggle inverts the
attribute every frame.  See
[`disasm/enemies.md`](disasm/enemies.md).

## Code ($E920) — enemy-ship AI dispatcher

Top-level per-frame ship AI.  See
[`disasm/ship-ai.md`](disasm/ship-ai.md) for the full trace.

- Every-other-frame skip via `$EE73` toggle.
- 4-cycle counter `$E48B` advance.
- Per slot (7 slots at `$E597`): draw via `$E9AC` ×2 if alive,
  else (re-)init via `$EADE`.  Movement loop calls `$EB00` →
  `$EB5B` (scenery + scroll-tick) → `$EB47` (reverse on hit).
  Always end via `$EAA3` → `$EB99` (fire-bullet gate, falls into
  `$EBB2`).

## Code ($EADE / $EB00 / $EB3E / $EB47 / $EB52) — ship AI helpers

`$EADE` randomizes the ship's 3 AI state bytes.  `$EB00` steps
the animation/movement counter in `[$04..$70]` with direction
reverses at the endpoints.  `$EB3E`/`$EB47`/`$EB52` are bit-
toggle helpers that flip direction bits (5, 5+6, 6 respectively).

## Code ($EB5B / $EB62) — scroll-tick + scenery probe

`$EB5B` calls `$D827` to bump scroll progress, then falls into
`$EB62` which probes the level scenery at the AI pointer's
(X, Y).  Returns ZF=1 if the tile is 0 (open space).  Also used
by `$DFAF` for the player's own wall collision.

## Code ($EB7A) — enemy-ship-vs-player collision

Walks `$E8C9` (player's 4 quadrant addresses) comparing each to
HL (the ship's drawn screen address).  On match, calls `$DD4A`
to fire the hit-sound + shield-decrement + possible-death chain.
Symmetric to `$EDC0` for bullets.

## Code ($EAB2 / $EABD) — range check + offset compute

`$EAB2` returns CF=1 if a ship's `X - $E583 ≥ $20` (= outside the
scroll window).  `$EABD` precomputes the IX (= `$E80F[char_row]`),
DE adjusted, and A=pixel-offset for the next `$E9AC` blit.

## Code ($EAA3 / $EB99) — fire-bullet gate

Called at end of each ship's tick.  `$EB99` checks the slot's
sub-byte (HL[+3]) — if non-zero, RET (no fire).  Random gate
`LD A,R; AND $0F; CP level; RET NC` — fire-rate scales with level.
On pass, falls through into `$EBB2` (the spawn into `$EE9E`).

## Code ($E910) — RNG state mutation

Reads `($EE7A)` as the current RNG state, mixes in `R` (Z80
refresh register) and memory chain, stores back.  Used by `$E920`
to seed per-ship behaviour each frame.

## RAM ($EE7A) — ship AI RNG state (16-bit)

Updated by `$E910` each cycle.

## RAM ($EE7D..$EE8E) — boss live slot (20 bytes)

| Offset | Meaning |
| ------ | ------- |
| $EE7D | X (world byte) |
| $EE7E | Y |
| $EE7F | AI state byte (cycled in $ECBD) |
| $EE80 | Last X-direction sign |
| $EE81 | Cycle counter (1..12) |
| $EE82 | Alternate-frame toggle |
| $EE83 | Kill count |
| $EE84..$EE87 | Per-cycle speed table (rotated by $EE81) |
| $EE8E..$EE91 | Mirrored state for next-frame draw |

## Code ($EC4C) — boss tick body

Movement + draw for the single boss slot.  Same `$EAB2` /
`$EABD` / `$E9AC ×2` draw chain as ships, plus its own movement
algorithm using `$EE81` to pick a per-cycle speed from `$EE84[]`
and direction logic at `$ECA0..$ECCE` that chases the player.

## Code ($DFAF) — player-scenery collision + fuel pickup

Called from main loop at `$D813`.  Probes the level scenery at
player position (= `$E583+15`, `$E584`) using `$EB62`.  If the
tile is `$01` (= solid wall), JP `$DBC8` (death).

Also checks `($E589)` for a worker/pickup target match — if
the player is at the right (X, Y) AND fuel < `$5F`, calls
`$F90E` (fuel sound) + `$E419` (refill animation).

## Code ($DCAC) — player sprite bank-shifter

Called from main loop at `$D804`.  Shifts the table at
`$E8B0..$E8C8` (= 7 banks × 4 quadrant addresses) by one
sub-position when `altitude & 7 != 0`.  Keeps the player's
draw addresses coherent as altitude moves through fractional
char-rows.

## Code ($F8F9 / $F93A / $F90E) — print-stream alerts

Print-stream routines that emit a fixed text message via
`$FA0A` (the print-stream interpreter):

- `$F8F9` — 11-byte msg at `$F904` (called by `$EC10` on boss spawn)
- `$F93A` — 13-byte msg at `$F945` (called by `$E920`'s `$E97A`
  path = "no more spawns" / max-density warning?)
- `$F90E` — 9-byte msg at `$F919` (called by `$DFAF` fuel pickup)

Text content TBD (depends on the print-stream opcode set).

## Code ($E419) — bar-fill refill animation

```
E419  LD A,$FF; LD ($E463),A; LD ($E465),A   ; reset hit/fuel accumulators
E421  XOR A; LD B,$30                          ; B = 48 iterations
E424  PUSH BC
E425  ADD A,$02                                ; value += 2 each iter
E427  LD ($E464),A; LD ($E466),A               ; shield + fuel = value
E42A..E43B  per-iter beep (descending pitch)
E43D  CALL $E0AB                                ; redraw HUD bars
E440..E444  POP BC; DJNZ $E424                  ; loop
E446  LD A,$5F; ($E466) = ($E464) = $5F        ; final cap
E44E  LD A,$FF; ($E465) = ($E463) = $FF
E456  RET
```

Used for both level-start refill AND fuel-pickup refill (called
from `$DFEB` after `$DFAF` detects worker overlap with low fuel).

## Code ($EBB2) — enemy ship spawn

Random-rate spawner: `LD A,R; AND $0F; CP B(level); RET NC`.
~1/16 chance at level 1, ~5/16 at level 5.  Finds the first
free slot in `$EE9E` and writes (X, Y, DX-sign, DY-sign,
alive, lifetime).  Spawn coordinates come from HL, set by the
caller (TBD).

## Code ($ED01) — enemy ship per-frame tick

For each alive slot in `$EE9E`: erase last position with level
colour, decrement lifetime (expire on 0), `X += DX`, `Y += DY`
(expire on negative Y), test scenery collision (`$EB62`), test
horizontal-range (`$ED8A`), paint new position bright white,
test player collision (`$EDC0`).

## Code ($EDC0) — enemy hits player

Compares enemy's screen address `($EE78)` against the 4 player
quadrant addresses in `$E8C9`.  On match: `CALL $DD4A` — fires
the hit-sound + shield-decrement + possible-death chain.

## Code ($DB06) — world-scroll cursor update

```
DB06  LD A,($E583); ADD A,E; LD ($E583),A; RET
```

Called by both horizontal-scroll routines:
- `$DA54  LD E,$01; CALL $DB06`   in `$DA23` (scroll left)
- `$DA93  LD E,$FF; CALL $DB06`   in `$DA62` (scroll right)

So `($E583)` is the WORLD-SCROLL CURSOR — increments by 1 when
the player scrolls right (ship moves right through the level),
decrements by 1 when scrolling left.

The collision check at `$DD55` builds the player's world position
as `($E583) + $0F` (= scroll cursor + 15-byte offset for the
ship's screen position).  Entities at `$E597` are compared against
this player world position.

## RAM ($F2E2–$F2E7) — per-level entity count

6 bytes, one per level. Decoded from
`build/post-game.bin`: `06 0A 09 0D 12 19` — 6, 10, 9, 13, 18, 25
entities for levels 0..5. Read by `$F1BC` and stored in `($F1BB)`
as the active count.

## RAM ($F594–$F59F) — per-level entity-list pointer

12 bytes (6 × 2). Decoded: `$F2E8, $F2EB, $F33B, $F383, $F3EB,
$F47B`. The first two pointers are only 3 bytes apart, so the
entity records are NOT a uniform 8-byte stride. Variable-length
or tagged format, format TBD. Read by `$F1BC` and stored in
`($F1B9)` as the active entity-list base.

## Per-level entity records — screen position formula

Each 8-byte record (`$F2EB+` for level 1, etc.) encodes its
entity's screen position as:

```
effective_screen_address = TopAddr + (record.Y - ($E583))
```

(Port of `$F278 ADD HL,BC`.)

**Verified**: every TopAddr stored in the records has `x_byte=0`
(scanline-start), so the record's "Y" byte (+1) is actually a
**bitmap-byte offset added to TopAddr** — which, due to
Spectrum's interleaved addressing, can shift both X (within
the char-row) and char-row (when byte-x overflows past 31).

The `CP $1F; RET NC` at `$F223` skips drawing if `(record.Y -
$E583) >= 31` — so entities further than 31 bytes' offset from
their TopAddr scanline-start are not drawn this frame.

See [`disasm/entities.md`](disasm/entities.md) for the worked
example (level 1 record 0: type=$02 y=$11 topAddr=$48A0 →
effective $48B1 → pixel (136, 104)).

## RAM ($E463) — hit accumulator

Initialized to `$FF`.  Each damage hit calls `$DDC4` which SUBs
`$40` from this byte.  Only on underflow does `$E464` (visible
shield) DEC by 1 — giving the player ~4 hits per visible bar
notch.  Verified at f100: `$E463 = $FF` (no hits yet).

## RAM ($E464) — player shield, 0..$5F

The shield bar's source value.  Range 0..`$5F` (0..95) — capped at
`$5F` by `$E0BE` (the bar paint routine has `CP $60; RET Z`).  When
DECremented to 0 in `$DDC4`, `$E464` is floored at 1 and the death
routine fires (`JP $DBC8`).  Verified at f100: `$E464 = $1E` (mid
bar-fill animation).  At f300: `$E464 = $5F` (full bar).

## RAM ($E465) — fuel accumulator

Initialized to `$FF`.  Each frame the L key is held, `$D8D8..$D8EC`
SUBs `$20`.  On underflow, `$E466` (fuel) DECs by 1 — so 8 frames
of held L = 1 fuel notch.

## RAM ($E466) — player fuel, 0..$5F

Same range as shield.  Decremented by the `$D8D8..$D8EC` chain:
the L-key (horizontal thrust) drains the accumulator at `$E465`
by `$20` per frame; on underflow, `$E466` DECs by 1.  At full
tank (`$5F`) it takes ~760 frames (12.7 s at 60 fps) of held
horizontal input to fully deplete.

The HUD bar painter at `$E0B4` reads `$E466` and passes it to
`$E0BE` (the bar drawer).

A fuel-low warning fires at `$D879` when fuel drops below `$20`
— calls `$F8B4` (probably a beeper alert).  Likewise shield-low
warning calls `$F8D8`.

## RAM ($E588) — lives counter

The lives count, **including the currently-active ship**.  Starts
at `$05` (5 lives) per the cassette's initial state.  `$D8A8`
reads this after each death animation: `LD A,($E588); DEC A; JR
NZ,$D8B8` — if DEC produces 0 (i.e. `$E588 == 1`) it calls
`$F974` (game-over).  The HUD draws `lives - 1` ship icons in the
top-right (positions cols 21, 24, 27, 30), so at game start there
are 4 icons + 1 active = 5 lives total.

**Lives DEC site**: `$F6D9..$F6E0`.  Fall-through path from
`$F6BE` (called after the death-anim restoration).  Reads
`$E588`, DEC A, `JP Z,$F73B` (game over if zero), else stores
back to `$E588` and re-runs the level setup at `$F6E3..$F6EF`.

`$D8A8` is the post-explosion stack-restore-only path — it
reads `$E588` to detect "is this last life" but does NOT DEC.
The actual DEC happens later in the call chain.

Other write sites: `$F69E` (`$F69C` sets Lives=5 at fresh
game start), `$D8A0` (`LD (HL),$01` — sets Lives=1, likely a
cheat-code or panic state).

## Code ($DDC4) — hit sound + shield decrement

```
DDC4..DDCB  speaker low/silent/high      ; "TICK" sound
DDCD..DDD5  $E463 -= $40                  ; drain accumulator
            RET NC                        ; no underflow → just a "tick"
DDD6..DDDA  $E464 -= 1                    ; shield DEC
            RET NZ                        ; still alive
DDDE..DDE2  $E464 = 1                     ; floor at 1
            JP $DBC8                      ; death
```

Called on every damage-causing collision.  See
[`disasm/death.md`](disasm/death.md) for the full annotated trace.

## Code ($DBC8) — death/explosion animation

Four passes of `$DBDA` (8-particle attribute-flash anim) bracketing
a screen-dim sound `$DC43`, then `JP $D8A8` (lives check + stack
restore).  Each `$DBDA` pass:

1. Copy 32-byte particle seed table from `$E861` to live scratch
   at `$E881`.
2. Override each particle's Y with `$BF - altitude` (`$E584`).
3. 64 iterations of: paint cell attribute with `$E57B` (level
   colour), step (`x += dx`, `y += dy`), paint white `$07`.

The effect runs entirely in the attribute file — the bitmap is
untouched.  See [`disasm/death.md`](disasm/death.md).

## Code ($DC43) — descending whine + screen wipe

Calls `$DC4E` 8 times, each iteration doing `SRL (HL)` over the
entire bitmap `$4000..$5000`.  Net effect: the screen fades to
black one bit-shift at a time, accompanied by a busy-wait
descending-pitch beep.

## RAM ($E861) — death-particle seed table

32 bytes, 8 records × 4 bytes (x, y, dx, dy).  Copied wholesale
to `$E881` by `$DBDA` then the Y bytes are overridden.  The X
bytes seed the burst pattern (8 outward directions).

## RAM ($E881) — death-particle live scratch

8 × 4-byte particle records, overwritten from the `$E861` seed
table at the start of each `$DBDA` pass.

## Code ($D8A8) — post-death restore + lives check

```
D8A8..D8AD  RES bits 0,1 of $5C91          ; clear keyboard state
D8AF..D8B3  test $E588 - 1 == 0
D8B5        CALL $F974                     ; game over (if 0)
D8B8..D8BF  restore SP from $E457 and RET  ; unwind to game-loop
```

## Code ($E104) — sprite-composition walker (level paint?)

Reads `HL = ($E579) + $1000`, then walks 4096 bytes *backwards*
to `($E579)`. For each non-zero byte calls `$E127` which uses
`$E1E4` to compute a screen address from a `(B, C)` pair and
ORs the byte into the bitmap. The inner loop counter C runs
through all 256 values; the outer loop B runs 16 times. The
screen row used for drawing is `$20 - (B<<1)` so it covers
rows 0, 2, 4, ..., 30 — i.e. an entire screen vertically.

Either this is the per-level scenery painter or something
adjacent to it; the source data at `$60F4..$70F3` (level 1) is
empty in our mid-gameplay RAM dump, suggesting the buffer is
built up by another path at level-load time. TBD.
