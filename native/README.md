# SubterraCS — native C# port

A standalone, **emulator-free** native re-implementation of
*Subterranean Stryker* in C#. Lives alongside the original solution
in this repo but is its own .NET 10 solution
(`native/SubterraCS.slnx`) with no references back into the
emulator-based one.

## Historic vs Modern — the one switch that matters

Since the fidelity audit ([RE-LOG §66](../docs/RE-LOG.md)) the port
has a single mode flag, `World.ModernMode` (**H** key in-game, or
`--modern` on the command line):

* **HISTORIC (default)** — cassette rules only, as reverse-engineered
  in [docs/disasm/](../docs/disasm/): rescue all 8 workers to unlock
  the exit, then **dive to the bottom** to advance (+1000, the
  `$F868` gate); fuel drains only while L is held and empty fuel just
  stops the scroll; ships never respawn; the laser (4 slots, no
  cooldown, tail-recede) kills only ships (+15) and the boss (+20);
  no invincibility anywhere; death is the 4×64-pass particle burst
  plus the `$DC43` screen dim, silent; the only in-game sounds are
  the cassette's real three ($DDC4 hit, $E419 bar-fill, $E135
  spawn-in); levels wrap 5 → 1 (avoiding the cassette's level-0
  memory-corruption bug).  Two always-on extras ride on top by
  design: the **Shift** pixel-precision moves and the **N**-key
  sound modes.
* **MODERN** — the port-only embellishments, all in one bundle:
  **endless procedural depths past level 5**, laser-vs-decor with
  scores, enemy-ship respawns, ambient fuel drain + fuel-death
  (survival pressure), respawn/level-start invincibility grace,
  low-fuel/low-shield warnings, in-game music (the real `$5E88`
  Follin data), Hall-of-Fame name entry + persistence to
  `hiscores.cfg`.

Procedural pages (depth 6+) are generated **in the cassette's own
data formats** — a 4 KB tile buffer, `$E69D`-format worker records,
`$E48D`-format ship records, the `$E58B` station pair, the `$E57B`
colour byte — so the faithful subsystems run unchanged on generated
caves.  Every page guarantees open dive shafts (the `$F868` gate
stays passable), eight rescuable workers on real floor, and a fuel
station in an open pocket marked by an electric-arc beacon.

## What's faithfully ported (spot checks welcome)

* **Renderer**: Spectrum-style 256 × 192 1-bit bitmap + 32 × 24
  attribute grid; the four blitters (indexed tile copy, 16×16
  quadrant blit, player XOR draw, single-byte bullet XOR).
* **Progression**: `$F868` dive gate; `$E77D` cleared flags;
  +50/rescue, +1000/page.
* **Damage model**: `$DCF5` XOR pixel-overlap primary trigger,
  `$DDC4` accumulator (SUB $40, no cooldown), `$DD8C`/`$DDAA`
  instant-death walkers (ships {p, p−1}, bullets {p, p+1},
  boss included), two-column `$DFAF` wall death.
* **Movement**: `$D95D` ramp capped at 7 (max 3 px/frame), byte
  scroll `$DA23`/`$DA62`, fuel drain `$D8D8` (L-held only).
* **Ships**: `$E48D` init, 7 slots, every-other-frame `$E920` AI,
  `LD A,R`-placed fire gates, no respawns (historic).
* **Boss**: full `$EC82..$ECE6` body — mod-12 cycle, `$EE7F`/`$EE80`
  direction persistence, throttle until 10 spawns, and the real
  "sprite": the `$EE8E` state block `[$7E, spd, spd, $7E]` whose
  bands cycle through **uninitialized memory** (`$EE84` is never
  written — B7/ED/DB are leftover loader bytes; see
  [boss.md](../docs/disasm/boss.md)).
* **Workers**: `$E69D` records, `$EFAE` 4×3 pickup zone, bit-5 →
  bit-7 freeze frame, faithful OVERWRITE blit of the single `$F071`
  sprite (`$F0F1` is zeros — there is no worker animation),
  flashing mini-map dots via the `$F070` bit-2 gate.
* **Laser**: 4 slots, no cooldown, full span at fire (self-limited
  at scenery per `$DEDA`), tail recede, `$E9F0`-style span hits,
  `$DEC3` random bright ink with the cyan default.
* **Death/respawn**: 4×64 particle passes ($E861 seeds) + `$DC43`
  dim; respawn chain per `$F6D4..$F6EF` (ships reloaded, bullets +
  boss cleared, `$E419` bar refill, slide-in + spawn-in replay,
  eternal decor untouched).
* **HUD**: ROM-font labels, `$E785` bar stripes, depth digit at
  column 7, `$E104` mini-map on every level start/respawn.
* **Audio**: the three real cassette sounds as captured WAVs
  (`hit`, `barfill`, `spawnin`) in every mode; everything else
  silent in Off, mapped to `sfx-*.wav`/`lost-*.wav` in
  Designed/Historical.

## Known fidelity gaps

* **Per-level scenery composition** (hill silhouette, tree) — the
  `$F6F2` chain is partially traced; the port paints the level from
  the master tile bank via the mini-map buffer instead.
* **HUD attribute flash** — the `$E046` R-register colour cycle over
  the label cells is not ported (static colours).
* **Title menu** — the captured menu screen is blitted rather than
  re-rendering the print stream; FIRE also starts the game
  (harness-friendly) in addition to the faithful 1–5 keys.
* **Timbre** — WAV captures + a PCM synth stand in for the
  cycle-accurate `OUT ($FE)` loops.  Same notes, different timbre.

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
│    ├── EntityAI.cs                 $F1EF animate-only tick (+ modern laser tables)
│    ├── ProceduralGenerator.cs      modern depth-6+ pages in cassette data formats
│    ├── LevelScroll.cs              $DB1A slide-in + $DA23/$DA62 scroll paint
│    ├── MiniMap.cs                  per-level 16×256 tile buffers + bottom strip
│    ├── LevelEntities.cs            $F2E8 per-level placement records
│    ├── EnemyShips.cs               $E920 ship AI + BossEntity ($EC10, full port)
│    ├── EnemyBullets.cs             $EE9E 6-slot bullet table ($ED01 tick)
│    ├── WorkerSchedule.cs           $E75D workers + $EFAE pickup zone
│    ├── Explosion.cs                $DBC8 death + $E135 spawn-in particles
│    ├── RomFont.cs + ScreenLoader.cs ROM glyphs + .scr blitting
│    ├── World.cs                    game state + tick + draw + Historic/Modern flag
│    ├── Hud.cs + MiniFont.cs        HUD (ROM font) + 8×8 font for port screens
│    ├── HallOfFame.cs               $FCDB table (+ modern name-entry/persistence)
│    ├── GameInput.cs                up/down/horizontal/fire booleans
│    ├── SoundEffects.cs             SfxKind enum + SfxQueue (Core only)
│    ├── SfxWavBank.cs               loads + caches PCM WAVs from assets/extracted/sfx/
│    ├── BeeperSynth.cs              PCM beeper
│    ├── MusicPlayer.cs              (duration, pitch) $5E88 stream — modern only
│    ├── PngWriter.cs + Crc32        dependency-free PNG encoder
│    ├── RenderTarget.cs             "renders/" path + repo-root walk-up
│    └── AssetLoader.cs              loads assets/extracted/*.bin at boot
├── SubterraCS.Platform/             SDL2 only — hand-rolled P/Invokes
│    ├── Sdl2.cs                     P/Invokes + custom DllImportResolver
│    ├── Sdl2Window.cs               window + streaming texture + letterbox
│    ├── Sdl2InputPump.cs            keyboard → GameInput (+ M/N music gate)
│    ├── KeyMap.cs                   user-configurable key bindings + keymap.cfg
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
    --frames=700 "--keys=20-25:FIRE,40-45:2,120-700:A" --seed=42

# Jump straight into a level (e.g. a modern procedural page):
dotnet run --project SubterraCS.Game -- --headless --modern \
    --level=7 --frames=400 --seed=42

# Interactive SDL2 mode — requires libSDL2 installed natively
# (macOS: `brew install sdl2`; Linux: `apt install libsdl2-2.0-0`).
dotnet run --project SubterraCS.Game            # historic
dotnet run --project SubterraCS.Game -- --modern
```

Controls in interactive mode:

- **Q / Up** — thrust up · **A / Down** — thrust down
- **L** — scroll horizontally in current facing · **Left / Right** — face and scroll
- **Enter / Space** — fire
- **Shift** (always-on extra) — precision modifier: one pixel per
  key-edge instead of the acceleration ramp
- **1–5** on the title screen — pick control option and start
- **H** — toggle HISTORIC ↔ MODERN (see above)
- **N** — cycle the sound mode for the events the cassette left
  silent ([CURIOSITIES.md §2](../docs/CURIOSITIES.md)):
  **OFF** (cassette-faithful — only the real three sounds) →
  **DESIGNED** (purpose-built `sfx-*.wav`) →
  **HISTORICAL** (1985 `lost-*.wav` reconstructions)
- **M or N held on the title** — plays the Follin title tune, the
  `$F637` gate exactly as on the cassette (modern mode loops it
  without the key)
- **K** — in-game key bindings screen (arrows select, Enter rebinds,
  Esc/K saves; persists to `keymap.cfg`)
- **P** pause · **F11** fullscreen · **Esc** quit

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
