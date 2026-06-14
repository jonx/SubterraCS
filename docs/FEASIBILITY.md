# Feasibility — is a full C# port realistic?

> **Status (2026):** The port described here has been built — see
> [`native/README.md`](../native/README.md) for the current state.
> Main risks resolved: (1) per-enemy AI ported as `EntityAI.cs`
> (table-driven, faithful to each type's documented behaviour but not
> a byte-for-byte Z80 translation); (2) sound effects captured as
> WAVs via `sfx-render`; the Follin message system decoded and the
> eight lost sounds reconstructed + unlockable via **N**
> ([CURIOSITIES.md §2](CURIOSITIES.md)).  This file is preserved as
> the original pre-port assessment.

**Short answer: yes.** *Subterranean Stryker* is small enough,
regular enough, and now well-enough mapped that a faithful native
C# port is realistic — not a "decade-long fan project", more like
a focused 2-4 week effort once we commit. This file is the honest
breakdown of *why*, and where the remaining risk lives.

## What "ported" means

Three increasingly ambitious definitions, and how this project
relates to each:

1. **Emulated.** The original Z80 binary runs inside our
   hand-written CPU + 48 K Spectrum host. Cross-platform, plays
   identically to the 1985 release. **Already shipped.**
   See [`Subterra.Game`](../src/Subterra.Game/).
2. **Hybrid.** Native C# game loop replaces the Z80 main loop,
   but reuses extracted assets (tile bank, sprite banks, level
   schedules, music data) byte-for-byte. The look and feel of
   the original is preserved exactly; the underlying machine is
   modern. *This is the realistic target.*
3. **Re-imagined.** Fresh art, audio, mechanics — Subterranean
   Stryker as a *new* game in the same spirit. Out of scope for
   this project but a natural next step.

The rest of this document is about **target 2**.

## How big is the game, really?

Counts come from the work in [`docs/MEMORY-MAP.md`](MEMORY-MAP.md):

| Component                          | Size | Comment |
| ---------------------------------- | ----:| ------- |
| Music data (`$5E88`+)              | ~4 KB | flat 16-bit period stream for Follin tune |
| Master tile bank (`$B0F4`)         | ~3 KB | 390 × 8 bytes (8×8 cells) |
| Entity sprite banks (`$B8F4`+)     | ~8 KB | 16 types × 16 frames × 32 bytes |
| Player sprite (`$E63B`)            | 48 B | 2 directional frames + explosion patterns |
| Cave UDGs (`$E62B`)                | 168 B | 21 × 8-byte UDGs |
| Entity-type table (`$F5A0`)        | 64 B | 16 types × 4 bytes (ptr, frames, attr) |
| Per-level pointers + speed         | 18 B | `$E56D`, `$E57C`, `$E58B` × 6 levels |
| Per-level spawn schedule (`$E69D`) | 192 B | 6 levels × 32 bytes (8 spawns each) |
| **Total data**                     | **~15 KB** | |
| Game code ($5E88..$FFFF less data) | ~10 KB | bounded by the 48 K RAM minus the above |

**~25 KB of "intent".** A morning's reading once it's properly
disassembled. We've already named every major code region.

## What we already have in C#

Out of the work described in this repo:

* **A full Z80 emulator + 48 K Spectrum host** (`Subterra.Spectrum`)
  — the reference oracle for any "does the port behave the same?"
  test. Run-in-parallel diffing is trivial.
* **A snapshot loader + screen decoder + PNG writer** — already
  cross-platform, zero deps.
* **A disassembler** with full prefix coverage — we can produce
  annotated listings of every game routine on demand.
* **Asset extractors** for the tile bank, UDGs, entity banks,
  player frames, bullets — outputs raw `.bin` per asset and
  RGBA PNGs for review.
* **Two Avalonia GUIs** — the playable Game (emulator-wrapped)
  and the asset Editor.
* **Tracing tools** — `scrwrite-trace`, `tile-trace`, `emu-peek`
  — for verifying any new native code matches the original on a
  per-frame basis.

## What the port would actually involve

Estimating in person-days. These are *focused* days, assuming
you already know the Spectrum tricks; multiply by 2-3 for casual
time.

| Module                                  | Days | Risk | Notes |
| --------------------------------------- | ---:| --- | ----- |
| **Renderer**                            | 0.5 | low  | We already convert Spectrum bitmap+attrs to RGBA. Native game writes into a logical screen buffer using the same address arithmetic; the existing decoder draws it. |
| **Tile / sprite blitters**              | 1   | low  | Port the four primitives at `$DAF2` (indexed tile), `$E03D` (overwrite), `$F2BC` (16×16 quadrant blit), `$DCF5` (player XOR) as C# methods. Same data flows in. |
| **Entity dispatcher**                   | 1   | low  | The 4-frame time-slice + `$F5A0` type lookup is ~30 lines of C#. |
| **Player movement + altitude gate**     | 0.5 | low  | `$E584` ≥ `$75` ⇒ scroll page; INC/DEC per input bit. Tiny. |
| **Spawn-schedule executor**             | 0.5 | low  | 8 timers per level, decrement-and-spawn. Mechanical. |
| **Per-entity AI (16+ types)**           | 3-5 | **medium** | Each enemy type has its own update logic somewhere in the unidentified routines (`$E8FD`, `$DE2A`, etc.). Disassembly + manual translation per type. Time-consuming but not algorithmically hard. |
| **Collision + scoring + rescue**        | 1   | medium | Need to identify the pickup routine and the score-update routine. We have leads (`$DE2A` is the workers/rescue pass). |
| **HUD + UI**                            | 0.5 | low  | Already understood (`$E046` → `$E03D` font copy with ROM font at `$3C00`). Replace with native text rendering. |
| **Beeper audio**                        | 1   | medium | Translate the busy-wait timing in `$FA32` into a sample-stream generator; pipe through Avalonia/SDL audio. The pitch-slide trick is faithfully reproducible at 44.1 kHz with linear interpolation. |
| **Title / menus / game-over**           | 1   | low  | Mostly print routines + key handling. |
| **Avalonia / SDL host refactor**        | 1   | low  | Single window, key input, frame timer, blit RGBA buffer. We have it. |
| **Testing + diff against emulator**     | 2   | low  | `subterra emu-peek` + `scrwrite-trace` against the port to verify positional / pixel parity. |
| **Total**                               | **~14 days** | | |

So somewhere between **two and three weeks of focused work** to a
playable native C# port, with the emulator-based playable already
in hand as the spec.

## What's actually risky

Roughly in declining order:

1. **The per-enemy AI behaviours.** We have the *table* (16
   entity types at `$F5A0`), and the *generic* draw + position
   update routines, but each type's individual logic (how does
   the magenta drone move? how do the stalactites trigger?) is
   spread across `$D7FB`'s phase routines and we haven't fully
   annotated them yet. Disassembling 16 short behaviour
   subroutines is mechanical, but the time adds up. **Largest
   chunk of work.**
2. **The Follin sound effects.** The music player is one routine
   we've identified. Sound effects (shoot, explosion, pickup,
   alarm) are probably *separate* short subroutines hitting the
   same `OUT ($FE),A` mechanism — we need to find each. Less
   data than music but more "find the small routines".
3. **Subtle Z80 / Spectrum quirks.** Our emulator passes the
   game's smoke test (boots, plays, dives), but there may still
   be flag-edge bugs that the *game doesn't exercise* and the
   *port doesn't reproduce*. Low probability, but the safest
   hedge is to keep the emulator around for parallel-run
   verification.
4. **Memory contention timing.** Spectrum ULA contention slows
   the CPU during the screen-rendering window. Our emulator
   ignores this; if it turns out the original game's exact
   per-frame budget depends on contention, we'd need to add it.
   We've seen no evidence so far, but if anything subtle goes
   wrong, this is the first place to look.

## What's *easy*

* **No level data to reverse-engineer.** The level format is
  192 bytes, already decoded. There are no compressed tilemaps,
  no streamed terrain, no scriptable triggers — the world is a
  schedule of timed entity spawns and the entity system does the
  rest. This is the single biggest simplification.
* **Asset extraction is done.** Tile bank, sprite banks, UDGs,
  player frames, music data — all pulled out as raw bins (or
  trivially derivable from `build/post-game.bin` with the tools
  in `subterra`). The native port just `File.ReadAllBytes(…)`
  them at boot.
* **Rendering primitives are tiny.** Four blitters, all <50 lines
  of Z80 each. The C# equivalents are even shorter (no need to
  encode the Spectrum bitmap-address arithmetic in opcodes —
  just compute and copy).
* **No floating-point, no fixed-point fractions.** The whole game
  is 8-bit integer arithmetic with the occasional 16-bit step.
  Trivial in C#.
* **Cross-platform is the default in .NET.** The runtime library
  has *zero* third-party dependencies; Avalonia (or SDL2 if we
  swap it later) is the only thing between the game and the OS.

## Recommended approach

1. **Start with the renderer.** A `GameSurface` class wrapping a
   1-bit-per-pixel buffer + 32×24 attribute buffer, with the
   same address math as `SpectrumScreen`. Port the four blitters
   to write into it. The existing decoder already turns it into
   RGBA. Visual parity from day one.
2. **Add the entity system.** Port `$F1A5` + `$F1EF` + `$F2BC` +
   `$EF02` + `$F6F2`. Drop in the extracted spawn schedule and
   sprite banks. Watch enemies fall.
3. **Add the player.** Port `$DCF5` and the input dispatch.
   Read keys, move sprite, descent gates work.
4. **Iterate on per-type AI.** For each entity type, disassemble
   its specific update path, translate to C#, run side-by-side
   against the emulator, diff. ~1 type per half-day.
5. **Audio last.** Replace the busy-wait pulse generator with a
   sample-stream generator using the same period table. Run
   through Avalonia's `BufferedWaveProvider` or SDL_audio.
6. **Strip the emulator** from the shipping binary (keep it in
   the repo as a reference / verification tool). The native
   port becomes the primary `Subterra.Game`.

This is the same sequence, generalised to *any* Spectrum game and
written as a how-to, in [`PLAYBOOK.md` §6 "Field guide"](PLAYBOOK.md#6-field-guide--porting-your-spectrum-game).
The Playbook also explains the method these estimates assume — boot
the binary and extract from RAM, disassembling only the strategic
routines — which is why the numbers above are weeks, not months.

## The honest answer

A *bit-perfect* re-implementation is unrealistic — the original
makes occasional use of Z80 timing tricks (the music routine
disables interrupts and relies on cycle-accurate pulse widths)
that wouldn't survive a literal translation. But a **faithful**
one — where the game looks, plays, and sounds like the original,
and where any difference is invisible to the eye and ear — is
straightforwardly achievable from where we are. The data has
been recovered. The code has been mapped. What's left is
typing.

— John Knipper, `<code@jkn.me>`
