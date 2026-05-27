# Subterranean Stryker — Reverse Engineering & C# Port

A clean-room-ish reverse engineering project that takes the 1985 ZX
Spectrum game **Subterranean Stryker** (Insight Software) and rebuilds
it as a portable, cross-platform C# game with an accompanying
asset/map viewer/editor.

The project is being open-sourced; everything here — including the
tools used to peel the game apart — is hand-written so that the
provenance of every line of code is clear.

## Goals

1. Recover the original game from its tape image and 48 K snapshot.
2. Build the tooling we need to reverse engineer it (Z80 snapshot
   loader, Z80 disassembler, sprite/map ripper, ...) from scratch.
3. Document the reverse engineering process step by step so anyone
   can follow along.
4. Produce a faithful cross-platform C# re-implementation of the
   game.
5. Ship an asset/map viewer + editor so that the game can be modded
   and extended.

## Status

What works today:

* Hand-written **Z80 emulator** (full documented instruction set,
  CB / ED / DD / FD / DD-CB / FD-CB prefixes, flag-correct ALU).
* **Spectrum 48 host** (16 K ROM + 48 K RAM + ULA port `$FE`,
  IM 0/1/2 interrupts at 50 Hz). 48 K ROM bundled with permission.
* **`.z80` snapshot reader** (v1, v2, v3; RLE decompression).
* **Spectrum screen decoder** → RGBA, plus a hand-rolled **PNG
  writer** (and our own CRC-32) so the asset pipeline has zero
  graphics dependencies.
* **Z80 disassembler** with full prefix handling.
* **Subterra.Game** — Avalonia 12 window playing the original game
  cross-platform on macOS / Linux / Windows.
* **Subterra.Editor** — Avalonia sprite scanner: type an address,
  see decoded cells, hover for raw bytes, save sheets to `renders/`.
* **`subterra` CLI** with 10+ subcommands for poking around the
  binary, the live emulator, and RAM dumps.
* **First asset extracted**: the in-game UDG terrain dictionary at
  `$E62B` (21 × 8×8 cells of cave wall / dust / ground tiles).
* **Master sprite tile bank**: every in-game 8×8 tile (~390 of them
  — cave walls, trees, ships, humanoid figures, projectiles, the
  HUD font, ...) lives flat at `$B0F4`, indexed by the sprite-draw
  routine at `$DAF2`. Saved out as
  [`assets/extracted/tiles-b0f4.bin`](assets/extracted/) and
  visualised in [`renders/scan-$B0F4-8x8…`](renders/).

See [`docs/RE-LOG.md`](docs/RE-LOG.md) for the running notebook and
[`docs/MEMORY-MAP.md`](docs/MEMORY-MAP.md) for every named address
we've identified.

## Layout

```
original/         original game files (tape image, snapshot, loading screen)
src/              all hand-written code (snapshot loader, screen renderer,
                  disassembler, emulator, game, editor — one .NET 10 solution)
tools/            (reserved for future native helper scripts, if any)
renders/          timestamped screenshots/asset dumps — kept forever as
                  a visual changelog of what we've discovered
docs/             reverse engineering log and design notes
```

## Playing it

```sh
dotnet run --project src/Subterra.Game
```

That opens an Avalonia window with our Z80 emulator running the
original 1985 binary inside it. Controls (Spectrum keyboard layout
— in the GUI just press the same letter on your laptop):

* On the title screen, press **1** to pick the KEYBOARD control
  option (the game also offers Interface 2 / Kempston / Cursor on
  2 / 3 / 4 — those work too, just use different keys).
* In game:
  * **Q** — climb (up)
  * **A** — dive (down)
  * **L** — move horizontally
  * **Enter** — fire
* **Esc** quits the window.

Tip: the game is *subterranean* — the world only starts scrolling
once you have dived deep enough. Hold **A** for about two seconds
to start descending; you'll see DEPTH tick up and the cave roll
past.

## Quick start

```sh
dotnet build SubterraneanStryker.slnx

# render the loading screen and the title-screen snapshot to renders/
dotnet run --project src/Subterra.Tools -- render-scr original/dumps/SCRSHOT/SUBSTRYK.SCR
dotnet run --project src/Subterra.Tools -- render-snapshot original/dumps/SUBSTRYK.Z80

# dump the 48K RAM image from the snapshot
dotnet run --project src/Subterra.Tools -- unz80 \
    original/dumps/SUBSTRYK.Z80 build/substryk-ram.bin

# boot the game in our own emulator, save its post-game RAM
dotnet run --project src/Subterra.Tools -- run-emu \
    original/rom/48k.rom original/dumps/SUBSTRYK.Z80 600 \
    -keys=5-10:SPACE,40-50:1,200-500:A \
    -ram=build/post-game.bin

# extract the in-game UDG cave tiles from that dump
dotnet run --project src/Subterra.Tools -- sprite-scan \
    build/post-game.bin E62B E700 8x8 -cols=8 -count=21 -scale=6

# get all subcommands
dotnet run --project src/Subterra.Tools -- --help
```

## Editing assets

```sh
dotnet run --project src/Subterra.Editor
```

Opens the asset viewer with the bundled snapshot pre-loaded. The
preset buttons jump to known interesting addresses (title text,
UDG area, dense code region). Adjust the cell width / height / count
/ columns to interpret memory as sprite cells; hover any cell to
see its address and bytes; click **Save PNG** to drop a contact
sheet into `renders/`.

## Legal

The original 1985 game is © Insight Software. The tape image and
snapshot under [`original/`](original/) are widely mirrored across
preservation archives (World of Spectrum, Spectrum Computing).
Nothing in this repository is sold, and the binaries are kept here
only for the purpose of reverse engineering and historical
preservation. Insight Software (or any current rights holder) can
ask for the binaries to be removed at any time.

All hand-written code (tools, C# port, editor) is licensed under the
MIT License — see [`LICENSE`](LICENSE).
