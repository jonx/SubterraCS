# Tools

Every tool in this repository is hand-written: no third-party Spectrum
emulator, no third-party Z80 disassembler, no third-party imaging
library. The entire pipeline — from "give me the 48 K RAM" to "render
that tile as PNG" — lives in this solution. This file documents each
tool: **what it does**, **why we built it**, and **how we use it** in
practice.

Each tool is also a piece of the larger story in
[`RE-LOG.md`](RE-LOG.md); cross-references appear inline.

---

## The runtime ("library") layer

These projects are referenced by every CLI command and by both GUI
apps. They have **no third-party dependencies** beyond the .NET
BCL.

### `Subterra.Spectrum`

The Spectrum side of the runtime. Self-contained: snapshot loading,
CPU emulation, screen rendering, PNG output.

| Type | Purpose |
| --- | --- |
| `Z80Snapshot` + `Z80SnapshotReader` | Decompress and parse `.z80` v1 / v2 / v3 snapshot files. Returns a 48 K flat RAM image plus the captured register state. |
| `Z80Cpu` + `IZ80Bus` | Hand-written Zilog Z80 CPU emulator. Full documented instruction set: main + CB + ED + DD + FD + DD-CB + FD-CB. Flag-correct ALU including the F3 / F5 "undocumented" bits that follow the result. The bus is an interface so the CPU is testable in isolation; the host machine implements it. |
| `Spectrum48` | The 48 K Spectrum host: 16 K ROM at `$0000-$3FFF`, 48 K RAM at `$4000-$FFFF`, ULA port `$FE` (border out + keyboard read in), IM 0 / IM 1 / IM 2 interrupts at 50 Hz. Exposes `LoadSnapshot`, `RunFrame`, and `MemoryWritten` event for tracers. |
| `SpectrumScreen` | Spectrum bitmap-address arithmetic (the famous interleaved scanline order) + the 16-colour palette + a `.scr` → RGBA decoder. |
| `SpectrumKey` + `Spectrum48KeyExtensions` | Half-row/bit identifiers for every key on the 48 K rubber keyboard, plus `PressKey` / `ReleaseKey` / `ReleaseAllKeys` extension helpers. |
| `PngWriter` + `Crc32` | Tiny dependency-free RGBA PNG encoder, with our own CRC-32. Output is filter 0 (None) plus deflate via `System.IO.Compression`. |
| `RenderTarget` | Every render in the project goes through this so it lands in `renders/` with a `_yyyymmdd-hhmmss` suffix — never overwriting. Also walks up from the build output to find the repository root. |
| `Z80Disassembler` + `Z80Instruction` | Disassembler covering the documented instruction set; falls back to `DEFB` for genuinely undocumented combinations rather than producing garbage. |

**Why this layer exists separately:** the GUI projects (`Subterra.Game`,
`Subterra.Editor`) and the CLI (`Subterra.Tools`) all need the same
primitives — load a snapshot, decode a screen, render a tile, step a
CPU. Keeping them in a single dependency-free library means the
runtime is portable to any future host (web, mobile, headless server).

### `Subterra.Assets`

Higher-level decoders that interpret RAM regions as game assets.

| Type | Purpose |
| --- | --- |
| `SpriteSheet` | Decodes a chunk of memory as a grid of 1-bit Spectrum bitmap cells (arbitrary width-in-bytes × height-in-rows × cell-count). Can render an individual cell or a full contact sheet with a 1-pixel grid, optionally up-scaled by integer factor for chunky-pixel legibility. |
| `RenderedImage` | RGBA image record (bytes + width + height). Has an `UpscaleNearest(factor)` for crisp pixel-art zoom. |

**Why:** every asset-extraction command (`render-scr`, `render-snapshot`,
`sprite-scan`, the Editor's sprite preview) ends up needing the same
"interpret these bytes as 1-bit-per-pixel and render to RGBA" loop.
Centralising it here keeps the CLI commands and the GUI in lockstep.

---

## The CLI: `subterra` (project `Subterra.Tools`)

A single .NET console executable with sub-commands. Built as a swiss-army
toolkit so each new reverse-engineering question we asked could be
answered with a one-liner, and the answers were repeatable / commitable.

Run with no arguments to get the full help; or `dotnet run --project
src/Subterra.Tools -- <command> [args]`.

### `render-scr <path/to/file.scr>`

**What:** decodes a 6 912-byte ZX Spectrum screen file (raw bitmap +
attributes) into RGBA and writes a timestamped PNG into `renders/`.

**Why:** the very first thing we wanted to see was the iconic
"SUBTERRANEAN STRYKER" loading screen, to prove our bitmap-address
arithmetic was correct. It caught our first bug (replicated title
text — see RE-LOG §6) within minutes.

**How:**
```sh
dotnet run --project src/Subterra.Tools -- \
    render-scr original/dumps/SCRSHOT/SUBSTRYK.SCR
```

### `render-snapshot <path/to/file.z80>`

**What:** decompresses a `.z80` snapshot, then renders the snapshot's
own screen memory (`$4000-$5AFF`) as a PNG.

**Why:** lets us see what the snapshot was capturing without booting
an emulator. For `SUBSTRYK.Z80`, this showed the title screen — and
since the snapshot's PC was in the ROM `PAUSE-1` routine, that told
us the snapshot was taken with the game waiting for a key on its
title screen.

**How:**
```sh
dotnet run --project src/Subterra.Tools -- \
    render-snapshot original/dumps/SUBSTRYK.Z80
```

### `unz80 <file.z80> <output.bin>`

**What:** decompresses a `.z80` snapshot and writes the flat 48 K RAM
image to `<output.bin>` (Spectrum address `$4000` lands at file
offset 0).

**Why:** a lot of analysis is easier on a raw byte stream than on a
compressed snapshot. This is the one-liner to get there. Useful as a
sanity check on the snapshot reader, too.

**How:**
```sh
dotnet run --project src/Subterra.Tools -- \
    unz80 original/dumps/SUBSTRYK.Z80 build/substryk-ram.bin
```

### `snapshot-info <file.z80>`

**What:** prints the registers (`PC`, `SP`, `AF`, …), the interrupt
mode, the IFF flags, plus a per-4 K-block byte-value histogram of the
48 K RAM.

**Why:** quickest way to orient yourself in an unfamiliar snapshot.
The histogram immediately tells you which blocks are zero-padded vs
dense; the registers tell you what the snapshot was paused on. RE-LOG
§5 starts with this output.

**How:**
```sh
dotnet run --project src/Subterra.Tools -- \
    snapshot-info original/dumps/SUBSTRYK.Z80
```

### `disasm <file.z80> <hexAddr> <count> [out.asm]`

**What:** Z80 disassembly of `<count>` instructions starting at
`<hexAddr>` (16-bit, hex). Writes to stdout unless `out.asm` is given.

**Why:** the bread-and-butter of any reverse-engineering session.
This is how we read the BASIC loader, the input handler at `$D8F0`,
the main game loop at `$D7FB`, the XOR sprite-draw at `$E1DE`, etc.
Covers every documented Z80 opcode plus CB / ED / DD / FD prefix
tables and the DD-CB / FD-CB indexed bit ops.

**How:**
```sh
dotnet run --project src/Subterra.Tools -- \
    disasm original/dumps/SUBSTRYK.Z80 F5FD 30
```

### `stack-walk <file.z80> [depth]`

**What:** dumps the top of stack from a snapshot as 16-bit words —
candidate return addresses.

**Why:** when the snapshot is captured deep in a chain of ROM calls,
peeking the stack tells you what the *game* code called into the ROM
to do. Used early on to confirm the snapshot's PC at `$1F3D` was a
ROM `PAUSE` routine, not game code.

**How:**
```sh
dotnet run --project src/Subterra.Tools -- \
    stack-walk original/dumps/SUBSTRYK.Z80 12
```

### `hex <file.z80|file.bin> <hexAddr> <count>`

**What:** classic hex + ASCII dump of `<count>` bytes starting at
`<hexAddr>`. Accepts either a `.z80` snapshot or a raw 48 K RAM dump
(`.bin`).

**Why:** sometimes you just want to see the bytes. Used heavily to
follow the BASIC loader at `$5CCB`, inspect system variables, and
hand-trace structures we suspected (e.g. `($5C36)` pointing into the
ROM font).

**How:**
```sh
dotnet run --project src/Subterra.Tools -- \
    hex original/dumps/SUBSTRYK.Z80 5CCB 100
```

### `find-bytes <file.z80> <hex-pattern> [-min=ADDR] [-max=ADDR]`

**What:** opcode/data pattern search across the 48 K RAM. The pattern
is a sequence of hex bytes, optionally with `??` wildcards.

**Why:** lets you grep for *instructions*, not just bytes. We used it
to find every `IN A,($FE)` (keyboard read), every `LDIR` / `LDDR`
(block-move that might be a sprite draw), every reference to a
candidate variable address, and so on. This is how we located the
draw routines we then disassembled.

**How:**
```sh
# every "LD A,n ; IN A,($FE)" — keyboard reads
dotnet run --project src/Subterra.Tools -- \
    find-bytes original/dumps/SUBSTRYK.Z80 "3E ?? DB FE" -min=5E88
```

### `run-emu <48k.rom> <file.z80> <frames> [opts]`

**What:** boots the snapshot inside our Z80 emulator, runs `<frames>`
video frames, and renders the final screen to `renders/`. Options:

* `-keys=START[-END]:KEY,...` — schedule key presses by frame number
* `-stride=N` — also drop a render every N frames into `renders/`
* `-ram=path/out.bin` — dump the 48 K RAM after the run
* `-wav=path/out.wav` — render the run's beeper output (port `$FE`
  bit 4, captured edge-by-edge with CPU-cycle stamps) to a mono
  16-bit PCM WAV via area sampling — see
  [disasm/sound.md](disasm/sound.md)
* `-wav-rate=N` — WAV sample rate in Hz (default 44100)

**Why:** the very first proof that the emulator works correctly. With
no keys pressed, the snapshot renders identically to the title
screen. With `SPACE` pressed on frame 5 it advances to the game's own
control-selection menu; with `1` pressed shortly after that it lands
in gameplay. The `-ram` option is what gives the Editor a meaningful
RAM dump to work from (UDGs and tile bank are populated only *after*
the game's init runs).

**How:**
```sh
# play the game for 600 frames; press SPACE, then 1, then dive
dotnet run --project src/Subterra.Tools -- \
    run-emu original/rom/48k.rom original/dumps/SUBSTRYK.Z80 600 \
    -keys=5-10:SPACE,40-50:1,200-500:A \
    -stride=50 \
    -ram=build/post-game.bin
```

### `emu-peek <48k.rom> <file.z80> <frames> <hexAddr>... [-keys=...]`

**What:** like `run-emu`, but instead of rendering it prints the
byte / word / triple value at each listed memory address after the
run.

**Why:** built to watch a hypothesised game-state variable evolve
across frames. Decisive in solving "the ship doesn't scroll" — by
peeking `($E584)` while DOWN was held we could see the altitude
counter tick up to `$75` and then *reset to 0* on the level
scroll-advance. RE-LOG §10.

**How:**
```sh
dotnet run --project src/Subterra.Tools -- \
    emu-peek original/rom/48k.rom original/dumps/SUBSTRYK.Z80 400 \
    E583 E584 E587 \
    -keys=5-10:SPACE,40-50:1,200-500:A
```

### `sprite-scan <file.z80|file.bin> <fromHex> <toHex> <WxH[,WxH...]> [opts]`

**What:** bulk render of candidate sprite cells across a RAM range.
Each starting address × shape combination produces one contact-sheet
PNG in `renders/`. Options:

* `-cols=N` — grid columns per sheet
* `-count=N` — cells per sheet
* `-scale=N` — integer-nearest upscale

**Why:** we didn't yet know where the sprite/tile data lived, so we
needed a way to *eyeball* RAM as picture. After running the game and
dumping post-game RAM, this surfaced the `$E62B` UDG cave tiles
straight away. Later, narrowing in to `$B0F4` with this tool
confirmed the 390-tile master bank.

**How:**
```sh
# Bulk scan, 8x8 cells, 32 cols, 384 cells per sheet
dotnet run --project src/Subterra.Tools -- \
    sprite-scan build/post-game.bin B0F4 BCF4 8x8 \
    -cols=32 -count=384 -scale=4
```

### `tile-trace <48k.rom> <file.z80> <frames-before-trace> [-keys=...]`

**What:** boots, runs N frames, then for one additional frame
single-steps the CPU and watches for `PC == $DAF3` — the inner of
the indexed-tile-draw routine. Reports every tile index drawn during
that frame, where it landed on screen, and a per-256-byte PC
histogram of which game code ran.

**Why:** to find where the *active* draw routines live. The PC
histogram alone is gold — it instantly told us the hot region is
`$E100-$E2FF`, not the `$DAF2` path we'd assumed. That redirected us
to the XOR sprite-draw at `$E1DE`. RE-LOG §14.

**How:**
```sh
dotnet run --project src/Subterra.Tools -- \
    tile-trace original/rom/48k.rom original/dumps/SUBSTRYK.Z80 300 \
    -keys=5-10:SPACE,40-50:1,200-300:A
```

### `player-dump <file.bin|file.z80>`

**What:** reads the player Stryker's three pieces — the live
sprite buffer at `$E8A9`, the four screen addresses at `$E8C9`,
and the source-frame bank at `$E63B` / `$E64B` — and writes three
PNGs to `renders/`: the live (mid-XOR) sprite, the right-facing
source frame, and the left-facing source frame. Also prints the
raw bytes per row in ASCII art for the truly curious.

**Why:** to confirm we found the *actual* player sprite. RE-LOG
§17 records the embarrassing detour: I first thought entity
type 0 was the player, the user noticed it was the workers'
pickaxes instead, and the real player turned out to live in a
completely separate code path (XOR draw at `$DCF5`, sprite
source at `$E63B`).

**How:**
```sh
dotnet run --project src/Subterra.Tools -- \
    player-dump build/post-game.bin
```

### `entity-bank <file.z80|file.bin> [hexAddr] [frames] [-cols=N] [-scale=N]`

**What:** renders an *entity-type sprite bank* — 16 frames × 32 bytes
each, laid out in the column-major quadrant layout the game uses —
into a single PNG. Pass `all` as the address to auto-walk the type
table at `$F5A0` and dump every type's bank, using each type's own
attribute byte for the ink colour.

**Why:** once we found that bigger sprites in the game (the player
sub, enemies) are stored at `(IY+0,1) + frame*32` in a column-major
quadrant layout — and that `IY` is loaded from the type table at
`$F5A0` — this is the tool that produced the legible PNGs. Type 0
turns out to be the player submarine; the rest are the enemies and
hazards. See RE-LOG §16.

**How:**
```sh
# Dump *all* identifiable entity types, picking the ink colour from
# each type's attribute byte automatically.
dotnet run --project src/Subterra.Tools -- \
    entity-bank build/post-game.bin all

# Or dump one specific bank by address
dotnet run --project src/Subterra.Tools -- \
    entity-bank build/post-game.bin B8F4 16 -cols=8 -scale=5
```

### `scrwrite-trace <48k.rom> <file.z80> <frames-before-trace> [-keys=...]`

**What:** subscribes to `Spectrum48.MemoryWritten` and logs every
write into bitmap memory (`$4000-$57FF`) and attribute memory
(`$5800-$5AFF`) during one further frame. Prints:

1. Total bitmap / attribute writes and number of distinct addresses.
2. Top "hot PCs" — the instructions that did most of the writing.
3. Sprite source bytes captured at `$E1E1` (A right before `XOR (HL)`).
4. The first 40 bitmap writes chronologically, with PC + screen
   address + byte + decoded `(x, y)` pixel coordinates.

**Why:** the strongest forensic tool in the kit. It's how we
*proved* that all moving-sprite drawing in one gameplay frame
collapses to a single instruction (`LD (HL),A` at `$E1E2`), and how
we found the other two draw paths (`$E041` = ROM font overwrite,
`$DAF2` = tile bank). RE-LOG §14.

**How:**
```sh
dotnet run --project src/Subterra.Tools -- \
    scrwrite-trace original/rom/48k.rom original/dumps/SUBSTRYK.Z80 300 \
    -keys=5-10:SPACE,40-50:1,200-300:A
```

---

## The GUI apps

### `Subterra.Game` — the playable emulator window

```sh
dotnet run --project src/Subterra.Game
```

Avalonia 12 application. Boots the bundled snapshot inside our
`Spectrum48` host and ticks one frame per 20 ms (50 Hz) on a
`DispatcherTimer`. Each frame: the 6 912-byte screen region is
decoded to RGBA via `SpectrumScreen.DecodeRgba`, copied into a
`WriteableBitmap`, and pushed into an `Image` element.

Keyboard input goes through `MapKey` in `MainWindow.axaml.cs`, which
maps Avalonia `Key` values to `SpectrumKey` half-row/bit identifiers.
QWERTY letters/digits map directly to the corresponding Spectrum
keys; cursor arrows map to the Sinclair "Cursor" joystick (`CAPS` +
5/6/7/8). `Esc` quits.

**Why:** the cross-platform playable artefact the project promised.
It's the same emulator the CLI uses (just driven by a GUI loop
instead of a frame counter) — meaning every CLI improvement
immediately improves the game window.

### `Subterra.Editor` — the asset/sprite viewer

```sh
dotnet run --project src/Subterra.Editor
```

Avalonia 12 application. Auto-loads `build/post-game.bin` if present
(richer state) or falls back to the bundled boot snapshot.

The UI:

* **Toolbar:** address text box, cell width-in-bytes, cell height,
  cell count, columns-per-row, "Refresh" / "Save PNG" buttons.
* **Preset buttons** for the asset banks we've identified so far:
  Tile bank (`$B0F4`, 8×8), Cave UDGs (`$E62B`, 8×8), Music data
  (`$6000`), Title string (`$F82B`), generic Game data (`$E000`).
* **Sprite grid** — the rendered contact sheet, 3× nearest-neighbour
  zoom for legibility.
* **Hover panel** — moves with the mouse, shows the selected cell
  index, its Spectrum address, raw bytes, and (when the tile-bank
  preset is active) the tile index relative to `$B0F4`.

**Why:** the CLI is fast for scripts but slow for exploration. The
Editor lets you scroll through memory at any cell size and visually
hunt for patterns. Hovering a candidate sprite reveals its raw bytes
so you can copy them into a `find-bytes` query and look for
references.

**Save PNG** drops a clean contact sheet (white-on-black, with a
grid) into `renders/`, just like the CLI tools.

---

## Hand-written by design

The point of the toolset isn't just to get to a working port — it's
to make it possible for a single reader to follow every line of code
from "load a snapshot" to "render the game". Every byte of every
tool here has provenance in this repository. No dependency we don't
control is allowed to come between the user and the game.

That self-imposed constraint also produced the small simple
primitives — the byte-level encoder, the CRC-32, the snapshot RLE
decoder — that the project leans on every day.
