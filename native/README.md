# SubterraCS — native C# port

A standalone, **emulator-free** native re-implementation of
*Subterranean Stryker* in C#. Lives alongside the original solution
in this repo but is its own .NET 10 solution
(`native/SubterraCS.slnx`) with no references back into the
emulator-based one.

## Status — work in progress, not a port yet

> **An earlier README claimed this was "feature-complete". It isn't.**
> The user pointed at a real emulator screenshot vs. the native
> render and the gap is obvious: the original is open-sky free
> flight + rescue gameplay with a clean hand-drawn hill silhouette
> and left-stacked HUD; my earlier passes invented cave walls,
> a "dive to advance" mechanic, and a bottom-strip HUD that have
> nothing to do with the original. See
> [`docs/RE-LOG.md`](../docs/RE-LOG.md) §23 for the honest stocktake
> and §24 for what we've actually traced so far.

What IS correct today
---------------------
* **Boot splash** — `SUBSTRYK.SCR` from the cassette, displayed
  pixel-perfect.
* **Title menu** — captured Spectrum SCREEN$ from running the
  emulator past BASIC PAUSE; pixel-perfect.
* **Gameplay model** — free flight, A/Q vertical + L horizontal,
  no diving. Level completes when all rescuable workers picked up.
* **Asset loading + game loop architecture** — assets → splash
  → title → `LoadLevel(n)` → play → level complete / death.
* **Player sprite + bullet + entity 16×16 quadrant blit** —
  faithful ports of `$DCF5`, `$E1DE`, `$F2BC`.
* **Per-level spawn schedules** — bytes from `$E69D` used
  verbatim (re-scaled timers because we run at straight 50 Hz
  vs. the original's multi-pass slicer).

What is NOT correct yet
-----------------------
* **Per-level scenery** — the hill silhouette, tree, surface
  decor. The native port draws a placeholder pattern from the
  master tile bank. The real composition routine and its data
  table location are not fully understood (RE-LOG §24 documents
  what we DID trace: `$F6F2 → $E319 → $E2C6 → $E2E5 → $E347 →
  ...`).
* **HUD draw** — labels in the right shape but rendered with
  `MiniFont`, not by `RST 10` through the ROM font at `$3C00`.
  The stripe-bar palette and column count match `$E785`.
* **Per-type entity AI** — table-driven approximations in
  `EntityAI.cs`, not byte-for-byte ports of `$F1A5`'s per-type
  subroutines.
* **Static entity placement** — workers and other per-level
  fixed entities are not loaded from the `$F2E8`+ records yet
  because the records are variable-length and undecoded.

* **Renderer**: Spectrum-style 256 × 192 1-bit bitmap + 32 × 24
  attribute grid, decoded to RGBA. All four blitters from
  [`docs/MEMORY-MAP.md`](../docs/MEMORY-MAP.md) ported as C#
  methods: indexed tile copy, 16×16 quadrant blit, player XOR draw,
  single-byte bullet XOR.
* **Game loop**: 50 Hz, identical phase ordering to the original's
  `$D7FB` loop — input → world tick → draw → present. Drives a
  `GameState` machine: Title → Playing → Dying → GameOver →
  Playing.
* **Levels**: the **six original 32-byte schedules** at `$E69D`
  load verbatim from `level-schedules-e69d.bin` (re-scaled for
  our straight 50 Hz tick). Depth 6+ falls through to the
  `ProceduralGenerator` for infinite play.
* **Entity AI**: `EntityAI.cs` is a per-type dispatcher matching
  every kind we decoded — workers walk and are rescuable;
  stalactites cling-wobble-then-drop; rocks drift; drones and
  robots strafe; mine carts and wagons roll; bubbles rise and
  grant fuel; the creature does a slow X-chase; the bow-tie
  sine-drifts; vines and pipes are static decor; etc. Each kind
  has its own `CollisionRule` (damage/heal/score/rescue deltas)
  and its own `ShootScore`. Bullets respect `IsBulletProof` so
  you can't shoot the workers you're meant to rescue.
* **Player Stryker**: drawn via `Blitters.DrawPlayerXor` from the
  16-byte directional frame loaded from
  `assets/extracted/player-e63b.bin`. Flicker preserved by design;
  a temporary flicker also signals the post-respawn invincibility
  window.
* **HUD**: hand-built 8 × 8 `MiniFont`. Renders DEPTH / SCORE /
  RESCUED on row 0, SHIELD + FUEL bars on row 1, plus magenta
  lives chips bottom-right. `MiniFont.DrawCentered` is reused for
  the title and game-over banners.
* **Cave terrain**: `World.CaveHalfWidthAt(y)` is a sinusoid whose
  period shrinks with depth, so deeper caves twist tighter. The
  player takes graze damage when straying outside the safe
  corridor.
* **Sound effects**: `SfxQueue` is a Core-only one-shot queue with
  voices for Fire / Hit / Explode / Pickup / Dive / Damage /
  GameOver / LevelUp. The Platform layer drains it each frame
  into `BeeperSynth.Tone` — Core has zero audio dependency.
* **Music**: `MusicPlayer` walks the 4 KB Follin music stream
  (16-bit period pairs at `$5E88`), one note every 8 frames,
  mapping period → Hz via a normalising divisor. Approximates
  the `$FA32` Z80 driver without re-implementing the pulse-width
  slide loop at sample-accurate timing.
* **Procedural levels** (depth 6+): deterministic-but-varied
  infinite levels keyed on depth, with the difficulty curve
  rising as you dive and the type-pool broadening.

## Layout

```
native/
├── SubterraCS.slnx                  one .NET 10 solution, three projects
├── SubterraCS.Core/                 no third-party dependencies
│    ├── SpectrumPalette.cs          Spectrum 16-colour palette + attr→RGB
│    ├── Framebuffer.cs              256×192 1-bit bitmap + 32×24 attrs
│    ├── Blitters.cs                 the four sprite-draw primitives
│    ├── TileBank.cs                 8×8 master tile bank + UDG bank
│    ├── EntityBank.cs               16-type sprite bank (column-major quadrants)
│    ├── EntityTypes.cs              (ptr, max-frames, attr) per entity type
│    ├── Entities.cs                 EntityInstance + Bullet records
│    ├── EntityAI.cs                 per-kind movement, collision, scoring
│    ├── SpawnSchedule.cs            8 × 4-byte (timer, type, flags)
│    ├── OriginalLevels.cs           load the six original $E69D schedules
│    ├── ProceduralGenerator.cs      infinite levels via seeded RNG (depth 6+)
│    ├── World.cs                    full game state + tick + draw
│    │                                + GameState machine (Title/Play/Die/Over)
│    ├── Hud.cs + MiniFont.cs        bottom-strip HUD + hand-built 8×8 font
│    │                                (incl. centered title/game-over banners)
│    ├── GameInput.cs                up/down/horizontal/fire booleans
│    ├── SoundEffects.cs             SfxKind enum + SfxQueue (Core only)
│    ├── BeeperSynth.cs              Follin-style PCM beeper
│    ├── MusicPlayer.cs              walks 16-bit period pairs from $5E88
│    ├── PngWriter.cs + Crc32        copy of the dependency-free PNG encoder
│    ├── RenderTarget.cs             "renders/" path + repo-root walk-up
│    └── AssetLoader.cs              loads assets/extracted/*.bin at boot
├── SubterraCS.Platform/             SDL2 only — hand-rolled P/Invokes
│    ├── Sdl2.cs                     P/Invokes + custom DllImportResolver
│    ├── Sdl2Window.cs               window + streaming texture + letterbox
│    ├── Sdl2InputPump.cs            keyboard → GameInput
│    ├── Sdl2BeeperAudio.cs          SDL_OpenAudio callback → BeeperSynth
│    └── Sdl2Time.cs                 public façade over GetTicks/Delay
└── SubterraCS.Game/                 the executable
     ├── Program.cs                  entry point + argument parsing
     ├── HeadlessTestRunner.cs       --headless mode, dumps renders/
     └── Sdl2Runner.cs               interactive SDL2 mode
```

## Running

```sh
# Headless smoke test — drops frames into renders/ next to the
# main solution's, sharing the timestamped naming convention.
cd native
dotnet run --project SubterraCS.Game -- --headless \
    --frames=300 --keys=0-300:A --seed=42

# Interactive SDL2 mode — requires libSDL2 installed natively
# (macOS: `brew install sdl2 sdl2_mixer`; Linux: `apt install libsdl2-2.0-0`).
dotnet run --project SubterraCS.Game
```

Controls in interactive mode (same as the emulator-based Game):
**Q / Up** climb, **A / Down** dive, **L / Left / Right** strafe,
**Enter / Space** fire, **P** pause, **F11** fullscreen, **Esc**
quit.

## Dependencies

Just **SDL2** (a single native library). No NuGet packages, no
graphics toolkit, no audio framework — the whole presentation
stack is ~250 lines of P/Invokes plus our own RGBA-to-texture
upload loop.

## Reusing assets from the main solution

The native port reads its assets from
`<repo-root>/assets/extracted/*.bin`, populated by
`subterra extract-all build/post-game.bin` in the main solution.
Re-extracting after a fresh emulator run will refresh the bins;
the native port will pick them up automatically next launch.

## What we deliberately *didn't* port

The native game stands on its own — the deliberate omissions are
of *fidelity*, not *features*:

* **Cycle-accurate Z80 sound driver.** The original `$FA32`
  routine uses a tight `OUT ($FE),A` + DJNZ loop with a Follin
  pulse-width slide. We play the same data through a software
  PCM synth instead. Same notes, different timbre.
* **Strict 6-level loop.** The cassette loops the six pages
  forever; we hand off to the procedural generator past depth 6
  so the game keeps escalating.
* **Z80-accurate movement curves.** Player and entity motion is
  table-driven by `EntityAI.cs`, not byte-for-byte equivalent to
  the original's per-type subroutines around `$F1A5`.

Everything else — title, levels, entity types, rescues, scoring,
shield/fuel/lives, game-over, restart — is in.
