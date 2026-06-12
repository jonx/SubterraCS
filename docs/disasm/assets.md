# Assets — every static byte the port pulls from the cassette

This file inventories everything in `assets/extracted/`: where
each blob lives in the cassette's 48 K RAM image, its exact
byte layout, what code consumes it, and which port class
reads it.

The extractor is `subterra extract-all <ram.bin>`
([src/Subterra.Tools/ExtractAllCommand.cs](../../src/Subterra.Tools/ExtractAllCommand.cs)),
fed with `build/post-game.bin` (a 48 K RAM image captured from
the running emulator after the game has booted past the title
menu, so the relocated music + per-level tables are populated).
Some assets (the captured `.scr` screenshots, the ROM font, the
per-level entity records, and the level mini-map buffers) are
extracted by separate one-shot commands listed below.

## At a glance

| File | Cassette `$addr` | Size | Loader | Purpose |
| ---- | ---------------- | ---- | ------ | ------- |
| [music-5e88.bin](#music-5e88bin)                   | `$5E88`  | 4096 | `World` (raw bytes)             | Follin music engine + tune data |
| [tiles-b0f4.bin](#tiles-b0f4bin)                   | `$B0F4`  | 3072 | `TileBank`                      | 384 × 8×8 scenery tiles |
| [entity-banks-b8f4.bin](#entity-banks-b8f4bin)     | `$B8F4`  | 7680 | `EntityBank`                    | 15 entity-type frame banks (16 frames × 16×16 each) |
| [udgs-e62b.bin](#udgs-e62bbin)                     | `$E62B`  |  168 | `UdgBank`                       | 21 user-defined cave glyphs (8×8) used by HUD print |
| [player-e63b.bin](#player-e63bbin)                 | `$E63B`  |   96 | `World.PlayerSpriteRight/Left` (first 32 bytes)  | Stryker right/left sprites + post-bank effects |
| [entity-types-f5a0.bin](#entity-types-f5a0bin)     | `$F5A0`  |   92 | `EntityTypeTable`               | (sprite-ptr, frame-count, attr) × 23 entity types |
| [level-spriteptr-e56d.bin](#level-spriteptr-e56dbin) | `$E56D` | 12   | (info only — mapping hardcoded in `MiniMap.SelectLevel`) | 6 × pointer to per-level tile-index buffer (`$B0F4`, `$60F4`, `$70F4`, `$80F4`, `$90F4`, `$A0F4`) |
| [level-speed-e57c.bin](#level-speed-e57cbin)       | `$E57C`  |  6   | `World.LevelColourData` → `Scroll.LevelColour` per `LoadLevel` | 6 × per-level cave-colour attribute byte (`$07 $04 $03 $06 $02 $01`) |
| [fuel-stations-e58b.bin](#fuel-stations-e58bbin)   | `$E58B`  | 12   | `World.FuelStationData`         | 6 × (X, Y) fuel-station position per level |
| [level-init-e48d.bin](#level-init-e48dbin)         | `$E48D`  | 192  | `EnemyShips.LoadFromInit`       | 6 × 32 bytes of enemy-ship init data (LDIR'd to `$E597`) |
| [level-schedules-e69d.bin](#level-schedules-e69dbin) | `$E69D` | 192  | `OriginalLevels` + `WorkerScheduleData` | 6 × 32 bytes of worker / spawn schedule |
| [level-entities-f2e8.bin](#level-entities-f2e8bin) | `$F2E2..$F2E8+` | 654 | `LevelEntities`              | 6-byte count header + 8-byte System-A entity records per level |
| [level-minimaps.bin](#level-minimapsbin)           | `$60F4`+ stride `$1000` | 24576 | `MiniMap`                | 6 × 4096-byte per-level mini-map / tile-index buffers |
| [rom-font.bin](#rom-fontbin)                       | ROM `$3D00`     |  768 | `RomFont`                       | Spectrum ROM 8×8 character set `$20..$7F` |
| [splash-scr.bin](#splash-scrbin)                   | screen capture  | 6912 | `World.SplashScr`               | Loading-screen `.scr` dump (6144 bitmap + 768 attr) |
| [title-menu-scr.bin](#title-menu-scrbin)           | screen capture  | 6912 | `World.TitleMenuScr`            | "SELECT CONTROL OPTION" menu `.scr` dump |
| sfx/*.wav (4 real: hit, barfill, spawnin, titletune) | beeper capture | ~700 KB | `SfxWavBank` → `BeeperSynth.PlayPcm` | The cassette's REAL sounds (direct OUT routines + the title tune) rendered by `subterra sfx-render` / `run-emu -wav-from`.  The `$F8xx` "message" SFX are vestigial — never played by the original — see [sound.md](sound.md) |
| sfx/lost-*.wav (12 reconstructions)                | reconstruction  | ~400 KB | `SfxWavBank` (N-key Lost Sounds mode) | The never-played `$F8xx` messages rendered via `LostSoundReconstructor` (documented assumptions — [sound.md](sound.md) §lost, [CURIOSITIES.md](../CURIOSITIES.md) §2) |

Total: 17 files, ~55 KB on disk.  Roughly 60% of the cassette's
RAM footprint after game-init.

---

## music-5e88.bin

- **Cassette source:** `$5E88..$6E87` (4096 bytes)
- **What it is:** the Follin music player relocates itself out
  of the loader into `$5E88+` at boot.  The blob contains both
  the player code AND the tune-byte stream it walks.
- **Format:** opaque to the port — we don't interpret it.  See
  [sound.md](sound.md) for what's known of the player's pulse-
  width-slide trick.
- **Port loader:** `World.cs` field `MusicData = File.ReadAllBytes(...)`.
  Currently informational only; the port plays SFX via its own
  synth, not the Follin tune.
- **Why captured:** future port of the Follin player; comparing
  pitch-slide curves between cassette and reimplementation.

## tiles-b0f4.bin

- **Cassette source:** `$B0F4..$BCF3` (3072 bytes = 384 × 8 bytes)
- **What it is:** the master tile bank for cave scenery.  Each
  tile is 8 bytes (one Spectrum char cell, 8×8 mono).  Indexed
  by the per-level mini-map buffers.
- **Format:** `tile[N] = Data[N*8 .. N*8+7]`.  Bit 7 = leftmost
  pixel of the byte (standard Spectrum orientation).
- **Consumers:**
  - `$DAF2` tile blit (see [level-paint.md](level-paint.md))
    `LD HL,$B0F4; ADD HL,index*8; LDIR 8 bytes → bitmap`.
  - `$DB1A` outer loop walks all 32 cols × 16 rows of the
    active level's tile-index buffer, blitting each with `$DAF2`.
- **Port loader:** `TileBank.cs` — wraps `byte[]`, exposes
  `this[int index] → ReadOnlySpan<byte>` 8 bytes.
- **See also:** [`renders/scan-$B0F4-8x8_*`](../../renders/) for
  the visual catalog.

## entity-banks-b8f4.bin

- **Cassette source:** `$B8F4..$D6F3` (7680 bytes = 15 banks × 512 bytes)
- **What it is:** the 15 entity-type sprite banks.  Each bank is
  16 frames × 32 bytes/frame = 512 bytes.  Each 32-byte frame is
  a 16×16 sprite stored as 4 columns × 8 rows (column-major:
  `[TL 8 bytes][TR 8 bytes][BL 8 bytes][BR 8 bytes]`).
- **Pointed into by:** `$F5A0` entity-type table (= `entity-types-f5a0.bin`)
  — each entry has a 16-bit `SpritePointer` pointing somewhere
  inside this blob.
- **Consumers:** `$F1EF` per-entity draw + `$F2BC` 16×16 blit
  ([entities.md](entities.md)).
- **Port loader:** `EntityBank.cs` — `Frame(ushort typePointer, int frameIndex)`
  returns 32 bytes (`baseOffset = typePointer - $B8F4`).
- **Notes:** the bank covers workers, drones, rocks, lava, flame
  drips, vines, creatures, bubbles, force fields, pipes, bowties,
  robots, electric arc, sparks, explosion.  The Stryker player
  sprite is NOT here — it has its own bank at `$E63B`.

## udgs-e62b.bin

- **Cassette source:** `$E62B..$E6D2` (168 bytes = 21 × 8 bytes)
- **What it is:** 21 User-Defined Graphics (UDG) chars used by
  the HUD print path through `RST $10`.  These are the cave-themed
  glyphs (rock textures, mini-map borders, depth markers) that the
  ROM font doesn't supply.
- **Format:** same as `TileBank` — 8 bytes per glyph, indexed.
- **Why offset is `$E62B`:** the game sets `STKBOT ($5C7B)` to
  `$E62B`, claiming `$E62B..$FFFF` for itself; UDGs land at the
  start of that block per Spectrum convention.
- **Port loader:** `UdgBank.cs` (= `TileBank` with a different
  pointer).

## player-e63b.bin

- **Cassette source:** `$E63B..$E69A` (96 bytes)
- **Layout:**
  - `[0..15]`  = Stryker right-facing 16×8 sprite (2 cols × 8 rows).
  - `[16..31]` = Stryker left-facing 16×8 sprite.
  - `[32..95]` = various post-bank effects bytes (laser stages,
    bus-counter scratch — used by `$DCAC` bank-shifter and the
    laser stages at `$E80F`).  See [player.md](player.md) +
    [laser.md](laser.md).
- **Consumers:** `$E3F4` stages the correct-facing sprite into
  `$E8A9` for `$DCF5` to XOR-draw.
- **Port loader:** `World.cs` slices `[0..16] → PlayerSpriteRight`,
  `[16..32] → PlayerSpriteLeft`.  Bytes 32+ are not yet exposed.
- **Crucial:** the BOTTOM 16 bytes of each 32-byte 4-quadrant
  sprite are intentionally ZERO — the Stryker is 16 × 8, not
  16 × 16.  This affects the `$DD22 AND A; JR Z` skip-transparent
  optimization in `$DCF5` and the XOR-collision math
  ([damages.md](damages.md)).

## entity-types-f5a0.bin

- **Cassette source:** `$F5A0..$F5FB` (92 bytes = 23 × 4 bytes)
- **Format per entry:**
  - `+0..+1` little-endian 16-bit `SpritePointer` (into the
    `$B8F4` bank above).
  - `+2`     `MaxFrames` (animation cycle length, usually 16).
  - `+3`     `Attribute` byte (Spectrum INK/PAPER/BRIGHT/FLASH
    nibbles).
- **Consumer:** `$F1EF` looks up `IY = $F5A0 + type*4` per entity
  to find the sprite + colour.
- **Port loader:** `EntityTypeTable.cs` — reads until a pointer
  leaves the `$4000..$DFFF` plausible-RAM range (catches the
  drift past type 22 into unrelated bytes).
- **Decoded types:** Worker, Lava, Stalactite, FallingRock, Drone,
  MineCart, Wagon, Sparks, Explosion, FlameDrip, Vine, Creature,
  Bubble, ForceField, Pipe, Bowtie, Robot, ElectricArc, plus a
  few less-clearly-identified ones.  See `EntityAI.Kind` in the
  port.

## level-spriteptr-e56d.bin

- **Cassette source:** `$E56D..$E578` (12 bytes = 6 × 2-byte LE pointers)
- **What it is:** per-level pointer to that level's
  tile-index buffer.  Bytes decode to:
  ```
  L0  $B0F4  ← actually points at the master TILE BANK (not a
              4 KB index buffer) — part of the original's level-0
              data bug; see [entities.md §Level 0](entities.md)
  L1  $60F4
  L2  $70F4
  L3  $80F4
  L4  $90F4
  L5  $A0F4
  ```
- **Consumer:** `$DB1A` reads `($E56D + level*2)` to find the
  base of the per-level index buffer it iterates ([level-paint.md](level-paint.md)).
- **Port:** info-only.  The same mapping is hardcoded inside
  `MiniMap.SelectLevel` (which switches `MiniMap.Buffer` to
  `PerLevelBuffers[level]`).  The file is kept so future code
  can validate the addresses match.

## level-speed-e57c.bin

- **Cassette source:** `$E57C..$E581` (6 bytes = 1 byte per level)
- **What it is:** per-level **cave-colour** attribute byte.  Loaded
  into `$E57B` (active colour) at level start by
  `$F706 LD A,(HL); LD ($E57B),A`.  Despite the file name (a
  legacy guess of "speed"), the bytes are colour attributes:
  ```
  L0  $07  bright white
  L1  $04  green
  L2  $03  magenta
  L3  $06  yellow
  L4  $02  red
  L5  $01  blue
  ```
- **Consumers:** `$DB6C` scenery-paint attribute, `$DBFC` death-
  animation colour, `$E14A` spawn-in colour, `$E9BE` ship-sprite
  attribute.  Anything that reads `($E57B)` for its INK colour.
- **Port loader:** `World.LevelColourData = File.ReadAllBytes(...)`.
  `World.LoadLevel` applies `Scroll.LevelColour =
  LevelColourData[level % 6]` — before this was a hardcoded
  `0x04` (green) for every level.

## fuel-stations-e58b.bin

- **Cassette source:** `$E58B..$E596` (12 bytes = 6 × (X, Y))
- **Format:** `byte X, byte Y` per level.
- **Consumer:** `$DFCD..$DFEB` (pickup check) compares player
  world-X + altitude against `($E58B + level*2)`.  Match → calls
  `$E419` fuel-refill animation.
- **Port loader:** `World.FuelStationData = File.ReadAllBytes(...)`.
  `World.TickPlaying` does the inline pickup check around line 315.
- **See:** [collision-matrix.md](collision-matrix.md) for the full
  pickup-zone trace.

## level-init-e48d.bin

- **Cassette source:** `$E48D..$E54C` (192 bytes = 6 × 32 bytes)
- **What it is:** per-level enemy-ship init data.  `$E319` LDIRs
  the active level's 32 bytes into `$E597..$E5B6` (the 7-slot
  ship table, stride 4; the 8th record is dead data — every
  consumer loop walks 7 slots and nothing in the binary
  references `$E5B3..$E5B6`).
- **Format per slot (4 bytes):** `(X, Y, Status, Sub)` —
  matches `EnemyShips.Slots[i]` in the port.
- **Consumer:** `$F714 CALL $E319` at level-load.
- **Port loader:** `EnemyShips.LoadFromInit(initData, level)` reads
  `[level*32 .. + 28]` for the 7 ship slots.
- **See:** [ship-ai.md](ship-ai.md), [enemies.md](enemies.md).

## level-schedules-e69d.bin

- **Cassette source:** `$E69D..$E75C` (192 bytes = 6 × 32 bytes)
- **What it is:** per-level worker/spawn schedule.  The original
  game treats this as a script of entity spawns triggered by
  `$E587` (current level) / `$EE74` (scroll progress).
- **Format:** per level, 32 bytes interpreted as a stream of
  `(triggerProgress, typeId, flags)` records — exact stride
  varies by record type.
- **Two consumers in the port:**
  1. `OriginalLevels.Load` parses the bytes into
     `SpawnSchedule[6]` (an `IReadOnlyList<ScheduleEntry>`
     per level) used by the procedural generator's "level <6"
     pass-through.
  2. `World.WorkerScheduleData` keeps the raw bytes for
     `Workers.LoadFromSchedule(...)` to populate the
     `$E75D` worker table.
- **See:** [workers.md](workers.md).

## level-entities-f2e8.bin

- **Cassette source:** `$F2E2..$F2E7` (6-byte count header) +
  `$F2E8..` (variable: 8-byte records).  Total 654 bytes.
- **Format:**
  - `[0..5]`  = `byte counts[level]` — how many records this
    level has.
  - `[6..]`   = records of 8 bytes:
    ```
    +0  Type id (×4 → index into $F5A0 type table)
    +1  Y coordinate (pixel)
    +2  Initial animation frame
    +3..+4  Top bitmap address (LE) — TL char-cell
    +5..+6  Bottom bitmap address (LE) — BL char-cell
    +7  Flags (sprite-flip + behaviour bits)
    ```
- **Consumer:** `$F1BC` per-level loader walks `($F594 + level*2)`
  → record list, populates the live entity table at `$F1B9`.
- **Port loader:** `LevelEntities.cs` — `Record(byte TypeId, byte Y,
  byte Frame, ushort TopAddr, ushort BotAddr, byte Flags)`.
- **See:** [entities.md](entities.md).

## level-minimaps.bin

- **Cassette source:** the six addresses in the `$E56D` pointer
  table, in level order (6 × 4096 bytes = 24576 total).  Verified
  byte-for-byte against a post-game RAM image: slice 0 = `$B0F4`
  (level 0's bogus pointer — the tile bank itself, see
  [entities.md §Level 0](entities.md)), slices 1..5 = `$60F4`,
  `$70F4`, `$80F4`, `$90F4`, `$A0F4` (real levels 1..5).  So
  `PerLevelBuffers[level]` indexes directly by cassette level
  number; slice 0 is garbage by inheritance from the original's
  level-0 data bug and is never selected (the port wraps 5 → 1).
- **What it is:** per-level packed tile-index buffer.  Each
  byte is an index into the master tile bank at `$B0F4`.
  Layout: 16 rows × 256 cols (the WORLD is 256 bytes wide, even
  though the visible window is only 32).
- **Consumer:** `$DB1A` reads via `($E56D + level*2)` →
  `(row × 256) + col` to pull tile indices for paint.  Also used
  as the SCENERY PROBE source (`$EB62` reads the same buffer to
  determine if a position is solid wall).
- **Port loader:** `MiniMap.LoadFromAsset(path)` →
  `PerLevelBuffers[6]`, each a `byte[4096]`.  `MiniMap.Buffer`
  points at the active level's buffer.  Doubles as both the
  scrolling-scenery source (for `LevelScroll.ScrollOneStep`) and
  the mini-map silhouette (via `MiniMap.DrawTo` painting the
  bottom strip at y=160..191).
- **See:** [level-paint.md](level-paint.md), [scroll-horizontal.md](scroll-horizontal.md).

## rom-font.bin

- **Cassette source:** Spectrum ROM `$3D00..$3FFF` (768 bytes =
  96 × 8 bytes for chars `$20..$7F`).
- **What it is:** the Spectrum's stock 8×8 character set.  Used
  by the cassette's HUD via `RST $10` print stream.
- **Why captured:** byte-identical HUD text in the port.  Our
  port can't legally redistribute the Sinclair ROM, but the font
  data alone is a small derivative; the file is loaded from
  `original/rom/48k.rom` by hand.
- **Port loader:** `RomFont.Load(path)` —
  `Glyph(char ch) → ReadOnlySpan<byte>` 8 scanlines.

## splash-scr.bin

- **Cassette source:** screen capture (NOT from a RAM extract).
  A `.scr` file is the Spectrum's native 6912-byte screen format:
  6144 bytes bitmap (`$4000..$57FF`) + 768 bytes attribute
  (`$5800..$5AFF`).
- **What it is:** the SUBTERRANEAN STRYKER loading-screen graphic
  that fills the cassette tape's loader header.
- **Captured by:** running the emulator past the loader, then
  dumping the screen with `subterra render-scr` (the inverse
  of our normal render commands).
- **Port loader:** `World.SplashScr = File.ReadAllBytes(...)`.
  Painted by `ScreenLoader.OverwriteFramebuffer` during the
  `Splash` game state.
- **Fallback:** if missing, the port falls back to a `MiniFont`
  "LOADING" centered string.

## title-menu-scr.bin

- **Cassette source:** screen capture.  Same `.scr` format as
  splash.
- **What it is:** the post-loader "SELECT CONTROL OPTION TO BEGIN"
  menu, captured AFTER the cassette's `$F5FC` title loop has
  painted the layout but BEFORE the player makes a selection.
- **Consumer:** `World.DrawTitle` blits this to the framebuffer
  during the `Title` game state.  The cassette's `$F5FC` would
  normally re-paint this every frame; capturing it once is
  simpler and visually identical for the port.

---

## Extraction protocol

The `subterra extract-all <ram.bin>` command runs a fixed table
([ExtractAllCommand.cs](../../src/Subterra.Tools/ExtractAllCommand.cs))
that handles the bulk of the per-level / per-type assets.  The
remaining files (`splash-scr.bin`, `title-menu-scr.bin`,
`rom-font.bin`, `level-entities-f2e8.bin`, `level-minimaps.bin`,
`level-init-e48d.bin`, `fuel-stations-e58b.bin`) are produced by
one-shot subcommands and ad-hoc Python invocations.

To regenerate the lot:

```sh
# 1.  Boot the emulator past the title menu, dump RAM:
dotnet run --project src/Subterra.Tools -- run-emu \
    original/rom/48k.rom original/dumps/SUBSTRYK.Z80 600 \
    -ram=build/post-game.bin

# 2.  Bulk-extract the addressed assets:
dotnet run --project src/Subterra.Tools -- extract-all build/post-game.bin

# 3.  The screen captures + ROM font + level-entities +
#     level-minimaps need their own commands or are
#     hand-extracted (see git history of assets/extracted/
#     for the exact commits when each appeared).
```

The captured `build/post-game.bin` (= post-init RAM image) is the
canonical source because:
- The Follin player relocates code into `$5E88+` only at boot —
  a pre-boot `.z80` snapshot has it elsewhere.
- The per-level live tables at `$E597`, `$EE7D`, `$EE9E`, etc. are
  zeroed in a pre-game snapshot; the static asset tables we want
  (`$B0F4`, `$F5A0`, `$F2E8`...) are stable in both.

## Cross-cutting addresses (port-mapping summary)

| Cassette region            | Asset file              | Port class                    |
| -------------------------- | ----------------------- | ----------------------------- |
| `$3D00..$3FFF` (ROM)       | `rom-font.bin`          | `RomFont`                     |
| `$5E88..$6E87`             | `music-5e88.bin`        | `World.MusicData`             |
| `$60F4 + level*$1000`      | `level-minimaps.bin`    | `MiniMap`                     |
| `$B0F4..$BCF3`             | `tiles-b0f4.bin`        | `TileBank`                    |
| `$B8F4..$D6F3`             | `entity-banks-b8f4.bin` | `EntityBank`                  |
| `$E48D..$E54C`             | `level-init-e48d.bin`   | `EnemyShips`                  |
| `$E56D..$E578`             | `level-spriteptr-e56d.bin` | (info only — `MiniMap.SelectLevel` hardcodes equivalent mapping) |
| `$E57C..$E581`             | `level-speed-e57c.bin`  | `World.LevelColourData` → `Scroll.LevelColour` |
| `$E58B..$E596`             | `fuel-stations-e58b.bin` | `World.FuelStationData` |
| `$E62B..$E6D2`             | `udgs-e62b.bin`         | `UdgBank`                     |
| `$E63B..$E69A`             | `player-e63b.bin`       | `World.PlayerSpriteRight/Left`|
| `$E69D..$E75C`             | `level-schedules-e69d.bin` | `OriginalLevels` + `Workers` |
| `$F2E2..$F2E7 + records`   | `level-entities-f2e8.bin` | `LevelEntities`             |
| `$F5A0..$F5FB`             | `entity-types-f5a0.bin` | `EntityTypeTable`             |
| screen capture             | `splash-scr.bin`        | `World.SplashScr`             |
| screen capture             | `title-menu-scr.bin`    | `World.TitleMenuScr`          |

Anything NOT in this table is either generated at runtime (the
player's 4-quadrant attribute table at `$E8C9`, the live entity
state at `$E597+`, the spawn-in / death particle scratch at
`$E881`, etc.) or hard-coded in the port (the cassette's
`$E841` / `$E861` particle seed tables, which the port stores
inline in `Explosion.cs`).

## Wired status

| File | Status | Reason if not fully wired |
| ---- | ------ | ------------------------- |
| `tiles-b0f4.bin`             | ✅ wired |  |
| `entity-banks-b8f4.bin`      | ✅ wired |  |
| `udgs-e62b.bin`              | ✅ wired |  |
| `player-e63b.bin` (bytes 0..31) | ✅ wired | right + left sprites only |
| `entity-types-f5a0.bin`      | ✅ wired |  |
| `fuel-stations-e58b.bin`     | ✅ wired |  |
| `level-init-e48d.bin`        | ✅ wired |  |
| `level-schedules-e69d.bin`   | ✅ wired |  |
| `level-entities-f2e8.bin`    | ✅ wired |  |
| `level-minimaps.bin`         | ✅ wired |  |
| `rom-font.bin`               | ✅ wired |  |
| `splash-scr.bin`             | ✅ wired |  |
| `title-menu-scr.bin`         | ✅ wired |  |
| `level-speed-e57c.bin`       | ✅ wired | per-level cave colour (wired in this pass) |
| `level-spriteptr-e56d.bin`   | 🟡 info only | the level→index-buffer mapping is hardcoded in `MiniMap.SelectLevel`; file kept for validation |
| `music-5e88.bin`             | 🟡 loaded, not played | porting the Follin player + tune-stream interpreter is a separate large project (see [sound.md](sound.md)); the port uses its own SFX synth instead |
| `player-e63b.bin` (bytes 32..95) | 🟡 unused tail | post-bank effects (laser-stage RAM, bus-counter scratch); the port uses simpler equivalents in `Explosion.cs` and `World` |
