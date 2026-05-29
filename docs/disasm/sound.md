# Sound/music — `$FA0A` print-stream + `$5E88` Follin player

The cassette uses a Tim Follin beeper player at `$5E88..~$6FFF`,
called via a "print-stream"-style interface at `$FA0A`.  Short
SFX (boss alert, fuel pickup, warning) are encoded as compact
byte sequences that `$FA0A` reverse-copies into a `$FF51` working
buffer for the player to consume.

## `$FA0A` — message dispatch

```
FA0A  LD DE,$FF51                  ; DE = working buffer (top)
FA0D  DI
FA0E  LD A,(HL); LD (DE),A
FA10  INC HL; DEC DE                ; reverse-copy
FA12  DJNZ $FA0E                    ; B bytes
FA14  EX DE,HL; LD ($FF54),HL       ; store buffer ptr
FA18  EI
FA19  LD HL,($FA30)                 ; load player state
FA1C  PUSH HL; POP AF                ; AF = state
FA1E..FA29  reload HL/DE/BC from $FA2A..$FA2F (saved Follin regs)
FA29  RET
```

Called with `HL = message data` and `B = message length`.

## `$FA32` — Follin player tick

```
FA32  LD IX,$5E88           ; IX = music data base
FA36  LD HL,$FF51           ; HL = working buffer
...
FA47  LD DE,$00FF
FA4A  LD A,$00; OUT ($FE),A ; speaker low
FA4E  LD B,D; DJNZ $FA4F    ; delay loop
FA51  LD A,$10; OUT ($FE),A ; speaker high
FA55  LD B,E; DJNZ $FA56    ; delay loop
FA58..  consume next byte from IX; loop
```

A standard XOR-FE square-wave generator with timing controlled
by bytes consumed from `$5E88+`.  The 6 KB region at `$5E88..$6FFF`
is the music/SFX data for the whole game.

## Short SFX entries

Three known callers of `$FA0A` with hard-coded message tables:

| Routine | Caller | Length | Data ptr | Purpose |
| ------- | ------ | ------ | -------- | ------- |
| `$F8F9` | `$EC10` boss spawn | 11 bytes | `$F904` | Boss alert |
| `$F90E` | `$DFAF` fuel pickup | 9 bytes  | `$F919` | Fuel-pickup chime |
| `$F93A` | `$E920` (path `$E97A`) | 13 bytes | `$F945` | Density warning? |
| `$F8B4` | `$D879` fuel-low | 19 bytes | `$F8C5` | Fuel-low alert |
| `$F8D8` | `$D88A` shield-low | 16 bytes | `$F8E9` | Shield-low alert |

Each calls `CALL $F9F9` (probably "stop current SFX") before
loading `HL = data ptr; B = length; JP $FA0A`.

## Message data bytes (verified)

```
$F8F9 → $F904: 77 3A 37 33 03 18 2D 33 0D 03 CD
$F90E → $F919: 67 13 28 31 1F 2D 0C 2C 03
$F93A → $F945: 17 17 3E 03 14 2D 13 07 0B 37 03 21 13
```

These are NOT ASCII text — they're Follin SFX opcodes (each pair
likely encodes (pitch, duration)).  Decoding into actual audible
sounds requires a Follin-player port, not yet ported.

## C# port status

**Beeper-faithful audio is now live in the EMU runtime.**  The
cassette's whole sound system — Follin player, every SFX entry,
the loading-screen tape sounds — comes through bit 4 of the
value written to port `$FE`.  Capturing every transition of that
bit with its CPU-cycle stamp and resampling to PCM produces a
byte-faithful reproduction without re-implementing the Follin
engine; the cassette's own code is doing the work, we just
record what it emits.

### Capture

[`Subterra.Spectrum.BeeperRecorder`](../../src/Subterra.Spectrum/BeeperRecorder.cs)
keeps an edge log `(cycle, high)` populated from
`Spectrum48.WritePort` whenever a write hits port `$FE`.  Edges
are coalesced — only transitions are stored — so the Follin
pulse-width trick at `$FA47..$FA56` (which can hammer the port
every few T-states) doesn't bloat the log.

### Resampler

`BeeperRecorder.RenderPcm(startCycle, endCycle, sampleRate, amplitude)`
walks samples in lockstep with edges: at sample i (cycle =
`startCycle + i * 3.5MHz / sampleRate`) it advances the edge
cursor up to that cycle and outputs `±amplitude` based on the
current beeper level.  Pure square wave — no anti-aliasing, no
low-pass filter — matching the actual ULA hardware which can
only drive the speaker fully HIGH or fully LOW.  Tim Follin's
PWM trick relies on exactly that.

### Sinks

| Sink | Location | How |
| ---- | -------- | --- |
| WAV file (offline)        | [`Subterra.Spectrum.WavWriter`](../../src/Subterra.Spectrum/WavWriter.cs) + `subterra run-emu -wav=<path>` | Renders the full cycle range `[0, Cpu.Cycles)` to mono 16-bit PCM at 44.1 kHz (default) and writes a RIFF/WAVE file. |
| Live audio (Avalonia EMU) | [`Sdl2Audio`](../../src/Subterra.Game/Sdl2Audio.cs) + `MainWindow.OnTick` push loop | Per-frame: render only `[lastPcmCycle, Cpu.Cycles)` so RenderPcm doesn't redo work; `SDL_QueueAudio` pushes into the device's internal queue; the queue is throttled to ≤200 ms backlog; edges older than ~2 sec are trimmed.  SDL2 audio runs in its own thread but we never share the recorder across threads — all the rendering happens on the UI thread that owns the emulator. |
| Live audio (native SDL2 runner) | [`Sdl2BeeperAudio`](../../native/SubterraCS.Platform/Sdl2BeeperAudio.cs) + `BeeperSynth` | The native port doesn't run the cassette so there's nothing to capture from; instead `SfxQueue` triggers a per-effect synth.  Predates the Subterra.Spectrum capture work above. |

### Example

```sh
dotnet run --project src/Subterra.Tools -- run-emu \
    original/rom/48k.rom original/dumps/SUBSTRYK.Z80 800 \
    -keys="10-50:ENTER,80-110:1" \
    -wav=renders/cassette-boot.wav
# → 6757 edges, 704651 samples @ 44100 Hz, ~16s of audio
```

Or just run the Avalonia game with `dotnet run --project src/Subterra.Game`
and you'll hear the loading tape squeak + title menu + gameplay
SFX live.  The status bar shows `audio=on` or `audio=off`
depending on whether SDL2 is installed on the system.

### What's NOT done

- **The Follin player itself is NOT separately ported.**  The
  cassette's `$5E88..` code is what synthesises the music; we
  just relay its bit-4 toggles.  A pure C# port of the player
  (so the native runner could play the same music without the
  Z80 emulator) would consume the `music-5e88.bin` tune stream
  and produce the same edge sequence directly — that's a
  separate, much bigger task and probably not worth doing.
- **The native runner's `SfxQueue`** still uses its own
  synthesised effects (fire / hit / damage / pickup / explode).
  Bringing the cassette's SFX bytes through the same beeper
  recorder path would require either embedding the Z80 emu in
  the native port OR re-implementing the Follin player.  Out of
  scope here.

## Longer tunes — `$F974` / `$F99F`

Two longer entries that dispatch `$F97F`-style 32-byte messages
plus per-level lookups:

- **`$F974`** — game-over tune.  Called ONLY from `$D8B5` (= the
  lives-reached-zero path inside `$D8A8`):
  ```
  F974  CALL $F9F9
  F977  LD HL,$F97F; LD B,$20    ; 32-byte message at $F97F
  F97C  JP $FA0A
  ```
  Hard-coded fanfare.

- **`$F99F`** — per-level fanfare.  Called from `$F72E` (= inside
  the level-load chain `$F6F2`):
  ```
  F99F  CALL $F9F9
  F9A2  LD DE,($E587)            ; level (low byte)
  F9A6  DEC E; LD D,$00; SLA E    ; (level - 1) * 2 = word index
  F9AB  LD HL,$F9B8; ADD HL,DE
  F9AF  LD E,(HL); INC HL; LD D,(HL); EX DE,HL  ; HL = $F9B8[level - 1]
  F9B3  LD B,$0B                  ; 11 bytes
  F9B5  JP $FA0A
  ```
  Table at `$F9B8` of 6 pointers to 11-byte per-level fanfares.

## Related

- `$F9F9` — probably the "stop current SFX" / state-reset routine
  called before each `$FA0A` dispatch.
