# Sound/music — `$FA32` Follin player + the VESTIGIAL message system

**Headline (full `$FA32` disasm, RE-LOG §63):** the cassette's
only music is the title tune.  `$5E88` is pure DATA — a stream of
16-bit word pairs (duration, pitch) terminated by a first-byte
`$FF` — and `$FA32` is the player: a synchronous, DI'd loop that
plays the whole stream, exiting on the terminator or on ANY
keypress (`$FA96 IN A,($FE)` across all rows).  That keypress
exit is why title music only sounds while M or N is held: the
`$F637` gate decides whether to CALL the player, and touching any
key makes it return so the menu stays responsive.

The "message" SFX family (`$F8B4` fuel-low, `$F8D8` shield-low,
`$F8F9` boss alert, `$F90E` fuel pickup, `$F93A`, `$F974`
game-over, `$F99F` per-level fanfares) queues bytes into the
`$FF51` buffer via `$FA0A` — **but nothing in the binary ever
plays that buffer.**  `$FA32` RESETS the pointer (`$FA36 LD
($FF54),HL`) and reads only the `$5E88` stream; an exhaustive
search finds no other reader of `($FF54)` except the `$F8A8`
pending-check, and no alternate player entry (`$FA3D`/`$FA36`/
`$FA47` have zero callers).  `$F93A` has no callers at all.  The
whole message system is vestigial — development leftovers.  In
the real game: no boss-alert jingle, no pickup chime, no
fanfares, no game-over tune, no kill jingle.  In-game audio is
exclusively the direct OUT routines.

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

## `$FA32` — the Follin player, FULLY DECODED

```
FA32  LD IX,$5E88            ; IX = tune data stream
FA36  LD HL,$FF51
FA39  LD ($FF54),HL          ; ★ RESET message ptr (= discard any queued msg)
FA3C  DI
; per note:
FA3D  L,H = (IX+0),(IX+1)    ; HL = note DURATION (16-bit countdown)
FA43  IX += 2
FA47  LD DE,$00FF            ; D = low-phase width, E = high-phase width
; per pulse cycle:
FA4A  OUT 0; wait D           ; speaker low for D DJNZ counts
FA51  OUT $10; wait E         ; speaker high for E counts
FA58  BC = (IX+0),(IX+1)      ; PITCH word — busy-wait countdown
FA5E  DEC BC until 0
FA63  DEC HL; JR Z,$FA96     ; duration exhausted → next note
FA68  INC E; DEC D            ; ★ THE FOLLIN SLIDE: duty shifts 1 count
FA6A  JR NZ,$FA4A             ;   per cycle; at D=0 the mirrored half
FA6C..FA94                    ;   ($FA6E..) slides back the other way
; note end:
FA96  IN A,($FE) all rows
FA9A  AND $1F; JR NZ,$FAA9   ; ★ ANY key pressed → EXIT player
FA9E  IX += 2                 ; skip the pitch word
FAA2  LD A,(IX+0); CP $FF    ; first byte $FF = tune terminator
FAA7  JR NZ,$FA3D             ; else next (duration, pitch) pair
FAA9  EI; LD IX,$0000; RET
```

**Data format of `$5E88`:** a flat stream of 16-bit little-endian
word PAIRS — `(duration, pitch)` — terminated by a pair whose
first byte is `$FF`.  Pitch = the busy-wait count between speaker
toggles (bigger = lower note); duration = how many pulse cycles
the note lasts.  The famous Follin timbre is the `INC E / DEC D`
at `$FA68`: the square wave's duty cycle slides continuously
across each note while the total period stays put — phasing on a
1-bit speaker.

The player is SYNCHRONOUS (DI'd) and runs until the terminator
or any keypress.  No interrupts, no per-frame ticking, no
second voice, no message consumption.

## The message system is VESTIGIAL

`$FA0A` reverse-copies `B` bytes from `HL` into `$FF51` downward
and stores the end pointer at `($FF54)`; `$F9F9` saves
HL/DE/BC/AF to `$FA2A..$FA30` and `$FA19` restores them.  The
entries:

| Routine | Caller | Bytes | Data | Intended purpose (never heard) |
| ------- | ------ | ----- | ---- | ------------------------------ |
| `$F8F9` | `$EC26` boss spawn | 11 | `$F904` | boss alert |
| `$F90E` | `$DFE8` fuel-station refill | 9 | `$F919` | pickup chime |
| `$F93A` | — NO CALLERS — | 13 | `$F945` | (dead code) |
| `$F8B4` | (none found) | 19 | `$F8C5` | fuel-low alert |
| `$F8D8` | (none found) | 16 | `$F8E9` | shield-low alert |
| `$F958/$F962` | `$EA09` laser kill | 9 | `$F96A` | kill jingle |
| `$F974` | `$D8B5` game-over | 32 | `$F97F` | game-over tune |
| `$F99F` | `$F72E` level load | 11 | `$F9B8`[level−1] | per-level fanfare |

But `($FF54)` has exactly ONE reader in the whole binary — the
`$F8A8` pending-check — and `$FA32` clobbers the pointer on
entry.  No alternate player entry point has any caller.  **The
queued messages are never played.**  Either the player lost its
message mode late in development, or the dispatch was never
finished.  The message bytes presumably use the same
(duration, pitch) word-pair format; pointing a future
re-implementation at them would let us hear the eight sounds the
game never played — an archaeology project, not a porting need.

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

### Resampler — area sampling (duty-cycle averaging)

`BeeperRecorder.RenderPcm(startCycle, endCycle, sampleRate, amplitude)`
computes each output sample as the **time-weighted average** of
the beeper level over that sample's CPU-cycle window.  Walking
edges in lockstep with samples: for each output sample, sum
`highCycles` over its window, then output
`amplitude · (2·highCycles/sampleSpan − 1)` — duty 1 → +amp,
duty 0 → −amp, duty 0.5 → 0.

This is a perfect box-filter low-pass: anything above the
Nyquist frequency that would otherwise alias as harsh distortion
gets averaged out.  Tim Follin's pulse-width-modulation trick
still works because we preserve the duty cycle of every sample
window — a window that's 70% HIGH and 30% LOW comes out at
+0.4·amp, exactly what the Spectrum's natural speaker low-pass
would produce.

(An earlier version used **nearest-neighbor sampling** — each
sample = the latest beeper level at that cycle, no averaging.
The cassette's Follin player tops 10 kHz easily and pushes
significant content above Nyquist; nearest-neighbor aliased all
of that into the audible range as harsh distortion.  The user
described it as "stressful but not random" — exactly the
signature of structured-but-aliased audio.  Area sampling fixed
it.)

### Sinks

| Sink | Location | How |
| ---- | -------- | --- |
| WAV file (offline)        | [`Subterra.Spectrum.WavWriter`](../../src/Subterra.Spectrum/WavWriter.cs) + `subterra run-emu -wav=<path>` | Renders the full cycle range `[0, Cpu.Cycles)` to mono 16-bit PCM at 44.1 kHz (default) and writes a RIFF/WAVE file. |
| Live audio (Avalonia EMU) | [`Sdl2Audio`](../../src/Subterra.Game/Sdl2Audio.cs) + `MainWindow.OnTick` push loop | Per-frame: render only `[lastPcmCycle, Cpu.Cycles)` so RenderPcm doesn't redo work; `SDL_QueueAudio` pushes into the device's internal queue; throttle kicks in only at **≥ 500 ms backlog** so we don't drop chunks mid-tune (earlier 200 ms threshold caused audible gaps); edges older than ~2 sec are trimmed.  SDL2 audio runs in its own thread but we never share the recorder across threads — all the rendering happens on the UI thread that owns the emulator.  **M key** toggles the queueing on/off; the keypress also forwards to the Spectrum keyboard so the cassette's `$F637` title-music gate (`IN A,$7FFE; AND $0C`) still sees the M press it needs. |
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
SFX live.  Status bar shows `audio=ON` or `audio=OFF` and
`(M toggles)`.

### Title-music gate — `$F637`

Cassette code at `$F637..$F63D` reads port `$7FFE`, masks bits 2
and 3 (M and N keys on row 7), and `RET Z` if neither is pressed.
The player calls at `$F64E` / `$F65D` (and `$F641 CALL $F973`
which is actually a `RET` no-op) live AFTER that gate.  And the
player itself ($FA32, above) EXITS on any keypress — so the
complete behaviour is: press M/N to start the tune, and it plays
until you touch any key (including releasing into another press).
The synchronous DI'd player would otherwise freeze the menu; the
keypress poll at `$FA96` is what keeps the title responsive.  The
Avalonia emulator's **M key** doubles as both the audio toggle
AND a forwarded Spectrum M keypress — hold M at the title to hear
the intro music; tap M during gameplay to mute.

### Per-effect capture — `subterra sfx-render` + the native SfxWavBank

[`SfxRenderCommand`](../../src/Subterra.Tools/SfxRenderCommand.cs)
runs each cassette sound routine in ISOLATION inside the emulator
and captures the beeper to
`assets/extracted/sfx/<name>.wav` (mono 16-bit PCM @ 22 050 Hz =
the native audio device rate, so playback is 1:1):

- Harness: fresh 600-frame boot per effect, then a sentinel-return
  call single-stepped to completion.
- Real captures: **hit** ($DDC4 click), **barfill** ($E419),
  **spawnin** ($E135) — the direct OUT routines — plus
  **titletune** (12 s of `$FA32` playing `$5E88`, captured by
  `run-emu -wav-from` with M held).
- **History/correction:** an earlier version of the tool also
  "captured" the `$F8xx` queued entries by driving `$FA32` after
  each — those WAVs were all the TITLE TUNE at different sample
  phases (verified by phase-insensitive zero-crossing comparison)
  because `$FA32` ignores the message buffer entirely.  All ten
  bogus files were purged; the Effects table now carries the
  explanation.  Also corrected on the way: `$DC43` ("descending
  whine" in death.md) contains NO `OUT` — it's the silent
  screen-dim loop.

Native playback: `SfxWavBank` (Core) loads the WAVs;
`BeeperSynth.PlayPcm` plays them with priority over the synth
tone; `Sdl2Runner` maps Hit/Damage → `hit.wav`; every other
SfxKind uses the PORT-ONLY synth tones (the cassette plays
nothing for those events — see the vestigial-message verdict
above).

### Follin player port — decision

With `$FA32` fully decoded, a C# port is now TRACTABLE (the
player is ~120 bytes; `music-5e88.bin` is a flat
(duration, pitch) word stream; the PWM slide is two INC/DECs).
But it would only reproduce the one tune the game has — which
`titletune.wav` already delivers byte-faithfully through the
capture pipeline.  Decision: not ported; the format documentation
above is sufficient for anyone who wants to hear the eight
never-played message sounds someday.
