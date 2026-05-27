# SubterraCS — native C# port

A standalone, **emulator-free** native re-implementation of
*Subterranean Stryker* in C#. Lives alongside the original solution
in this repo but is its own .NET 10 solution
(`native/SubterraCS.slnx`) with no references back into the
emulator-based one.

## Status

* **Renderer**: Spectrum-style 256 × 192 1-bit bitmap + 32 × 24
  attribute grid, decoded to RGBA. All four blitters from
  [`docs/MEMORY-MAP.md`](../docs/MEMORY-MAP.md) ported as C#
  methods: indexed tile copy, 16×16 quadrant blit, player XOR draw,
  single-byte bullet XOR.
* **Game loop**: 50 Hz, identical phase ordering to the original's
  `$D7FB` loop — input → world tick → draw → present.
* **Entities**: 16-slot entity list (`World.Entities`) plus an
  8-slot bullet list — same shape as `$F1B9`/`$E881`. Sprites pulled
  from the extracted entity-bank blob.
* **Player Stryker**: drawn via `Blitters.DrawPlayerXor` from the
  16-byte directional frame loaded from
  `assets/extracted/player-e63b.bin`. Flicker preserved by design.
* **HUD**: hand-built 8 × 8 `MiniFont` (so we don't depend on the
  Spectrum ROM character set). Renders DEPTH / SCORE / RESCUED plus
  shield + fuel bars at the bottom.
* **Procedural level generator**: replaces the 6 hard-coded spawn
  schedules with deterministic-but-varied infinite levels keyed on
  depth. Difficulty curve (timer pressure rises as you dive) plus a
  type-pool that broadens with depth.
* **Audio**: `BeeperSynth` synthesises 16-bit PCM with the Follin
  pulse-width slide trick. SDL2 streams it through the legacy
  callback API.

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
│    ├── SpawnSchedule.cs            8 × 4-byte (timer, type, flags)
│    ├── ProceduralGenerator.cs      infinite levels via seeded RNG
│    ├── World.cs                    full game state + tick + draw
│    ├── Hud.cs + MiniFont.cs        bottom-strip HUD + hand-built 8×8 font
│    ├── GameInput.cs                up/down/horizontal/fire booleans
│    ├── BeeperSynth.cs              Follin-style PCM beeper
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

## What's still proudly missing

The native port deliberately does not yet implement:

* Per-enemy AI behaviours (currently every enemy just falls
  straight down — pleasant but uniform). Plugging in the
  individual AI tables is the natural next step.
* Pickup / rescue mechanic. The original collects workers when
  bullets miss; we render them but don't track them yet.
* Sound effects beyond the chirp-on-score and dive-blip
  hooks. The music data is loaded into memory but not yet driven.

These are documented in [`docs/FEASIBILITY.md`](../docs/FEASIBILITY.md)
as the medium-effort items on the day-by-day port plan.
