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
