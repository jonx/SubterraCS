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

See [`docs/RE-LOG.md`](docs/RE-LOG.md) for the running notebook.

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
original 1985 binary inside it. Controls:

* **A** / **Q** — dive / climb the submarine
* **Space** — fire
* **Enter** — confirm menus
* **1 / 2 / 3 / 4** — pick a control option on the title screen
  (the game asks for KEYBOARD / Interface 2 / Kempston / Cursor)
* **Esc** — quit

Tip: the game is *subterranean* — the world only scrolls once you
have dived deep enough. Hold **A** for about two seconds to start
descending; you'll see DEPTH tick up and the cave roll past.

## Quick start

```sh
dotnet build SubterraneanStryker.slnx

# render the loading screen and the title-screen snapshot to renders/
dotnet run --project src/Subterra.Tools -- render-scr original/dumps/SCRSHOT/SUBSTRYK.SCR
dotnet run --project src/Subterra.Tools -- render-snapshot original/dumps/SUBSTRYK.Z80

# dump the 48K RAM image from the snapshot
dotnet run --project src/Subterra.Tools -- unz80 \
    original/dumps/SUBSTRYK.Z80 build/substryk-ram.bin
```

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
