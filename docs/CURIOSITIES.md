# Curiosities — the hidden gems of Subterranean Stryker

Forty years after release, reverse-engineering this 1985 cassette
turned up things nobody knew were in there: an easter egg, the
developers' signatures, eight sounds that were composed but never
played, a level that corrupts memory, and some genuinely clever
engineering.  This file collects them all in one place; each entry
links to the full trace.

## 1. The Star Wars hall of fame — and the developers' signatures

The idle title screen draws a HALL OF FAME (`$FCDB`) whose default
high-score table at `$FE0F` reads:

| Rank | Name | Score |
| ---- | ---- | ----- |
| 1 | somebody | 2900 |
| 2 | **Wedge** | 2820 |
| 3 | **Biggs** | 2422 |
| 4 | John D. | 1402 |
| 5 | **Luke** | 488 |
| 6 | **Porkins** | 487 |
| 7 | **Timothy** | 442 |
| 8 | **Gof** | 240 |

Wedge Antilles, Biggs Darklighter, Luke, Porkins — *Star Wars Red
Squadron*, hiding in the data since 1985.  And the last two
entries are almost certainly the developers signing their work:
**Timothy** = Tim Follin (music) and **Gof** = Peter Gough (code).
"John D." remains unidentified — if you know who he is, open an
issue.  ([title-menu.md](disasm/title-menu.md), RE-LOG §59)

The native port ships this exact table as its default
(`HallOfFame.cs`) — beat 240 points and you knock Gof off his own
scoreboard.

## 2. The eight sounds the game never plays

The ROM contains a complete family of sound-effect *messages* —
boss alert, pickup chime, a warning, fuel-low and shield-low
alerts, a kill jingle, a 32-byte game-over tune, and five
per-level fanfares.  The game dutifully QUEUES them at the right
moments (`$FA0A` copies them into a buffer at `$FF51`)… and then
**no code anywhere ever plays that buffer**.  The `$FA32` player
resets the buffer pointer on entry and only ever plays the title
tune; one of the eight (`$F93A`) isn't even queued by anything —
it has no callers at all.  The whole system is a development
leftover: either the player lost its message mode late in
development, or the dispatch was never finished.
([sound.md](disasm/sound.md), RE-LOG §63)

**We reconstructed them.**  The message bytes aren't in the title
player's (duration, pitch) format — they're groups of pitch bytes
separated by `$03` (clearest in the game-over data:
`1B 58 03 | 58 58 03 | 18 18 03 …`), with values in exactly the
title player's pitch range.  `LostSoundReconstructor.cs` renders
each message through `$FA32`'s pulse-cycle engine (same DJNZ
timing, same Follin duty-slide) with documented assumptions for
the parts that never shipped (note length, rest length).  The
results live in `assets/extracted/sfx/lost-*.wav`.

**Hear them in the game:** press **N** in the native port to
toggle *Lost Sounds* mode.  Default is OFF (faithful — those
events are silent on the cassette); ON maps each reconstruction
to the event the original code queued it for: boss spawn, fuel
station, low-fuel/low-shield warnings, laser kills, game over,
and the per-level fanfares.  Forty years late, but they play.

## 3. Level 0 is a bug

The per-level entity-record pointer table has six entries, but
level 0's pointer sits **3 bytes before level 1's records** —
its six "records" are level 1's bytes read out of phase, giving
sprite addresses in ROM and stray RAM writes (`$A0xx`).  Its
scenery pointer is just as broken: it points at the tile bank
itself.  Level 0 is unreachable in normal play (the level counter
increments before the first page); only finishing level 5 — 25
entities, the hardest page — exposes it, which is presumably why
it shipped untested.  The original would draw garbage and corrupt
memory; the port wraps 5 → 1 instead.
([entities.md](disasm/entities.md) §Level 0, RE-LOG §57)

## 4. The bitmap IS the collision system

Nothing in this game ever compares coordinates for damage:

- The **player** takes damage when his XOR-draw lands on non-zero
  pixels (`$DCF5`'s shadow-carry trick — and the spawn routine
  `$F891` blanks the player's spawn cell precisely so frame one
  can't false-trigger it).
- **Enemy ships and the boss** die when their own draw finds the
  laser beam pattern `$EF` in the bytes they're about to cover
  (`$E9F0`).  The laser never checks anything; the *targets* do.
- We got this wrong once ("the laser hits nothing") before finding
  the check hiding in the targets' blitter — the correction story
  and the lesson (*absence of one opcode pattern is not absence of
  a behaviour*) are in RE-LOG §58/§62.

([damages.md](disasm/damages.md), [laser.md](disasm/laser.md))

## 5. The boss has no sprite

The boss's sprite-source pointer aims at `$EE8E` — **its own
state block**.  What you see on screen is the boss's current
speed byte (written twice per tick) plus neighbouring state
values, rendered as horizontal bands that shift as its speed
phase cycles.  A procedural glitch-creature, zero bytes of
artwork.  ([boss.md](disasm/boss.md) §Visual)

## 6. Entities never move — and four more small gems

- **All cave entities are stationary.**  Every "falling rock" and
  "flying drone" is a 16-frame animation inside a fixed 16×16
  box; the only byte the engine ever writes back to an entity
  record is its frame counter.  (RE-LOG §61)
- **The score-parity twinkle.**  Entity types ≥ `$13` swap their
  sprite pointer to `$4800` — the middle of *screen memory* —
  whenever bit 0 of the score is clear, sampling whatever pixels
  happen to be there: a pseudo-random shimmer with zero bytes of
  state.  ([entities.md](disasm/entities.md) §`$F239`)
- **Title music stops when you touch the keyboard** — by design:
  the player is a synchronous, interrupts-off loop, and its
  any-key poll is the only way the menu stays responsive.  M or N
  starts it; any key exits it.  ([sound.md](disasm/sound.md))
- **The Follin timbre is two instructions.**  The famous PWM
  slide is literally `INC E / DEC D` once per pulse cycle —
  duty sweeps while the period holds.  ([sound.md](disasm/sound.md))
- **The 8th ship slot.**  Each level's init data contains eight
  ship records; every loop in the game walks seven.  Level 1's
  unused 8th ship (`X=$50 Y=$58`, alive flag set) has been
  waiting to spawn since 1985.  (RE-LOG §59)
