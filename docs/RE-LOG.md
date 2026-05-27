# Reverse Engineering Log

A running notebook of what we discover as we tear the game apart. New
entries are appended; older entries are not edited (except for fixing
factual errors, which we mark with a struck-through note).

The log is meant to be readable end-to-end by anyone who wants to
understand *how* we got to the C# port — not just the conclusions.

---

## 1. The target

- **Game**: Subterranean Stryker
- **Year**: 1985
- **Publisher**: Insight Software (UK)
- **Platform**: ZX Spectrum 48 K (single release)
- **Original price**: £6.95
- **Sources**:
  - [Spectrum Computing entry 4983](https://spectrumcomputing.co.uk/entry/4983/ZX-Spectrum/Subterranean_Stryker)
  - [World of Spectrum archive](https://worldofspectrum.org/archive/software/games/subterranean-stryker-insight-software)

Spectrum Computing lists only one release for the Spectrum (the 48 K
version) plus an unofficial Italian magazine reprint ("Missione nel
profondo", Load 'n' Run, Dec 1985) which we are ignoring. There is no
128 K variant, so "the more advanced Spectrum version" is the 48 K
original.

**Confirmed:** Everygamegoing tags the cassette as compatible with both
Spectrum 48K and 128K, but the game's own feature box reads
"48K Spectrum Only" — the 128K tag is just compatibility (the tape
runs in 48K mode on a 128K). There is no separate 128K-exclusive
edition with enhanced audio/graphics. The Spectrum version we are
targeting is the one canonical 1985 release.

**Credits** (from Everygamegoing): code by Mark Wilson and Peter
Gough, music by Tim and Mike Follin. The Follin brothers' involvement
means the AY/beeper tune is a genuine bit of Spectrum music history,
and worth preserving carefully when we get to the audio extraction
step.

## 2. Files in `original/`

```
original/
├── tape/SubterraneanStryker.tzx        49 469 bytes — full tape image
└── dumps/
    ├── SUBSTRYK.Z80                     36 673 bytes — .z80 v1 snapshot
    └── SCRSHOT/SUBSTRYK.SCR              6 912 bytes — loading screen
```

The `.tzx` is the canonical preservation artefact (turbo-loaded tape).
The `.z80` is a v1-format compressed snapshot of the game already
loaded into RAM, taken at some point during the title screen. The
`.scr` is the raw 6 912-byte Spectrum display (256×192 mono + 32×24
attributes).

## 3. Tools we'll need

We are deliberately **not** pulling third-party Spectrum tooling. Every
piece of analysis machinery here will be hand-written so that the open
source release has clean provenance. The tool set will grow as the
project proceeds; current plan:

1. **`unz80`** — decompress a v1 `.z80` snapshot into a flat 48 K
   memory image (one byte per Spectrum RAM byte, addresses 0x4000 –
   0xFFFF mapped to file offsets 0 – 0xBFFF).
2. **`zdisasm`** — a small Z80 disassembler. Just enough to follow
   game code; doesn't need every undocumented opcode at first.
3. **`scrview`** — render `.scr` files (and chunks of memory) as PNGs
   so we can visually identify graphics in the memory image.
4. **`tapread`** — walk a `.tzx` file, list the blocks, and dump the
   data inside each one (this is where the original loader and any
   in-game data live, before depacking).
5. **`sprrip`** / **`maprip`** — once we know the data layouts, rip
   the sprites and maps out.

These all end up under [`tools/`](../tools/). They are written in
small self-contained C# console projects (single .NET solution) so the
whole pipeline is one toolchain.

The runtime port + the asset/map editor live in [`src/`](../src/),
share an `Assets` library with the tools, and target `net8.0`.

## 4. Method

The general loop:

1. Extract the data we have (snapshot, tape blocks).
2. Pull out the obvious assets first (the loading screen is already
   a known format).
3. Disassemble the resident game code; identify the main loop and the
   input/draw/update phases.
4. Walk back from "draw sprite" / "draw map" to find sprite/map
   tables, document their formats, and rip them.
5. Re-implement the gameplay logic in C# against the ripped data.
6. Wire a viewer/editor on top of the same `Assets` library.

Every interesting offset, table, or routine address goes into the
[`docs/MEMORY-MAP.md`](MEMORY-MAP.md) file as we identify it.

## 5. First glance at the snapshot

`subterra snapshot-info` (see [`src/Subterra.Tools`](../src/Subterra.Tools))
reports the following for `SUBSTRYK.Z80`:

```
Kind:           V2
PC: 1F3D    SP: 5E82    IM: 0    Border: 7    IFF1/2: 1/1
AF: 0050    BC: 0000    DE: 395D    HL: 5D34
AF':FF81    BC':0147    DE':369B    HL':0000
IX: 0000    IY: 5C3A    I:  3F      R: 08
```

Observations:

* PC = `$1F3D` is **inside the Spectrum 48K ROM** (the ROM occupies
  `$0000-$3FFF` and is *not* present in the snapshot). The snapshot was
  taken while the CPU was executing a ROM routine — most likely the
  keyboard-scan loop at the title screen, since:
  * `IY = $5C3A` and `I = $3F` are the canonical BASIC/system values
    set up by the ROM,
  * `SP = $5E82` is well above the usual stack base,
  * `IM 0` plus `IFF1/IFF2 = 1` is what the ROM uses for normal
    interrupts.
* Border colour is `7` (white), border colour is set by the ROM at
  startup.
* The byte histogram per 4 KB block shows most of `$4000-$DFFF` is
  zero-padded but the upper region `$E000-$FFFF` is dense — that's a
  good first guess for where the game's resident code/data block
  lives.

We rendered both `SCRSHOT/SUBSTRYK.SCR` and the snapshot's screen
memory (`$4000-$5AFF`). They are identical — confirming that the
snapshot was captured at the title screen rather than during
gameplay. To get a *gameplay* reference frame, we'll need to run the
game ourselves: that's why building a minimal Z80 + Spectrum ULA
emulator is the next big step.

Renders live in [`renders/`](../renders/) with a timestamp suffix so
we keep a complete visual history; never overwrite.

## 6. First renders confirm the screen pipeline

Two PNG artefacts come out of the early pipeline and live in
[`renders/`](../renders/):

<p align="center">
  <img src="../renders/scr-substryk_20260527-224113.png" width="320" alt="Loading screen with the sub, explosion, INSIGHT logo and the SUBTERRANEAN STRYKER title"/>
</p>

* The original `.scr` loading screen — the iconic sub & explosion
  with the "INSIGHT" credit and "SUBTERRANEAN STRYKER" title.
* The first 6 912 bytes of the snapshot's RAM, decoded with the same
  Spectrum-bitmap-address math — pixel-identical to the `.scr`,
  confirming both our snapshot loader and our screen decoder.

The first attempt also caught a classic Spectrum bug: our
`BitmapAddress(x, y)` had the position of `y`'s low 3 bits wrong,
producing a render where the title was *vertically replicated*. The
fix — put `y & 0x07` into address bits 10-8 and `y & 0x38 >> 3` into
address bits 7-5 — produced a correct render on the next try. The
buggy render is preserved in `renders/` as a reminder; it's the same
mistake every Spectrum emulator author has to make at least once.

## 7. Following the BASIC loader

PROG (the BASIC program start, system variable at `$5C53`) points to
`$5CCB`. Reading the tokens out of line 10:

```
10  BORDER NOT PI
   :POKE VAL "23693", VAL "71"
   :CLEAR VAL "24199"
   :LOAD "" CODE
   :RANDOMIZE USR VAL "28350"
   :POKE VAL "23739", VAL "111"
   :LOAD "" CODE
   :POKE VAL "23739", VAL "244"
   :PAUSE NOT PI
   :RANDOMIZE USR VAL "62973"
```

A perfectly typical mid-80s Spectrum loader. We learn:

* `CLEAR 24199` sets RAMTOP to `$5E87`, so the game code lives at
  addresses ≥ `$5E88` (= 24200).
* The first `LOAD "" CODE` is the loading screen (loads at `$4000`).
* `RANDOMIZE USR 28350` (`$6EBE`) — a pre-game routine, probably for
  the credits/title decoration.
* The second `LOAD "" CODE` is the main game binary.
* `PAUSE NOT PI` is `PAUSE 0`, i.e. "wait for a key", which is why the
  snapshot's PC sits at `$1F3D` — that's the Spectrum ROM's PAUSE
  routine.
* `RANDOMIZE USR 62973` (`$F5FD`) — the real game entry point.

Disassembling at `$F5FD`:

```
F5FD  21 57 FF    LD HL,$FF57
F600  CB 8E       RES 1,(HL)
F602  CD B2 E3    CALL $E3B2          ; init helper (used twice)
F605  3E 02       LD A,$02
F607  CD 01 16    CALL $1601          ; ROM CHAN-OPEN (upper screen)
F60A  21 2B E6    LD HL,$E62B
F60D  22 7B 5C    LD ($5C7B),HL       ; STKBOT
F610  CD B2 E3    CALL $E3B2
F613  AF          XOR A
F614  D3 FE       OUT ($FE),A         ; border black
F616  32 83 EE    LD ($EE83),A        ; clear a game flag
F619  3E 02       LD A,$02
F61B  CD 01 16    CALL $1601
F61E  21 2B E6    LD HL,$E62B
F621  22 7B 5C    LD ($5C7B),HL
F624  21 2B F8    LD HL,$F82B
F627  7E          LD A,(HL)
F628  FE FF       CP $FF
F62A  28 04       JR Z,$F630
F62C  D7          RST $10             ; ROM print char
F62D  23          INC HL
F62E  18 F7       JR $F627
F630  06 60       LD B,$60            ; 96 reps
F632  3E 96       LD A,$96            ; UDG #6 (block)
F634  D7          RST $10             ; print it
F635  10 FB       DJNZ $F632
F637  3E 7F       LD A,$7F
F639  DB FE       IN A,($FE)          ; read keyboard row 7F
F63B  E6 0C       AND $0C
F63D  C8          RET Z               ; (etc.)
```

The string at `$F82B` is:

```
AT 8,8 INK 0 PAPER 0 "BY  MIKE FOLLIN"
AT 2,31 OVER 0  <UDG-codes…> FF
```

So we now have hard names:

| Address  | Meaning                                        |
| -------- | ---------------------------------------------- |
| `$5CCB`  | BASIC loader, line 10                          |
| `$6EBE`  | Pre-game USR routine                           |
| `$E3B2`  | Init helper called twice from `$F5FD`          |
| `$F5FD`  | Main game entry (the second `RANDOMIZE USR`)   |
| `$F82B`  | "BY MIKE FOLLIN" title-screen string + UDGs    |

These get tracked in [`docs/MEMORY-MAP.md`](MEMORY-MAP.md) so we can
look them up while porting.

The game also calls ROM routines directly (CHAN-OPEN at `$1601`,
`RST $10` for print-char). Any emulator we build needs the 48 K ROM
loaded, or we have to intercept those calls and re-implement the
handful that the game uses. The latter is more in the spirit of "do
it yourself", but for now we'll bring in the ROM (it has been freely
redistributable for non-commercial use since Amstrad's 1999
permission grant).

## 8. Tooling so far

`src/Subterra.Spectrum` is a small dependency-free library with:

* `Z80SnapshotReader` — handles v1, v2, v3 .z80 files (RLE decode,
  page mapping); only 48 K mode is supported for now.
* `SpectrumScreen` — Spectrum bitmap-address math + 16-colour palette
  + `.scr → RGBA` decoder.
* `PngWriter` (+ our own `Crc32`) — minimal RGBA PNG encoder so we
  never depend on a third-party imaging library.
* `RenderTarget` — every render in the project goes through this so
  it lands in `renders/` with a timestamp.

`src/Subterra.Tools` (binary name: `subterra`) exposes commands:

```
subterra render-scr <file.scr>
subterra render-snapshot <file.z80>
subterra unz80 <file.z80> <out.bin>
subterra snapshot-info <file.z80>
```

We caught one bug along the way: our first `BitmapAddress(x, y)`
mixed up the position of `y`'s low 3 bits in the bitmap offset,
producing a screen where the title was vertically repeated. The
corrected formula (see [`SpectrumScreen.cs`](../src/Subterra.Spectrum/SpectrumScreen.cs))
puts `y`'s low 3 bits into address bits 10–8 and the next 3 bits of
`y` into address bits 7–5; that produced a correct render on the
next try.

## 9. Z80 emulator

<p align="center">
  <img src="../renders/emu-substryk-f00030_20260527-225654.png" width="240" alt="Game title screen showing BY MIKE FOLLIN and the four control options"/>
  <img src="../renders/emu-substryk-seq-f00280_20260527-225827.png" width="240" alt="In-game scene with HUD, tree silhouette, terrain"/>
</p>

*Left: the game's own title screen, reached after we let the
snapshot run for ~30 frames with SPACE pressed.  Right: actual
gameplay — fully drawn HUD with DEPTH / SCORE / SHIELD / FUEL / RESCUED,
green terrain, and the player ship in the centre — after we held "1"
to choose the keyboard control option. Both are screenshots from
**our own emulator**, no original Spectrum involved.*

`src/Subterra.Spectrum/Z80/Z80Cpu.cs` is a hand-written, dependency-free
Z80 emulator covering the documented instruction set in full:

* All 256 main opcodes, with full flag-correct ALU including the F3
  and F5 "undocumented" bits that follow the result.
* CB prefix (rotates / shifts / `BIT` / `RES` / `SET`), with all 8
  rotate/shift modes including `SLL` (undocumented but commonly used).
* ED prefix (`SBC HL,rr` / `ADC HL,rr`, block ops `LDI`/`LDD`/`LDIR`/
  `LDDR`/`CPI`/`CPD`/`CPIR`/`CPDR`, `IN`/`OUT (C)`, `RRD`/`RLD`,
  `RETI`/`RETN`, `IM 0/1/2`, register-I/R loads).
* DD/FD prefix (IX/IY indexed forms) plus the DD-CB / FD-CB four-byte
  indexed bit ops with displacement.
* Interrupt acceptance for IM 0 / 1 / 2, with the correct PC-advance
  when waking from `HALT` (this was the one tricky bug we caught: the
  CPU stalls PC at the HALT opcode, but on interrupt acceptance the
  return address pushed must be the address *after* the HALT).

`src/Subterra.Spectrum/Spectrum48.cs` wraps the CPU into a 48 K
machine — 16 K ROM at `$0000-$3FFF`, 48 K RAM, ULA port `$FE` (border
write / keyboard read). `RunFrame()` fires one maskable interrupt
then ticks 69 888 T-states (the canonical 50 Hz frame).

With this plus a snapshot loader, `subterra run-emu` boots
`SUBSTRYK.Z80`, runs N frames with a scripted key schedule, and
renders the final screen to `renders/`. After ~30 frames with SPACE
pressed (to break out of `PAUSE 0`) the game's *own* title screen
appears with "BY MIKE FOLLIN" and the four-option control menu;
after another 100 frames with "1" held, the gameplay screen with the
HUD, terrain and player sprite materialises. That's our smoke test
for the entire stack.

## 10. The "ship doesn't scroll" mystery

The first time we got the game running in our emulator, the player
sprite would move up/down but the *world* never scrolled. This sent
us through several false hypotheses (HALT bug? interrupt mode mismatch?
issue 2 vs issue 3 keyboard? `FRAMES` counter never updating?). The
actual answer turned out to be a *game-mechanics* misunderstanding:

The main loop at `$D7FB` calls a pre-step routine `$F868` first, and
`$F868` gates everything on this condition:

```
F86D  3A 84 E5    LD A,($E584)   ; player altitude
F870  FE 75       CP $75         ; = 117
F872  D8          RET C          ; if altitude < 117, do nothing
```

`$E584` is the player altitude (0..120). It starts at 0 each new
section. Holding DOWN (key A, row 1) increments it by 1 per frame
via the routine at `$D95D`. Once it reaches 117 the world scrolls
one page, `$E584` is reset to 0, and the player keeps diving. The
name "Subterranean Stryker" turns out to be literal: the game
scrolls *down* into the earth, not sideways like we'd assumed.

This was diagnosed entirely by tooling — no real Spectrum needed:

* `subterra find-bytes` to grep RAM for the opcode of every keyboard
  read (`DB FE`) and every store to `($E584)` (`32 84 E5`).
* `subterra disasm` to read the main loop and the pre-step gate.
* `subterra emu-peek` to watch `($E584)` advance frame-by-frame
  while DOWN was held.
* `subterra run-emu -stride=N` to drop a flip-book of renders
  showing the world transition from "static surface scene" to
  "scrolling underground caverns" once the threshold was crossed.

## 11. Tooling so far (current)

```
subterra render-scr     PNG of a 6 912-byte .scr file
subterra render-snapshot PNG of a .z80 snapshot's screen memory
subterra unz80          decompress .z80 → flat 48 K RAM image
subterra snapshot-info  registers + per-block byte histogram
subterra disasm         Z80 disassembly from a snapshot
subterra stack-walk     dump top of stack (return-addr trace)
subterra hex            classic hex+ASCII dump
subterra find-bytes     opcode/data pattern search with ?? wildcards
subterra run-emu        boot a snapshot, run frames, render + dump RAM
subterra emu-peek       boot, run, then print named memory addresses
subterra sprite-scan    bulk render of candidate sprite cells across RAM
```

The two GUI apps:

* `dotnet run --project src/Subterra.Game` — Avalonia window of the
  live emulator, playable on macOS / Linux / Windows.
* `dotnet run --project src/Subterra.Editor` — Avalonia sprite
  scanner: type an address + cell size, see decoded cells, hover
  for raw bytes, "Save PNG" dumps the sheet to `renders/`.

## 12. Two visual quirks that look like bugs but aren't

While playing, the ship visibly **flickers** and the **colour of
nearby enemies changes when the ship moves past them**. Both turn
out to be authentic Spectrum behaviour — our emulator is
reproducing them faithfully.

**Flicker** comes from the way Spectrum games animate. The 48 K
machine has no hardware sprites, only a single 6 144-byte bitmap +
768-byte attribute buffer. To move any object you have to *erase
the old position, then draw at the new position* — both operations
mutate the live framebuffer. If the game's main loop isn't strictly
synchronised to the 50 Hz frame interrupt, the raster beam catches
the framebuffer in an "erased" state for one or more scanlines and
the sprite is missing on that frame. Subterranean Stryker's main
loop (`$D7FB`–`$D826`) calls a dozen update routines in sequence
without an obvious `HALT` between draw and erase, so this happens
constantly. Most Spectrum action games have the same look.

**Colour clash** comes from the Spectrum's attribute layout. The
bitmap is 1 bit per pixel — only ink-vs-paper, no colour. Colour is
stored separately in the 32×24 attribute grid, one byte per 8×8
character cell. A sprite that moves across cells takes its colour
from whichever cell it currently sits in; any *other* sprite or
piece of terrain inside that cell shares the same colour, because
there's only one attribute byte for the whole cell. So as the
player ship sweeps past an enemy, the enemy briefly takes on the
ship's ink colour — the famous "colour clash" effect. Every
Spectrum game has it; usually the artists work *with* the grid by
keeping sprites colour-uniform inside each 8×8.

Neither is something we'd want our future C# port to "fix" — they
are part of what makes the game look like itself.

## 13. First extracted asset

<p align="center">
  <img src="../renders/scan-%24E62B-8x8_20260528-001405.png" width="416" alt="21 8x8 UDG cave-terrain tiles extracted from address E62B"/>
</p>

The game keeps its in-game UDGs at `$E62B`. `MainEntry` does
`LD HL,$E62B; LD ($5C7B),HL` early on, pointing the Spectrum
system's UDG-base sysvar at the game's own glyph table (21 cells of
8 × 8 bytes = 168 bytes). Decoded with `subterra sprite-scan
build/post-game.bin E62B E700 8x8`, they turn out to be a small
cave-terrain dictionary: smooth-cloud / dust-cloud variants,
checkerboard cave-wall tiles, and ground variants. The PNG is in
[`renders/scan-$E62B-8x8…`](../renders/). This confirms the
asset-extraction pipeline end-to-end: emulator → run-N-frames →
post-game RAM dump → SpriteSheet decoder → timestamped PNG in
`renders/`.

## 14. Unravelling the sprite system backwards from the screen writes

<p align="center">
  <img src="../renders/scan-%24B0F4-8x8_20260527-234538.png" width="580" alt="Master 8x8 sprite tile bank at B0F4 — about 390 tiles including cave walls, trees, buildings, humanoid figures, projectiles, the HUD font"/>
</p>

*The master 8 × 8 sprite tile bank at <code>$B0F4</code> — every
in-game graphic the XOR-draw routine ever displays comes from a
slice of these ~390 tiles. You can spot the cave walls and drips at
the top, then trees and surface buildings, then the "RESCUED"
humanoid figures, then projectiles and HUD glyphs (F, U, E, L and
the digits are all in there somewhere).*

The UDGs at `$E62B` are only used for a small set of static
terrain glyphs; the *real* sprite content (the player sub, every
enemy, the explosions, the HUD font, …) had to be found by tracing
how the game actually draws pixels onto the screen. We grew the
toolkit in three small steps:

1. **`subterra find-bytes`** for opcode-pattern search. Used to
   locate every `LDIR` / `LDDR` block-move in game code (≈ 10 of
   them), every `IN A,($FE)` keyboard read, and so on.
2. **`subterra tile-trace`** for breakpoint-style inspection.
   Steps through one frame, captures the program state at a
   chosen address, and produces a per-256-byte PC histogram of
   where the CPU spent its time during the frame.
3. **`subterra scrwrite-trace`** for full memory observation.
   Subscribes to a new `Spectrum48.MemoryWritten` event and logs
   every write into bitmap memory (`$4000-$57FF`) and attribute
   memory (`$5800-$5AFF`) during one frame, with the PC, the
   target address, the byte value, and (post-decoded) the screen
   pixel coordinates. Plus a single-step capture of the A register
   right at the XOR-draw entry, so the actual *source* sprite byte
   is visible.

The big find from following the trail was that **the game
maintains three completely separate draw paths in parallel**, and
that combination — not any one of them — explains everything we see
on screen.

| Draw path | Routine | Tile source | Used for |
| --- | --- | --- | --- |
| Block-copy from indexed bank | `$DAF2` (via `($E579) + index*8 + $B0F4`) | `$B0F4` master tile bank | Scenery, level tiles (overwrite, no flicker) |
| Block-copy from ROM font | `$E03D`-`$E045` (via `($5C36) → $3C00`) | Spectrum ROM character set | HUD labels: DEPTH / SCORE / SHIELD / FUEL / RESCUED |
| Per-byte XOR draw | `$E1DE` (driven by 2×2 wrapper at `$E1C1`) | A is supplied by the caller per byte | Every moving sprite: player, enemies, projectiles |

The third path is the source of the visible flicker. XOR is
double-duty (`a ⊕ a == 0`): the *same* call both draws and erases.
So once per frame, every moving sprite is XOR'd at its old position
to remove it, then XOR'd at its new position to redraw — with a
brief window in between where it isn't on the screen at all. Most
1980s Spectrum games do exactly this; the colour clash on adjacent
enemies is just the standard 8 × 8 attribute-cell quirk on top.

`scrwrite-trace` proved the model at runtime: 158 bitmap writes
during one frame of normal gameplay, **all 158 from the same
`LD (HL),A` at `$E1E2`**. At a different gameplay frame we see a
mix — `$E1E3` (XOR draws), `$E041` (HUD font copy), `$DD26..$DD2E`
(playfield-edge column), `$F2CF` (still to be analysed) — which
matches a model where the HUD and edges are written once per frame
solidly, and the moving objects are XOR'd over the top.

The biggest asset extraction so far is the **`$B0F4` master tile
bank** (≈ 390 distinct 8 × 8 cells, 3 KB stored flat in
`assets/extracted/tiles-b0f4.bin`). Recognisable in the contact
sheet at `renders/scan-$B0F4-8x8…`:

* Rows 1-3: cave walls, drips, stalactites.
* Row 4: trees, mountains, surface decoration.
* Row 5: surface buildings.
* Row 6: humanoid figures — the "RESCUED" people.
* Row 7-8: equipment, vehicles, power-ups.
* Row 9: projectiles and sparks.
* Hidden in there too: the letters F, U, E, L and the digits used
  by the HUD.

What's *not* yet pinned down: the per-object **sprite composition
tables** — i.e. for each enemy type, the list of tile indices and
their (row, column) offsets that together form the 16 × 16 or
24 × 16 picture you see. Every byte the XOR-draw routine processes
comes through `A` in `$E1E1`, so capturing those values across a
frame and matching them back to byte sequences in the tile bank
will give us the per-sprite tile lists. That's the next obvious
follow-up.

## 15. Documentation hygiene

Two living documents anchor the project:

* [`docs/RE-LOG.md`](RE-LOG.md) (this file) — narrative, ordered
  story of *how* we found each thing. New sections at the bottom.
* [`docs/MEMORY-MAP.md`](MEMORY-MAP.md) — the lookup table of every
  named address we've identified, organised by RAM region. Updated
  in lockstep every time we name a new routine or table.

Where they overlap, the memory map is authoritative for the
*what* (address, name, brief description) and the log is
authoritative for the *why* (how we found it, what dead-ends we
hit). Commits where we identify something new should touch both.
