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

Not yet ported.  Native plays its own SDL beep effects via
`SfxQueue` triggered at gameplay events; the cassette's Follin
player isn't emulated.  Adding it requires a tick driver that
consumes the message data at the same cadence as the cassette
($FA32 driven by the Z80 R-register through speaker toggles).

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
