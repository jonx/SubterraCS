# Reverse Engineering Log

A running notebook of what we discover as we tear the game apart. New
entries are appended; older entries are not edited (except for fixing
factual errors, which we mark with a struck-through note).

The log is meant to be readable end-to-end by anyone who wants to
understand *how* we got to the C# port — not just the conclusions.

---

## 0. Standing on shoulders

Before any of the technical notes below: this log is only possible
because of forty years of accumulated work by other people. The
Zilog Z80 User Manual provides every flag and opcode our emulator
implements; the Spectrum's hardware quirks (interleaved bitmap,
attribute clash, ULA port `$FE`, the 50 Hz interrupt) are
collectively documented by the World of Spectrum, Spectrum Computing
and Sinclair Wiki communities; the `.z80` snapshot format is Gerton
Lunter's; the binaries under `original/` are mirrored by the
Spectrum preservation community; the 48 K ROM image is bundled by
permission of Amstrad. And the game itself was written by Mark
Wilson, Peter Gough, Tim Follin and Mike Follin in 1985. The
[`README`](../README.md#acknowledgements) names them properly.

The convention in this log going forward: when a section borrows
heavily from a specific outside source we cite it inline.

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

## 16. Cracking the entity system

Picking up from §14: we knew every moving sprite was XOR-drawn one
byte at a time at `$E1E2`, but we hadn't found *where the source
bytes came from*. The trace at frame 300 had given us single-bit
patterns (`$80, $40, $20, …`) which made obvious sense for bullets
but not for the player ship or the enemies (which clearly draw
more pixels than one bit per byte).

So there had to be a *second* draw path used for bigger sprites.
The PC histogram at frame 280 (a less bullet-heavy gameplay state)
spelled it out:

```
PC=$E041     72 writes   ← overwrite font copy (HUD)
PC=$F2CF     64 writes   ← !! unknown path !!
PC=$DC87     32 writes
PC=$E1E3     22 writes   ← XOR draw (the bullets)
PC=$DD26/27/2E  14 each  ← playfield edge
...
```

`$F2CF` had nothing to do with `$E1DE`. Disassembling around it
showed a separate primitive:

```
F2BC  LD B,$08
F2BE  PUSH HL
F2BF  LD A,(HL)            ; read screen
F2C0  JP $F2CD             ; (always)
F2CD  LD A,(DE)            ; load sprite byte
F2CE  LD (HL),A            ; overwrite — no XOR
F2CF  INC DE
F2D0  INC H                ; next scanline (within band)
F2D1  DJNZ $F2BF           ; 8 rows
F2D3  POP HL
F2D4  …                    ; compute attribute address from H
F2DD  LD A,(IY+$03); LD (HL),A
F2E1  RET
```

So `$F2BC` is an 8-row × 1-byte sprite blit (with attribute byte
from `(IY+$03)`). Then the wrapper at `$F26D` calls it four times
— at four screen addresses pulled from `(IX+3..4)` and `(IX+5..6)`
— to draw a full **16 × 16-pixel quadrant sprite**.

And the *sprite source* address comes from this beautiful little
indirection in `$F228` onward:

```
F228  LD L,(IX+$02)        ; current animation frame
F22B  LD H,$00
F22D-F231  ADD HL,HL × 5    ; HL = frame * 32
F232  LD E,(IY+$00)
F235  LD D,(IY+$01)         ; DE = sprite-bank base for this *type*
F238  ADD HL,DE              ; HL = bank_base + frame*32
```

Going back one step: where do IX and IY come from? `IY` is set
inside the per-entity setup at `$F1F0`:

```
F1F0  LD IY,$F5A0           ; entity-type table base
F1F4  LD E,(IX+$00)          ; type id
F1F9  SLA E; SLA E            ; ×4 (4 bytes per type)
F1FD  ADD IY,DE               ; IY = $F5A0 + type*4
```

— and `IX` is walked across an *entity instance list* in the
dispatcher at `$F1A5` (which is one of the calls in the main game
loop at `$D7FB`). Each instance is 8 bytes; each type is 4 bytes.

A `subterra hex` dump of `$F5A0` revealed the type table content:

```
F5A0  F4 B8 10 43 F4 BA 10 42 F4 BC 10 43 F4 BE 10 44 …
```

— interpreted: type 0 → bank `$B8F4`, 16 frames, attr `$43`
(bright magenta on black). 16-byte stride between types.  ✓

To *see* the result we wrote `Subterra.Assets.QuadrantSpriteRenderer`
(the column-major-quadrant decoder) and `subterra entity-bank`
(the CLI tool that walks the type table and dumps every bank as a
PNG). The first PNG was unmistakable:

* **Type 0** = the **player submarine**, magenta, with the drill
  on top, in 16 animation frames: side view, descent, drilling,
  explosion.
* **Type 1** = red molten lava droplets, falling and rising frames.
* **Type 2** = magenta cave-roof stalactites.
* **Type 3** = green falling rocks / debris.
* …and so on, up to ~22 entity types.

So the sprite system is now fully decoded:

```
   ($F1B9), ($F1BB)            ← active entity-list pointer + count
        │
        ▼
   entity instance (IX, 8 bytes)  ── type id, y, frame, screen-addr top, screen-addr bottom
        │
        ▼  type id × 4
   entity-type table at $F5A0 (IY, 4 bytes per type)  ── sprite-bank ptr, max-frames, attr
        │
        ▼  frame × 32
   16 × 16 sprite in column-major quadrant layout
        │
        ▼  four calls to $F2BC
   pixels on screen + colour attribute
```

This was *exactly* the "unravel backwards from the screen writes"
strategy the user suggested in [§14](#14-unravelling-the-sprite-system-backwards-from-the-screen-writes)
— traced one extra layer further with `scrwrite-trace`'s PC
histogram + targeted disassembly + memory peeks.

## 17. The player Stryker has its own draw path

I got ahead of myself in §16. When the first entity-type bank
rendered as 16 frames of a magenta vertical "T-shape with debris",
I jumped to "submarine with a drill on top, mid-descent" and put
that in the README. The user pushed back: *"are you sure this is a
submarine and not just the shovels of the workers?"* — and the
moment I looked again the shapes were obviously **shovel /
pickaxe heads swung mid-air, with dirt particles around them**.
Type 0 is the rescued workers' digging animation, not the player.

Then a second nudge: *"none of those are the ship, it's the
monsters mostly or the decor with lava and bubbles and such"*.
Rendering types 1-16 confirmed it: every single one is an enemy,
hazard, or decoration. So where *is* the player?

A third nudge from the user closed the loop: *"you said earlier
that you confirmed the ship blinking was by design, so you were
probably not far from where the ship is?"*. Right — back in §12
and §14 we narrowed the *visible* flicker down to XOR drawing.
The entity system in §16 uses **overwrite** through `$F2BC` — so
those sprites *don't* flicker. The flickering thing must be a
*different* XOR drawer.

A quick `find-bytes "AE 77"` (XOR with HL ; LD (HL),A — the
core XOR-draw instruction pair) over the in-game code range
turned up another, separate XOR draw routine at `$DCF5`. Its
setup is the smoking gun:

```
DCF5  LD IX,$E8C9      ; four screen addresses for the four quadrants
DCF9  LD DE,$E8A9      ; 32-byte sprite buffer
```

Two new RAM addresses to look at. `$E8C9` decodes as four 16-bit
screen addresses — exactly the 2×2 layout for a 16-pixel-wide
sprite. `$E8A9` is the sprite buffer itself. **Drawn with XOR. So
this is the thing that flickers.**

What's in the buffer right now? `subterra hex build/post-game.bin
E8A9 32`:

```
E8A9  78 7C 7F 3F 3F FC 78 07
E8B1  00 00 0C F2 FB 1F FE F0
E8B9  00 00 00 00 00 00 00 00
E8C1  00 00 00 00 00 00 00 00
```

The first 16 bytes are an obvious sprite; the next 16 are zero.
That means the player ship is rendered as 16 × 8 pixels (top
half of a 16 × 16 buffer), and you see the actual shape rendered
via `QuadrantSpriteRenderer.RenderRgba` as a *pink horizontal
spacecraft with a nose, cockpit and tail*. That's the Stryker.

One more layer to find: where do those 16 bytes originate? `LD
DE,$E8A9` is the *destination* of a copy in setup code. Two
`find-bytes` later we land at `$E3F4`:

```
E3F4  LD HL,$E8A9; LDIR …                ; zero the buffer
E401  LD DE,$E8A9
E404  LD HL,$E63B                         ; ← source
E40A  LD A,($E586); BIT 0,A
E40F  JR NZ,$E413
E411  LD C,$10                            ; if facing-flag set, +$10
E413  ADD HL,BC; LD C,$10; LDIR           ; copy 16 bytes in
```

So the actual **player sprite bank lives at `$E63B`**, with two
directional frames of 16 bytes each:

* `$E63B` — Stryker pointing **right**
* `$E64B` — Stryker pointing **left**

Selected by `($E586) BIT 0` (the player facing direction). After
those two frames come some explosion / hit effect frames at
`$E65B+` (the `55 AA …` checkerboard patterns).

Rendered side by side, the two frames are clearly the player's
craft — mirror images, with a pink hull, a cockpit bump, and a
small tail. Saved as `renders/player-frame-right_…` and
`renders/player-frame-left_…`. And **because they're drawn with
XOR, the in-game flicker the user observed is a direct,
deterministic consequence of this code path** — exactly the
prediction §12 made.

Lessons for future-me, recorded so I don't repeat them:

1. **Don't trust the first interpretation of a sprite without
   correlating it back to the live screen.** Type 0 looked like
   "something pointy, magenta, animated frames" — that pattern
   could fit a submarine *or* a swung pickaxe. I should have
   compared its pixel shape against an actual gameplay
   screenshot before naming it.
2. **The player is a special case in most arcade ports.** The
   player tends to get its own dedicated draw routine because it
   has unique requirements (orientation flag, dedicated screen
   buffer, collision flag, etc.). When a generic entity table
   doesn't include something prominent, look for a custom path.
3. **The user is the oracle.** Two short prompts ("are you sure
   it's not the shovels?" / "you confirmed the blinking was by
   design") pointed me at exactly the right place in three steps.
   When the user nudges, take the nudge.

### Postscript: this wasn't actually hidden — I found it the hard way

To be honest with future-me: the player sprite was not buried.
Every clue I needed was already on the page two sections earlier,
in §14's own table of "the three concurrent draw paths". I
*already* had:

* `$DD26 / $DD27 / $DD2E` listed as one of the hot bitmap-write
  PCs at frame 280 — I labelled them "playfield edge" and moved
  on, but the writes there were all at **column 15 (x = 120) of
  the bitmap** — which is the player's x position on screen. Had
  I followed *any* of those three writes back to its routine, I
  would have landed on `$DCF5` directly.
* `$E1DE` already named as "the XOR draw primitive for moving
  sprites", with the explicit prediction in §12 that **the
  flicker comes from XOR drawing**. The entity-table draws in
  §16 use `$F2BC`, which is *overwrite*, not XOR — so they
  *can't* be the flickering player by construction. That alone
  ruled out the whole entity-type rabbit hole.
* A trivial `subterra find-bytes` of the byte sequence `AE 77`
  (the `XOR (HL); LD (HL),A` instruction pair) across game RAM
  would have produced a short hit list including `$DCF5`, and a
  one-glance look at the few hits would have shown the
  player-specific setup (`LD IX,$E8C9; LD DE,$E8A9`).

The straight-line approach would have been:

```
flicker observed → §12 says flicker = XOR draw
                 → grep RAM for `AE 77` (the XOR-then-store pair)
                 → find $DCF5 → read setup → $E8A9 → trace upstream → $E63B
```

Three steps. Instead I disassembled the entity-type table, wrote
a quadrant decoder, rendered 16+ banks, mis-identified the first
one, got corrected twice, and only then took the actually short
path. The user was generous about it ("okay but it wasn't that
hidden, you just found it the hard way") — which earns its own
lesson:

4. **Before diving into a new system, re-read the notes I
   already have.** §12 + §14 of this very log contained the two
   facts that would have collapsed the whole problem. RE-LOG
   isn't just a write-only journal; it's a tool, and I should
   consult it before generating new tools.

## 18. The level "design" is 192 bytes

I expected to find cave maps somewhere — tile arrays describing
the underground topology of each level. There aren't any. The
investigation went:

1. The world advances when the player altitude `$E584` hits `$75`.
   The scroll routine `$F6F2` runs.
2. `$F6F2` increments `($E587)` mod 6 — so **there are 6 levels**,
   and they cycle.
3. Per level it dereferences three tables:
   * `$E56D + level*2` → 16-bit sprite-table pointer →
     `$B0F4 / $60F4 / $70F4 / $80F4 / $90F4 / $A0F4`
   * `$E57C + level*1` → 1-byte "speed/colour modifier" →
     `07 04 03 06 02 01`
   * `$E58B + level*2` → 16-bit second pointer
4. Then it `LDIR`-copies 32 bytes from `$E69D + level*32` to
   `$E75D..$E77C`. **That's the level data.**

The 32-byte block is 8 entries × 4 bytes = (timer-lo, timer-hi,
entity-type, flags). Walked each frame by `$EF02`, which
decrements timers and spawns the indicated entity-type from the
`$F5A0` table when each reaches zero.

So the game's "world" is **procedurally composed from timed
entity spawns**. The cave walls you see aren't drawn from a map
— they're a stream of stalactites, falling rocks, and other
sprites the entity system was already drawing. The player dives
through a probability cloud.

Total level-design footprint: **6 × 32 + 6 × 2 + 6 + 6 × 2 = 222
bytes for the entire game's content**. That's the kind of design
constraint that defines a 1985 Spectrum game: 48 K total RAM, the
ROM eats 16 K, the screen eats another 7 K, the BASIC loader and
sysvars carve out more — what's left has to fit code, sprites,
*and* level design. The Follins, Wilson and Gough's solution was
"no maps, just rhythm".

This is also good news for the C# port: extracting and re-running
the level format takes essentially zero additional work beyond
what we already have.

## 19. Tim Follin on the beeper

The music data turned out to live at `$5E88` (just above CLEAR'd
RAMTOP), and the player at `$FA32`. Both were findable with a
single `subterra find-bytes "D3 FE"` — that's `OUT ($FE),A`, the
opcode that drives the Spectrum's only sound output. The
interesting matches were the ones in a tight `LD A,$10` /
`LD A,$00` ping-pong:

```
FA4A  LD A,$00; OUT ($FE),A     ; bit 4 low (speaker idle)
FA4E  LD B,D; DJNZ $FA4F         ; D = period control
FA51  LD A,$10; OUT ($FE),A     ; bit 4 high (speaker pulse)
FA55  LD B,E; DJNZ $FA56         ; E = period control
```

That's a square-wave generator. The pitch is determined by the
total `D + E` busy-wait cycles per pulse pair; the duty cycle by
their ratio. The next few instructions do `INC E; DEC D` (and the
inverse on the way back) which **slides the duty cycle while
keeping the total fixed** — i.e. the perceived pitch stays
constant but the timbre sweeps. The Follin brothers used this
trick to make a single channel sound like multiple simultaneous
notes; it's their signature on dozens of 8-bit games.

The data feeding it is a flat array of 16-bit period values
starting at `$5E88`. No score / channel / instrument metadata —
just notes. The variation comes from the player's pitch-slide
geometry, not from the data.

The routine is called from the title screen (`$F64E`, `$F65D`).
Whether it's *also* called during gameplay (or only on the title
sequence) is an open question — we never heard the music in our
Avalonia Game window because audio output isn't hooked up yet.

## 20. The main game loop, phase by phase

For documentation completeness — `$D7FB` looped 50 Hz runs the
following twelve calls every frame, plus a 13th wraparound to
the top:

```
$F868   Pre-step gate          (altitude ≥ $75 → scroll page)
$D827   Scroll counter
$D8C2   Input snapshot + dispatch
$DCAC   Player sprite stage    (copy directional frame to buffer)
$DC5D   Player attribute paint
$F1A5   Entity dispatcher      (4-frame time-slice + entity draws)
$D9C8   Horizontal-move logic  (L key, attribute strip paint)
$DCF5   *Player draw (XOR)*    ← source of the flicker
$DFAF   Effect tick            (explosions / particles aging)
$E248   Dual coordinate transform
$E8FD   Projectile + fire pass
$DE2A   Workers / rescue pass
$EF02   Spawn-schedule executor (the level heartbeat)
$E046   HUD draw
        JR $D7FB
```

Per the doc-hygiene rule, every entry above has a corresponding
short entry in [MEMORY-MAP.md](MEMORY-MAP.md). The phase names
are educated guesses based on which addresses they read and
write; nothing is by inspection of behaviour yet. They will
sharpen as we keep watching the live emulator.

## 21. A complete picture

After §1-§20, the game's architecture is essentially known. Here
it is in one diagram, source addresses inline:

```
                                  ┌──────────────────────────────────┐
                                  │  BASIC loader $5CCB              │
                                  │  CLEAR 24199 → RAMTOP = $5E87    │
                                  │  LOAD ""CODE → screen ($4000)    │
                                  │  RANDOMIZE USR $6EBE (pre-game)  │
                                  │  PAUSE 0; RANDOMIZE USR $F5FD    │
                                  └──────────────┬───────────────────┘
                                                 │
                              ┌──────────────────▼────────────────────┐
                              │  Title screen $F5FD (BY MIKE FOLLIN)  │
                              │  Plays music from $5E88 via $FA32     │
                              │  Reads keys 1..4 → install handler    │
                              └──────────────────┬────────────────────┘
                                                 │
                              ┌──────────────────▼────────────────────┐
                              │       Main game loop $D7FB-$D826      │
                              │ ┌─────────────────────────────────┐   │
                              │ │ $F868   pre-step gate ($E584>?) │   │
                              │ │ $D827   scroll counter          │   │
                              │ │ $D8C2   input snapshot          │   │
                              │ │ $DCAC   player sprite stage     │   │
                              │ │ $DC5D   player attr paint       │   │
                              │ │ $F1A5   ENTITY DISPATCHER       │───┼──→ enemies, hazards, decor
                              │ │ $D9C8   horizontal-move         │   │   (16+ types in $F5A0)
                              │ │ $DCF5   PLAYER DRAW (XOR)       │───┼──→ flicker source
                              │ │ $DFAF   effect tick             │   │
                              │ │ $E248   coords                  │   │
                              │ │ $E8FD   projectiles + fire      │   │
                              │ │ $DE2A   rescue pickup           │   │
                              │ │ $EF02   spawn-sched executor    │───┼──→ next enemy from
                              │ │ $E046   HUD draw                │   │   level table $E69D
                              │ └─────────────────────────────────┘   │
                              └──────────────────┬────────────────────┘
                                                 │ (player altitude hits $75)
                                                 ▼
                              ┌────────────────────────────────────────┐
                              │     Scroll-page  $F6F2                  │
                              │  level = (level+1) mod 6                │
                              │  reload sprite ptr from $E56D            │
                              │  reload speed/colour from $E57C          │
                              │  copy 32-byte spawn schedule $E69D→$E75D │
                              └────────────────────────────────────────┘

   Data flow                                  Code drawing the picture
   ─────────────                              ────────────────────────
   $5E88   ── Follin music notes          ──→ $FA32   (beeper)
   $B0F4   ── master tile bank (~390×8B) ──→ $DAF2   (entity tile blit)
   $B8F4+  ── 16 entity sprite banks      ──→ $F2BC   (entity 16×16 blit)
   $E62B   ── 21 UDG cave tiles           ──→ ROM RST 10 + UDG sysvar
   $E63B   ── player sprite (R+L+effects) ──→ $DCF5   (player XOR draw)
   $E69D   ── 6 × 32-byte spawn schedules ──→ $EF02   (level heartbeat)
   $E881   ── 8 × 4 bullet/particle list  ──→ $E199   (single-byte XOR)
   $3C00   ── Spectrum ROM font           ──→ $E03D   (HUD overwrite)
   $F5A0   ── entity-type table           ──→ $F1F0   (type → bank lookup)
```

Everything below the dashed line is a piece of data; everything
above is a piece of code. Twenty addresses, give or take, span
the entire game. That's the whole machine.

## 22. Closing the loop — the native port is a real game now

The previous milestone (§21 / `native/`) gave us a *demonstrator*:
the cave drew, the ship flew, the procedural generator dripped
entities downward. Playable in the way an early prototype is
playable. Not a game.

This pass closes the gap. The native port now plays the **whole
game**:

* **Original level data is used first.** The six 32-byte schedules
  at `$E69D` are loaded verbatim by
  `OriginalLevels.Load("level-schedules-e69d.bin")`. We re-scale
  the raw 16-bit countdowns (the original ran a multi-pass slicer
  at `$EF02`; we tick once per 50 Hz frame) but the entity
  *types* and *flag bits* are exactly Mike Follin's 1985 mix.
  Pages 0..5 are the cassette; page 6+ falls through to the
  `ProceduralGenerator` for infinite play.
* **Each entity type has its own AI.** `EntityAI.cs` is a table-
  driven dispatcher matching every type id we decoded in
  MEMORY-MAP §`$F5A0`: workers walk along the cave floor and are
  *rescuable*; stalactites cling, wobble, then drop; rocks drift;
  drones and robots fly across at fixed altitude; mine carts and
  wagons roll along the floor; bubbles rise and grant a fuel
  trickle; the force-field bobs vertically; the creature does a
  slow chase in X; the bow-tie sine-drifts; etc. The original
  game's per-type subroutines are scattered across the entity
  dispatcher at `$F1A5`; we don't replicate them line-for-line
  but we honour the *archetype* each type plays in the
  composition.
* **Real collision rules.** A per-kind `CollisionRule` table
  decides what happens when the player touches each entity:
  workers heal +5 shield + 50 score + 1 rescued; bubbles give
  +2 fuel; lava and mine-carts do serious damage; vines and
  pipes are pass-through. Bullets respect `IsBulletProof` (you
  cannot shoot the workers — same rule the original had,
  enforced via the type-3 flag bit on `$F5A0`).
* **Full game-state machine.** `World.State` cycles through
  Title → Playing → Dying → GameOver → Playing. The title screen
  draws "SUBTERRANEAN STRYKER / NATIVE C# PORT / BY MIKE FOLLIN
  1985 / RE-PORT 2026" with a blinking PRESS FIRE prompt; the
  game-over screen shows DEPTH / SCORE / RESCUED and the same
  blinking PRESS FIRE TO RETRY. Lives are tracked (3 to start)
  and rendered as magenta chips on the bottom-right of the HUD.
* **Cave walls.** `World.CaveHalfWidthAt(y)` is a sinusoid whose
  period shrinks with depth, so deeper caves twist tighter. The
  player takes graze damage when straying outside the safe
  corridor — exactly the gameplay the cave-roof drops the
  original used to gate horizontal movement.
* **Sound effects.** `SfxQueue` is a Core-only one-shot queue;
  the Platform layer drains it each frame into `BeeperSynth.Tone`.
  Eight discrete voices: Fire, Hit, Explode, Pickup, Dive,
  Damage, GameOver, LevelUp. The Core has zero audio dependency
  — `World` only enqueues `SfxKind` values; the SDL2 runner is
  the only thing that actually makes noise.
* **Music playback.** `MusicPlayer` walks the 4 KB Follin music
  stream (16-bit period pairs at `$5E88`), one note every 8
  frames, mapping each period to a pleasant Hz via a normalising
  divisor. Not bit-identical to the original `$FA32` Z80 driver
  — that one used the pulse-width slide trick directly on the
  speaker port — but it plays the same *notes* from the same
  *data*. Music ticks only during Playing state; SFX preempt
  in-flight notes.

Verification: a 900-frame headless sweep (`--frames=900 --seed=11
--keys=...`) reproduces the full life-cycle in renders/ — title
screen at f1, gameplay through depths 0 and 1, game-over at f525,
fresh restart at f675, another playthrough through f900. Lives
chips visible bottom-right; HUD reflows correctly between states.

What we deliberately *didn't* port:

* **The exact Z80 sound driver.** `MusicPlayer` plays the data,
  not the timing curve. Reproducing the Follin pulse-width slide
  at sample-accurate rates would mean writing a Z80-cycle-
  accurate beeper emulator — fun, not on the critical path.
* **The original difficulty curve past page 6.** The cassette
  loops the six pages indefinitely; we instead hand off to the
  procedural generator so depth keeps climbing.
* **The exact rescue scoring formula.** The original's `$DE2A`
  rescue pass walks a 4-entry table at `$E46B` whose layout we
  haven't fully decoded; we approximated with a flat +50 score
  +1 rescued per worker grabbed.

The result is a complete, standalone, emulator-free game in
~3500 lines of C# (Core + Platform + Game), driven by 12 KB of
assets extracted from the 1985 tape. End-to-end, it plays.

## 23. The §22 "complete port" was a re-imagining, not a port

User feedback caught what §22 wasn't honest about: the visual is
nothing like the original. The original at depth 1 has a clean
green hillside silhouette with a tree, a left-stacked HUD
(DEPTH / SCORE / SHIELD / FUEL) with multi-colour stripe bars on
the right, and **free-flight + rescue** gameplay — no "dive to
go deeper" mechanic. The user pointed at
[`renders/emu-substryk-f00400_20260527-225827.png`](../renders/)
as the target, and at
[`renders/native-headless-f00350_20260528-015603.png`](../renders/)
as "complete shit". They were right.

What was wrong with §22:
* Cave walls hemming the player in — invented. The original is an
  open playfield.
* "Dive to advance level" — invented. The original is free flight;
  levels progress when every worker is rescued.
* HUD as a bottom strip — wrong layout. The original stacks the
  labels left-side.
* Hand-built `MiniFont` for HUD — the original prints through
  `RST 10` using the ROM font at `$3C00`.
* My "title screen" was a generated banner. The original draws a
  procedural multi-coloured "SUBTERRANEAN STRYKER" banner plus
  the "SELECT CONTROL OPTION TO BEGIN" menu with four numbered
  options.

The native port was not a port. It was a Spectrum-flavoured
shoot-em-up.

## 24. Resetting to a real port — what we actually traced

Switched to the proper RE workflow: run the emulator, peek
memory, disasm the relevant routine, port it. So far traced:

* **`$F6F2` — level-load entry** (called when player advances).
  Increments `($E587)` (current level) mod 6, reads the per-level
  speed/colour byte from `$E57C+level`, then chains nine helpers
  before returning. The helpers are the actual level-init work.

* **`$E319` — copy per-level "init data"**. Reads 32 bytes from
  `$E48D + level*32` (so a 6 × 32 = 192-byte table for the six
  levels), copies them to `$E597`. Format of the 32-byte block is
  unclear yet — values like `36 38 80 00 5E 60 A0 00 7E 40 80 00`
  look like (x, y, ?, 0) records but the high-bit pattern doesn't
  match the spawn-schedule flags. Then calls **`$F1BC`** which
  reads the entity count for this level from `$F2E2 + level` (one
  byte) and the entity-list pointer from `$F594 + level*2`. The
  per-level entity counts are `06 0A 09 0D 12 19` — 6, 10, 9, 13,
  18, 25 entities for levels 0..5.

* **`$E2C6` — load per-level pointers**. Reads `$E56D + level*2`
  into `($E579)` (the active sprite-composition base) and
  `$E58B + level*2` into `($E589)` (purpose still TBD). The
  composition pointers are `$B0F4, $60F4, $70F4, $80F4, $90F4,
  $A0F4` — same as the boot snapshot. For level 0 the active
  composition base IS the master tile bank at `$B0F4`.

* **`$E2E5` — copy spawn schedule**. Already in MEMORY-MAP §$E69D
  but now disasm-confirmed: copies 32 bytes from
  `$E69D + level*32` to `$E75D`.

* **`$E347` — clear and re-paint HUD chrome**. Calls `$E3CE` to
  clear the bottom half of the screen (writes `$00` to `$5000..
  $57FF` and the level colour byte from `$E57B` to `$5800..
  $5AFF`). Opens print channel 2, then walks an `$FF`-terminated
  string table at **`$E785`** through `RST 10`. The table is the
  *literal* HUD layout:

  ```
  E785  10 06 11 00 16 10 00              ; INK 6 (yellow), PAPER 0, AT 16,0
  E78C  "DEPTH :" 0D                       ; row 16
  E794  "SCORE :"                          ; row 17
  E79B  16 11 16 "RESCUED:" 0D             ; AT 17,22 then "RESCUED:" then newline
  E7A7  "SHIELD:" 10 00 11 00 20
  E7B3  10 02 90 90 90 90 90              ; INK 2 (red), 5 × char $90 (filled block)
  E7BA  10 03 90 90 90 90 90              ; INK 3 (magenta), 5 blocks
  E7C1  10 06 90 90 90 90 90              ; INK 6 (yellow), 5 blocks
  E7C8  10 05 90 90 90 90 90              ; INK 5 (cyan), 5 blocks
  E7CF  10 04 90 90 90 90 0D              ; INK 4 (green), 4 blocks
  E7D6  11 00 10 06 "FUEL  :"              ; row 19
  E7E1  11 00 10 00 20 10 02 90 90 90 90 90
        ...                                ; same stripe pattern for FUEL
  ```

  So the HUD is **24 cells wide** of multi-colour 8×8 filled
  blocks: 5 red + 5 magenta + 5 yellow + 5 cyan + 4 green for
  each of SHIELD and FUEL. The colour stripes change as the
  value drains because the game overwrites the trailing blocks
  with PAPER 0 (black).

* **`$E104` — sprite-composition walker** (presumed level-paint).
  Reads `($E579) + $1000`, walks 4096 bytes *backwards* down to
  `($E579)`. For each non-zero byte, calls `$E127` which uses
  `$E1E4` to compute a screen address from `(B, C)` and writes
  `OR (HL); LD (HL),A` — i.e. paints sprite bytes into the bitmap.

  For level 0 the composition base is `$B0F4`, which is the
  master tile bank. For level 1 it's `$60F4` — which is EMPTY in
  the mid-gameplay RAM dump.

  Either the routine doesn't paint scenery the way I read it, or
  the data at `$60F4..$70F3` is built up at runtime by a path I
  haven't traced. The emulator-peeked attributes show row 0..15
  uniformly green-on-black at frame 100 (top half of play area)
  and yellow-on-black at the HUD rows — the *visible* hill shape
  emerges later as entities accumulate.

* **`$F594` — per-level entity-list pointer table**. 12 bytes (6
  × 2). Decoded: `$F2E8, $F2EB, $F33B, $F383, $F3EB, $F47B`. The
  pointers for levels 0→1 are only 3 bytes apart, which rules
  out a uniform 8-byte-per-entity stride. The records must be
  variable-length, probably tagged.

## 25. The honest punch-list

What's truly correct in the native port today:

* `SUBSTRYK.SCR` boot splash — pixel-perfect (it's a literal
  copy of the cassette's screen file).
* Title menu — captured Spectrum SCREEN$ from running the
  emulator past BASIC PAUSE to frame 80. Pixel-perfect.
* Per-level spawn schedules from `$E69D` — bytes correct, just
  re-scaled because we run a straight 50 Hz decrement vs. the
  original's `$EF02` multi-pass slicer.
* Player sprite — correct bytes from `$E63B`/`$E64B`, drawn via
  the XOR primitive ported from `$DCF5`.
* Entity 16×16 quadrant blit — correct port of `$F2BC`.
* Single-byte bullet XOR — correct port of `$E1DE`.

What is *not* correct:

* Per-level **scenery** — the hill silhouette, tree, surface
  decor. The native port draws a placeholder pattern from
  master-tile-bank tiles. The real composition routine and its
  data table location are not fully understood.
* Per-level **static entity placement** — `$F2E8`+ table records
  remain undecoded.
* HUD draw — correct labels in the right shape but rendered with
  my own `MiniFont`, not by `RST 10` through ROM `$3C00`. The
  stripe-bar palette and column layout match `$E785`.
* Per-type entity AI — table-driven approximations in
  `EntityAI.cs`, not byte-for-byte ports of `$F1A5`'s per-type
  subroutines.

The pixel-perfect-by-emulation path is available via
`src/Subterra.Spectrum` if we want a working game today; the
hand-port path requires a step-by-step emu-trace + replace
workflow over multiple sessions.

## 26. The scenery IS the entity system

While building the `diff-frame` workflow (RE-LOG §27, below) we
went looking for where the bottom grass strip lives. At frame
100 of in-game play the emulator's bitmap rows 20..23
($5000..$57FF region, y=160..191) hold a complex 32-column
pixel pattern — clearly tile-composed, with telltale every-
other-scanline-doubled bytes that scream "rendered with
double-height".

We dumped those 1 KB of bitmap, extracted the unique 4-byte
patterns per column, and searched for them in:

* The live mid-gameplay RAM dump (`build/at-frame100.bin`).
* The original boot snapshot RAM (`build/boot-ram.bin`).
* The master tile bank at `$B0F4..$BFFF`, at every byte offset
  (not just tile-aligned).

Zero matches anywhere. The data is not stored verbatim — neither
as a per-level "scenery layout" table nor as preset tile-index
arrays.

The conclusion took embarrassingly long to land: **the scenery
isn't drawn at level-load.** It's drawn by the entity system,
over time, as decor entities (likely types 10 "vine/tree",
11 "creature", 14 "pipe", and the small humanoid workers at
type 0) spawn and either move or persist long enough to
accumulate into a recognisable silhouette. Each entity XORs its
sprite into the bitmap; left alone, the bitmap fills with green
silhouette pixels. This is exactly what MEMORY-MAP §$E69D
already documented as "the cave is procedurally composed by the
entity system" — we just didn't take that literally enough.

Implication for the native port: to reproduce the original's
green hillside-with-tree silhouette, we don't need a scenery
painter or a per-level layout table.  We need:

1. The hazard schedule from `$E69D` running at the right cadence
   so the *right entity types* spawn (already correct in the
   port — the schedules are loaded verbatim).
2. Per-type AI faithful enough that decor entities (10, 14, …)
   stay put and accumulate the way they do in the original
   (only approximated in the port today via `EntityAI.cs`).
3. The sprite-blit primitive `$F2BC` to draw 16×16 entities
   into the bitmap (already correctly ported as
   `Blitters.DrawSprite16x16`).

In short, the path to a matching scenery is to **make the entity
AI exact**, not to build a scenery painter. That's the real
shape of the remaining port work.

## 27. The diff-frame workflow

Built `src/Subterra.Tools/DiffFrameCommand.cs`. The native
headless mode now also writes a `.png.rgba` sidecar (raw RGBA
bytes) so external tools can read the framebuffer without a
PNG decoder. The diff command:

1. Runs the cassette inside our Z80 emulator for N frames with
   a given key spec.
2. Runs the native C# port in `--headless` mode for the same N
   frames, with a (possibly different) key spec to drive its
   state machine through splash → title → play.
3. Composes a 3-panel PNG into `renders/`:
   *emulator | native | red-on-diff*.
4. Prints the pixel-diff count and percentage to stdout.

Usage:

```
dotnet run --project src/Subterra.Tools -- diff-frame \
    original/rom/48k.rom original/dumps/SUBSTRYK.Z80 100 \
    -keys=5-10:SPACE,40-50:1 \
    -native-keys=0-30:FIRE \
    -seed=1
```

First baseline measurement: **22.47% pixel diff** at frame 100.

After porting the HUD layout from the `$E785` string table
(see §24) and removing the hill-silhouette placeholder from
the native's `DrawLevelScenery`: **12.91%**.

The remaining gap at frame 100 is entirely in the bottom
4 char-rows — the procedural scenery problem from §26.

## 28. The space ship correction

We had `dive` / `diving` scattered through the codebase and
docs. The user corrected us in plain terms: *"stop using
diving, it's a SPACE SHIP, it goes up down left and right!!!"*.
Purged the terminology across:

* `native/SubterraCS.Core/GameInput.cs` — comment on `Down`.
* `native/SubterraCS.Core/SoundEffects.cs` — `SfxKind.Dive`
  renamed to `SfxKind.Thrust`.
* `native/SubterraCS.Core/World.cs` — class docstring rewritten.
* `native/SubterraCS.Core/ProceduralGenerator.cs` — comment.
* `native/README.md` — controls section, status table, audio
  voice list.
* `README.md` (root) — controls section, smoke-test instructions,
  feasibility blurb.

The Stryker is a space ship with free flight in all four
directions; a level advances on rescuing all workers, not on
hitting an altitude gate. (`$E584` altitude in the original IS
used by the page-advance gate, but that's an *implementation
detail* of how the original game moves the player between
levels, not a player-facing "dive" mechanic.)

## 29. Pick one thing and port it properly — the HUD

Per the user's "be systematic, don't invent" directive, dropped
shallow guessing and traced the HUD end-to-end. Every byte in
the final port is justified by a specific address peek or disasm.

### Where the bars actually come from

Empirical test that broke my earlier assumption: dumped the
first SHIELD-bar cell's bitmap at frame 60 (just after level-
load) AND at frame 100. At frame 60 the cell reads <c>88 80 00
00 00 00 80 88</c>; at frame 100 it reads <c>88 80 FF FF FF FF
80 88</c>.  Same UDG-A cell, but the middle 4 scanlines went
from `$00` to `$FF` between the two frames.

So the bars are NOT a "full bar minus drain". They start EMPTY
(UDG-A corners only) and get filled by a per-frame routine.

### The per-frame HUD updater at $E046

```
E046  LD HL,$5027; LD ($E45D),HL          ; print position = SCORE row
E04C  LD HL,($E459); CALL $DFF6            ; print score (6 digits)
E052  XOR A; CALL $E01E
E056  LD HL,$503D; LD ($E45D),HL          ; print position = RESCUED row
E05C  LD HL,($E469); LD H,$00; CALL $E009  ; print rescued count
E064  LD HL,$5007; LD ($E45D),HL          ; print position = DEPTH row
E06A  LD A,($E587); CALL $E01E             ; print depth
... (~50 bytes of attribute-flash work for the top bands) ...
E0AB  LD HL,$5247; LD A,($E464); CALL $E0BE  ; draw SHIELD bar
E0B4  LD HL,$5267; LD A,($E466); CALL $E0BE  ; draw FUEL bar
```

### The bar driver at $E0BE

```
E0BE  NOP
E0BF  CP $60                  ; max value = 96
E0C1  RET Z                   ; if full, nothing to do
E0C2  RET NC                  ; clamp upper bound
E0C3  LD E,A; SRL E; SRL E    ; E = value / 4
E0C8  INC E; DEC E; JR Z,$E0CF
E0CC  LD D,$00; ADD HL,DE     ; advance HL by (value/4) bytes (one byte = one bar column)
E0CF  LD C,$FF; CALL $E0F1    ; write $FF to 4 middle scanlines at HL
E0D4  LD C,$00; INC HL; CALL $E0F1   ; write $00 at HL+1
E0DA  EX DE,HL; LD HL,$E0EC; AND $03; ...; LD C,(HL)
E0E6  EX DE,HL; CALL $E0F1    ; partial-fill at HL+? using $E0EC table
```

The cell-paint inner loop at `$E0F1`:

```
E0F1  PUSH HL; LD (HL),C; INC H; LD (HL),C; INC H; LD (HL),C; INC H; LD (HL),C; POP HL; RET
```

`INC H` is the Spectrum interleave trick — within an 8-line char
band, advancing the high byte of the bitmap pointer moves down
one pixel row.  So this writes byte `C` to four consecutive
scanlines of the same column.

### The partial-fill table at $E0EC

```
$E0EC:  00 C0 F0 FC  FF E5 71 24
```

The first four entries (`$00, $C0, $F0, $FC`) are the masks for
the boundary cell — bit patterns showing 0, 2, 4, or 6 left-
aligned pixels.  Combined with the 24-cell bar and `value/4`
boundary, this gives 24 × 4 = 96 quanta of resolution.  Each cell
holds 4 units; the partial cell shows the remainder as 0/2/4/6
of 8 pixels.

### The UDG-A frame at $E62B

The bar's "corner brackets" come from UDG A:

```
$E62B:  88 80 00 00 00 00 80 88
```

Verified unchanged between boot snapshot and mid-gameplay RAM
(was paranoid the game might redefine it; it doesn't).

### The HUD label layout from $E785

Already documented in §24.  Walked by `$E347` at level-load via
RST 10 with CHARS set to `$3C00` so the labels render in the
Spectrum ROM font.

### Native-port port

`Hud.cs` and the new `RomFont.cs` now reproduce every byte:

* `BarCells = 24`, `BarMaxValue = 96`, `QuantaPerCell = 4` — the
  range comes from the `CP $60` and double `SRL E` in `$E0BE`.
* `UdgA = { 0x88, 0x80, 0x00, 0x00, 0x00, 0x00, 0x80, 0x88 }` —
  the verified bytes from `$E62B`.
* `PartialMask = { 0x00, 0xC0, 0xF0, 0xFC }` — first four bytes
  of `$E0EC`.
* `StripeAttr` — 5+5+5+5+4 runs of bright red / magenta / yellow
  / cyan / green, exact INK codes from the `$E785` INK control
  bytes.
* `RomFont` loads the 768 bytes at `$3D00..$3FFF` from
  `assets/extracted/rom-font.bin` (extracted via `dd` from the
  48K ROM) and exposes a `Draw(fb, x, y, s, attr)` helper that
  blits each char's 8 scanlines.
* `DrawBar` writes UDG-A corners + middle = `$FF` (full),
  `$00` (empty), or the partial-mask byte at the boundary cell.

### Measured impact

`diff-frame ... 100 -keys=5-10:SPACE,40-50:1 -native-keys=0-30:FIRE -seed=1`

| Before | After bar geometry | After ROM font |
| ------ | ------------------ | -------------- |
| 22.47% | 10.66%             | **9.63%**      |

The remaining diff at frame 100 is concentrated in: (a) the bottom
grass strip (procedural decor — §26), (b) workers walking through
the HUD row (the original draws them on top — they're a per-level
static placement we haven't decoded yet), and (c) the player ship
position because game-state alignment between emu and native is
not exact frame-for-frame.

## 30. Per-level entity records are 8-byte, not unaligned

Last session I bailed on the `$F2E8` entity-list format because
the per-level pointer table at `$F594` had a suspiciously-small
3-byte gap between levels 0 and 1.  That looked like variable-
length records.

Re-checked with arithmetic and the answer was right there:

| Level | Count | Start    | Span to next | Bytes/entity |
| ----- | ----- | -------- | ------------ | ------------ |
| 0     | 6     | `$F2E8`  | 3            | 0.5 (!?)     |
| 1     | 10    | `$F2EB`  | 80           | **8.00**     |
| 2     | 9     | `$F33B`  | 72           | **8.00**     |
| 3     | 13    | `$F383`  | 104          | **8.00**     |
| 4     | 18    | `$F3EB`  | 144          | **8.00**     |
| 5     | 25    | `$F47B`  | ≥200         | **8.00**     |

Levels 1..5 are *uniform 8-byte records*, matching the in-memory
IX-walked layout already in MEMORY-MAP §`$F1EF`:

| Offset | Meaning |
| ------ | ------- |
| +0     | Type id (index into `$F5A0`) |
| +1     | y coordinate |
| +2     | Animation frame index |
| +3, +4 | Top-half screen address (Spectrum bitmap, lo/hi) |
| +5, +6 | Bottom-half screen address |
| +7     | Flag / facing byte (TBD) |

Level 0 is anomalous: 6 entities × 8 bytes = 48 bytes, but the
next level's pointer is only 3 bytes later — so level 0 either
shares records with level 1 (each level reads from a different
starting offset of a shared bytestream) or the boot snapshot
just has level 0 in an uninitialised state.  Either way, levels
1..5 decode cleanly.

### Verification against the running emulator

Confirmed level 1's records make sense by reading the *active*
entity list pointer `($F1B9)` from the mid-gameplay RAM dump:
`$F2EB` (exactly level 1's pointer), `$F1BB` = 10 (the count
from `$F2E2[1]`). Walking 10 × 8 bytes from `$F2EB` shows
plausible type IDs (`02, 0A, 01, 01, 01, 04, 04, 08, 09, 12`)
and screen addresses in the `$4000..$48FF` bitmap range, just
as expected.

### Decoded screen addresses

The original stores entity positions as their *top-half
Spectrum bitmap address*, not as `(x, y)` pixels.  The native
port reverses this in `LevelEntities.DecodeBitmapAddress`:

```csharp
int bitmapOffset = addr - 0x4000;
int yBand    = (bitmapOffset >> 5) & 0xC0;   // y bits 7,6
int yPixRow  = (bitmapOffset >> 8) & 0x07;   // y bits 2,1,0
int yCharRow = (bitmapOffset >> 2) & 0x38;   // y bits 5,4,3
int xByte    = bitmapOffset & 0x1F;
int y = yBand | yCharRow | yPixRow;
int x = xByte << 3;
```

### Port

Extracted `assets/extracted/level-entities-f2e8.bin` (654 bytes:
6 counts + 6 × N × 8 records).  New `LevelEntities` class loads
it; `World.PlaceWorkersForLevel(n)` consumes the records and
creates one `EntityInstance` per record with the right type,
frame, and decoded `(x, y)`.

The visible result at this commit: entities appear at sensible
positions for levels 1+ (the placeholder fallback path still
runs for level 0 because of the anomaly).  The pixel-diff
number didn't move much (9.63% → 9.81% → 9.63%) because the
per-entity sprite still has to be drawn by per-type AI that's
not byte-faithful yet — that's the next port target.

## 31. The bottom "grass strip" is actually a mini map

User insight: *"what you might call the bottom grass is probably
the mini map of the level, no?"*  Re-examined the bottom strip
(y=160..191) with this lens and the structure snapped into focus.

### What's verified

* The bottom-strip BITMAP is identical at frames 100 and 1500
  (stabilises once drawn).
* The pattern is paired-scanlines (`XX XX YY YY ZZ ZZ …` per
  column), the signature of a vertical 2× stretch.
* `$60F4..$70F3` (the per-level "sprite-composition" pointer
  target — `$E579` is set to `$60F4` during level 1) holds 4 KB
  of data, 37% non-zero at frame 100.
* That data is BYTE-IDENTICAL at frames 60 and 100 — populated
  by some routine early in level-load, then static.
* `$E104` is the walker I'd already half-decoded: it traverses
  the 4 KB region backwards calling `$E127` (which OR-writes
  pixels via `$E1E4` and the screen-row math
  `B = $20 - (outer<<1)` giving 16 source rows that map to the
  32-pixel-tall strip with 2× stretch).
* 16 source rows × 256 source cols = 4096 source bytes — exact
  match for the buffer size.

### What's still hypothesis

* Whether each source byte's VALUE matters (used as a pixel mask)
  or only its *non-zero-ness* (used as a stamp marker). The
  `$E127` code I disassembled reads `A` from the caller without
  setting it, so the actual pixel byte written depends on the
  caller's previous A value — still need to trace that.

### What we got wrong before

* The 272 bitmap-byte changes I observed in y=160..191 between
  f60 and f100 are NOT driven by changes in `$60F4..$70F3`
  (which is static).  They must come from a different source —
  most likely entity sprites that happen to draw in the strip,
  e.g. type 8 (explosion) which spawns at y=179 per the level-1
  entity records.  So the bottom strip = static mini-map
  background + transient entity sprite overlap.

### Searches that came back empty

* The chunk `A1 A4 A5 A8 …` at `$6309` (first non-zero region
  of the mini-map buffer) appears nowhere else in the 48 K RAM
  — so the buffer is not COPIED from a packed level asset.
  Something in the level-load chain WRITES the bytes
  computationally.  The exact routine is still TBD.

### Port

`MiniMap.cs` ships the buffer (4096 bytes) + walker + per-pixel
stamp helper.  Wired into `World.Draw` and `World.LoadLevel`.
Buffer stays empty until we trace the populator; renders a blank
strip for now.  This sets up the right SHAPE so that as soon as
we find what writes the bytes, plugging it in needs no
plumbing.

## 32. Mini-map shows level + entity positions — half-confirmed

User: *"note that the mini map shows the whole level but also the
position of the enemies and such"*.

Verifications:

* `$60F4..$70F3` (the supposed mini-map source buffer) differs by
  **exactly 1 byte** between f60/f100/f200/f400.  Effectively
  static after early init — *not* updated per-frame with entity
  positions.
* The bottom-strip BITMAP at y=160..191 differs by 272 bytes
  between f60 and f100 (the level-paint pass).  Then 0 bytes
  f100→f200 and only **12 bytes** f200→f400.
* The 12 late-stage diffs are all single-bit XORs at scattered
  pixel positions — pattern matches `$E1DE`-style single-byte
  XOR writes (the "particle / bullet" draw primitive).

Cross-check against the `$E881` particle table (8 slots × 4
bytes = x, y, dx, dy per MEMORY-MAP):

| Slot | f200 (x,y) | f400 (x,y) | Movement? |
| ---- | ---------- | ---------- | --------- |
| 0    | (50, 188)  | (128, 188) | yes       |
| 1    | (89, 71)   | (128, 188) | yes       |
| 2    | (11, 149)  | (128, 188) | yes       |
| …    | …          | …          | …         |

Several particles are at y ≥ 160 (i.e. IN the bottom strip
region), and they ARE updated every frame.  So the "moving
single-pixel markers" we see at y=160..191 are most likely just
**particles whose trajectories happen to traverse the bottom-
strip area** — they're the same particle/bullet system the
play area uses, not a dedicated mini-map marker layer.

What this means for the user's claim:

* "Mini-map shows the level" — partially supported.  The
  bottom-strip BITMAP holds a structured per-column terrain
  pattern that doesn't change, suggesting a static layout view.
  Whether `$60F4` populates it via `$E104` (theory of §31) or
  something else draws it directly (the 272-byte f60→f100 paint
  passing through some other code) is still open.
* "Position of the enemies" — only partially.  Enemy/particle
  motion DOES make pixels flicker in the bottom strip, but
  that's a side-effect of those entities being at y > 160,
  not a dedicated tracking layer.  The "real" entity tracking
  for AI / collision lives in `($F1B9)`'s 8-byte records and
  the `$E881` particles.

Honest wall: I can't make the bottom strip render correctly in
the native port without either (a) tracing the routine that
writes those 272 bytes of initial paint, or (b) capturing the
per-level mini-map bitmap and shipping it as an asset (which
crosses the "don't capture levels" line).  The infrastructure
in `MiniMap.cs` is ready for (a) when the routine is found.
