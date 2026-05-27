# Subterranean Stryker — Reverse Engineering & C# Port

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4.svg)](https://dotnet.microsoft.com/)
[![Avalonia 12](https://img.shields.io/badge/Avalonia-12-883a99.svg)](https://avaloniaui.net/)

A clean-room-ish reverse engineering project that takes the 1985 ZX
Spectrum game **Subterranean Stryker** (Insight Software — code by
Mark Wilson & Peter Gough, music by Tim & Mike Follin) and rebuilds
it as a portable, cross-platform C# program with a hand-written Z80
emulator, a complete reverse-engineering toolkit, and an
asset/sprite viewer.

Every line of code in this repository is hand-written — no
third-party Spectrum emulator, no third-party Z80 disassembler, no
third-party imaging library. The point isn't just to reach a
working port; it's to make it possible for a single reader to
follow every line of code from "load a snapshot" to "render the
game".

<p align="center">
  <img src="renders/scr-substryk_20260527-224113.png" alt="Loading screen — submarine, explosion, INSIGHT logo, SUBTERRANEAN STRYKER title" width="384"/>
</p>

---

## What works today

### A live, playable game

A hand-written Z80 emulator, plus a 48 K Spectrum host (RAM + ROM +
ULA), inside an Avalonia 12 window. The original 1985 binary boots,
runs, and accepts input on macOS, Linux, and Windows.

<p align="center">
  <img src="renders/emu-substryk-f00030_20260527-225654.png" alt="Game's own title screen, BY MIKE FOLLIN, control select menu" width="256"/>
  <img src="renders/emu-substryk-seq-f00280_20260527-225827.png" alt="Gameplay — surface scene with tree, terrain, HUD reading DEPTH 1 SCORE 000000 SHIELD FUEL RESCUED 00" width="256"/>
  <img src="renders/emu-substryk-seq-f00400_20260527-225827.png" alt="Gameplay — enemies appearing in the sky over the same scene" width="256"/>
</p>

```sh
dotnet run --project src/Subterra.Game
```

* **Q / A** — climb / dive (the world only scrolls once you've
  dived deep enough — it's *subterranean*)
* **L** — move horizontally
* **Enter** — fire
* **1 / 2 / 3 / 4** on the title screen pick a control method
  (KEYBOARD / Interface 2 / Kempston / Cursor)
* **Esc** quits

### A complete reverse-engineering toolkit

A `subterra` CLI with 12 sub-commands covering snapshot decoding,
Z80 disassembly, memory inspection, opcode-pattern search, live
emulator runs with scripted key input, RAM dumps, sprite extraction,
and instruction-level execution tracing. The full reference lives in
[**docs/TOOLS.md**](docs/TOOLS.md). Highlights:

* `render-snapshot` and `render-scr` decode Spectrum screens to PNG.
* `disasm` reads any region of a snapshot as Z80 mnemonics.
* `find-bytes` greps for an opcode pattern with wildcards.
* `run-emu` boots the game in the emulator, scripts key presses by
  frame number, and saves the final screen and RAM dump.
* `scrwrite-trace` logs every screen-memory write during a single
  gameplay frame, with PC, address, byte value, and `(x, y)`.
* `sprite-scan` interprets a memory region as a grid of 8 × 8 (or
  any size) cells and writes a contact-sheet PNG for each window.

### Two asset banks already extracted

<p align="center">
  <img src="renders/scan-%24E62B-8x8_20260528-001405.png" alt="21 UDG cave-terrain tiles at address E62B" width="416"/><br/>
  <em>The 21 cave-terrain UDGs at <code>$E62B</code> (8 × 8 each).</em>
</p>

<p align="center">
  <img src="renders/scan-%24B0F4-8x8_20260527-234538.png" alt="Master sprite tile bank at B0F4 — ~390 tiles, including cave walls, trees, buildings, humanoid figures (RESCUED people), projectiles, and HUD letters F U E L plus digits" width="580"/><br/>
  <em>The master 8 × 8 sprite tile bank at <code>$B0F4</code>
  (≈ 390 tiles, decoded with <code>subterra sprite-scan</code>).
  Cave walls, trees, buildings, the "RESCUED" people figures,
  projectiles, and the HUD font (you can spot F-U-E-L in there).</em>
</p>

### An interactive asset viewer

```sh
dotnet run --project src/Subterra.Editor
```

Avalonia GUI that auto-loads either the bundled snapshot or a
post-game RAM dump and lets you scroll through memory at any cell
size. Preset buttons jump to the tile bank, UDGs, music data, etc.
Hovering a cell shows its address and raw bytes; "Save PNG" writes a
clean contact sheet into `renders/`.

---

## How it was built

The story of the reverse engineering — every dead end included — is
in [**docs/RE-LOG.md**](docs/RE-LOG.md). The lookup table of every
named address we've identified is in
[**docs/MEMORY-MAP.md**](docs/MEMORY-MAP.md). The two are kept in
lockstep; every "found new routine" commit touches both.

A few highlights from the journey:

* **The first bug** wasn't in the emulator — it was in the
  Spectrum's interleaved bitmap-address arithmetic, which I got
  wrong on the first try and the title text rendered vertically
  replicated. The fix lives in
  [`SpectrumScreen.BitmapAddress`](src/Subterra.Spectrum/SpectrumScreen.cs).
* **The "ship doesn't move" mystery** was a misunderstanding of the
  game's controls, not a CPU bug. The main loop gates on
  `($E584) ≥ 117` (player altitude), and you have to *dive* before
  the world starts scrolling. Found by walking back from the gate
  with `subterra find-bytes` and `subterra emu-peek` —
  see [RE-LOG §10](docs/RE-LOG.md).
* **The master tile bank at `$B0F4`** was found by tracing back
  from `LD DE,$4000` (the routine writing the *screen* address) to
  the inner draw helper at `$DAF2`, where the indirection
  `index → $B0F4 + index*8` jumped out. See
  [RE-LOG §14](docs/RE-LOG.md).
* **Flicker and colour clash are by design.** Subterranean Stryker
  draws its moving sprites with an XOR routine at `$E1DE` — same
  call erases and draws, with a gap in between. And colour is
  stored in 32×24 attribute cells, so a sprite passing near an
  enemy briefly shares its 8×8 colour. Both are intrinsic
  Spectrum-hardware behaviour, faithfully reproduced. See
  [RE-LOG §12](docs/RE-LOG.md).

---

## Repository layout

```
original/        original 1985 game files + 48 K Spectrum ROM
  tape/          the .tzx tape image
  dumps/         the .z80 snapshot + SCR loading screen
  rom/           48k.rom (with provenance note)
src/             everything we wrote, one .NET 10 solution
  Subterra.Spectrum/   snapshot loader, Z80 CPU, ULA, screen, PNG
  Subterra.Assets/     SpriteSheet decoder, RenderedImage
  Subterra.Tools/      the `subterra` CLI — 12 sub-commands
  Subterra.Game/       Avalonia window — playable emulator
  Subterra.Editor/     Avalonia asset viewer
docs/
  RE-LOG.md      the running notebook (read top-to-bottom)
  MEMORY-MAP.md  every named address, organised by RAM region
  TOOLS.md       every tool with what / why / how-to
assets/extracted/
  tiles-b0f4.bin first standalone asset file (3 KB tile bank)
renders/         timestamped PNG history of every render the
                 project has ever produced — kept forever as a
                 visual changelog
```

---

## Quick-start

Requires **.NET 10 SDK**. No other tooling required.

```sh
# 1. Build the whole solution
dotnet build SubterraneanStryker.slnx

# 2. Play the original game in our emulator
dotnet run --project src/Subterra.Game

# 3. Browse memory / assets with the GUI viewer
dotnet run --project src/Subterra.Editor

# 4. Use the CLI — print all available commands
dotnet run --project src/Subterra.Tools -- --help

# Examples of CLI use ------------------------------------------------

# Render the original loading screen
dotnet run --project src/Subterra.Tools -- \
    render-scr original/dumps/SCRSHOT/SUBSTRYK.SCR

# Render the snapshot's screen memory (= title screen)
dotnet run --project src/Subterra.Tools -- \
    render-snapshot original/dumps/SUBSTRYK.Z80

# Disassemble the main game entry point
dotnet run --project src/Subterra.Tools -- \
    disasm original/dumps/SUBSTRYK.Z80 F5FD 30

# Boot the game, drive it via scripted keys, dump RAM for the Editor
dotnet run --project src/Subterra.Tools -- \
    run-emu original/rom/48k.rom original/dumps/SUBSTRYK.Z80 600 \
    -keys=5-10:SPACE,40-50:1,200-500:A \
    -ram=build/post-game.bin

# Extract the in-game UDG cave tiles from the RAM dump
dotnet run --project src/Subterra.Tools -- \
    sprite-scan build/post-game.bin E62B E700 8x8 \
    -cols=8 -count=21 -scale=6
```

See [docs/TOOLS.md](docs/TOOLS.md) for the full reference of every
CLI command, every public class in the runtime library, and every
GUI feature.

---

## Roadmap

Known open follow-ups, parked rather than blocking:

* **Sprite composition tables.** The `$B0F4` tile bank is the
  *vocabulary*; each enemy sprite is composed from a small list of
  tile indices. `subterra scrwrite-trace` already captures the byte
  stream — next step is to walk the streams across a frame and
  match them against the bank to label each game object.
* **Map / level data viewer.** Once we know the level format, add
  a "map view" tab to `Subterra.Editor` that lets you scroll a
  level and edit individual tile cells.
* **Spectrum beeper audio.** Capture toggles of bit 4 of port
  `$FE`, convert to PCM samples, and pipe through Avalonia audio so
  Tim Follin's title tune is preserved.
* **Native game logic.** Eventually replace the emulator core with
  a hand-written C# implementation of each game routine, using the
  extracted asset files. The emulator stays in the repo as a
  reference and for any routines we don't fully understand yet.

---

## Legal

The original 1985 game is © Insight Software. The tape image, the
snapshot, and the loading screen under [`original/`](original/) are
widely mirrored across preservation archives (World of Spectrum,
Spectrum Computing). Nothing here is sold, and the binaries are kept
only for reverse engineering and historical preservation. Insight
Software (or any current rights holder) can ask for the binaries to
be removed at any time.

The 48 K Spectrum ROM under [`original/rom/`](original/rom/) is
© Amstrad plc, redistributed under [Amstrad's 1999 permission grant
for non-commercial use](original/rom/README.md).

All hand-written code (tools, runtime library, GUI apps,
documentation) is licensed under the MIT License —
see [`LICENSE`](LICENSE).

— John Knipper, `<code@jkn.me>`
