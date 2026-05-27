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
