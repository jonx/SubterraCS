# Project history — the chronicle

This is the curated timeline of the project: what happened, in what
order, and — more interestingly — **what we got wrong and how each
wrong turn was caught**.  The day-by-day notebook with every dead
end is [RE-LOG.md](RE-LOG.md) (66 sections; each phase below links
into it); the method the project converged on is distilled in
[PLAYBOOK.md](PLAYBOOK.md).  145 commits, May 27 → July 6, 2026.

If the RE-LOG is the lab notebook, this page is the story you'd
tell a colleague over coffee.

---

## Phase 1 — Bootstrap: a machine to interrogate (May 27)

One day, from nothing to a running 1985 game:

- **Snapshot loader + screen renderer** — decode the `.z80` file,
  render `.scr` screens to PNG.  First light.
- **Z80 disassembler** — hand-written, sharing its decoder with
  what would become the emulator (so listings can never disagree
  with execution).
- **Working Z80 CPU + 48 K Spectrum host** — the game boots, runs,
  accepts input.  From this moment on, every question about the
  game has an *oracle* ([PLAYBOOK §3](PLAYBOOK.md)).
- First gameplay captures, and the first surprise: *"the ship
  doesn't move"* — resolved as **the game is vertical**; the
  Stryker descends, "subterranean" is literal
  ([RE-LOG §5](RE-LOG.md)).
- **The master tile bank found at `$B0F4`** by tracing backwards
  from screen writes, not by reading the loader — the moment the
  boot-and-extract method proved itself ([RE-LOG §14](RE-LOG.md)).
- Flicker and colour clash documented as **authentic hardware
  behaviour, by design** — to be preserved, not fixed
  ([RE-LOG §12](RE-LOG.md)).

## Phase 2 — Tracing the draw paths (May 27–28)

The instrumentation era: `tile-trace`, `scrwrite-trace`, PC
histograms.  Found the XOR sprite primitive at `$E1DE`, the 2×2
wrapper at `$E1C1`, the three concurrent draw paths, and cracked
the 16×16 entity sprite system.

Also the project's first recorded misidentification: `$B8F4` was
labelled the player sprite; it's actually the **workers' swinging
shovels** ([RE-LOG §17](RE-LOG.md) — including the postscript
admitting the "discovery" wasn't hidden at all).  Two habits date
from this phase: the **doc-hygiene rule** (every discovery lands in
both RE-LOG *and* MEMORY-MAP, same commit) and the practice of
leaving corrections visible instead of rewriting history.

The pre-port stocktake became [FEASIBILITY.md](FEASIBILITY.md):
verdict *yes, weeks not months* — because the method extracts
finished assets instead of decompiling code.

## Phase 3 — The native port is born… twice (May 28)

`native/SubterraCS` — an emulator-free, SDL2, zero-dependency C#
port — was created and immediately went wrong in an instructive
way: the first version was a **demonstrator that invented its own
game**.  Themed procedural levels, a sinusoidal cave corridor,
per-type entity AI with touch-damage rules — none of it from the
cassette.  The user's correction ("reset architecture") produced
the honest-stocktake commit and the tool that kept the port honest
from then on:

> **`diff-frame`** — pixel-diff the emulator (running the real
> binary) against the native port at the same frame.

The rest of the phase is the number going down: **22.47% → 4.06%
(mini-map ported) → 3.23% → 2.50% (bar-fill) → 1.24% → 0.73%**
([RE-LOG §39](RE-LOG.md)).  Each drop is a subsystem ported from
its disassembly instead of imagined: HUD byte-for-byte from
`$E785`/`$E046`/`$E0BE`, entity records from `$F2E8`, the mini-map
strip, the `$DB1A` hillside, lives icons, bar-fill curve.

## Phase 4 — Subsystem sweep (May 28–29)

The per-subsystem disasm files in [disasm/](disasm/) mostly date
from this 48-hour stretch: enemy ships (`$E920` + ~15 helpers),
boss (`$EC10`), workers (`$E75D`), input schemes (`$D8F0`), player
physics, title menu, main loop, level-load, sound.  On the port
side: horizontal scroll (`$DA23`/`$DA62`), laser (`$DE41`), the
damage chain (`$DCF5` XOR-overlap primary trigger + `$DD4D`
instant-death walker — and the **first invincibility artifact
removed**: the cassette has no per-hit cooldown, damages.md),
spawn-in/death particle animations with the real `$E841`/`$E861`
seed tables, fuel station, and the first **accepted port-only
feature**: the Shift pixel-precision modifier
([RE-LOG §55](RE-LOG.md)).  The emulator side gained faithful
beeper audio capture.

## Phase 5 — Corrections season (June 11–12)

Two weeks later, a re-read of the evidence turned several
"conclusions" over.  This phase is why the RE-LOG's rule is
*corrections are content*:

- **Level 0 is a bug in the 1985 game** — its record pointer sits
  3 bytes out of alignment; the 5→0 wrap would draw garbage and
  corrupt RAM.  The port wraps 5→1 ([RE-LOG §57](RE-LOG.md),
  [entities.md](disasm/entities.md)).
- **The laser saga**, in three acts: first "the beam erase/redraw
  brackets prove the laser hits *nothing*" (§58) — then the
  correction: **the laser DOES kill**; the check lives in the
  *targets'* blitter (`$E9F0` finds the `$EF` beam byte under the
  sprite), symmetric with the player's own bitmap-is-collision
  damage ([RE-LOG §62](RE-LOG.md), [laser.md](disasm/laser.md)).
- **`$F1EF` decoded end-to-end: entities never move.**  Every
  "falling rock" is a looping animation in a fixed box.  The
  port's invented per-type AI (movement, lifetimes, AABB damage)
  was deleted ([RE-LOG §61](RE-LOG.md)).
- **The `$FA32` message system is vestigial** — eight composed
  sounds the shipped game never plays.  That discovery became a
  feature: the **lost sounds**, reconstructed and unlockable on
  the N key, plus the Star Wars hall-of-fame easter egg
  ([CURIOSITIES.md](CURIOSITIES.md)).

Also in this phase: authentic captured SFX and title tune, the
Editor's Map tab, Hall-of-Fame attract screen + name entry,
keymap.cfg remapping.

## Phase 6 — Input truth + the Playbook (June 12–14)

A request for AZERTY-friendly keys triggered another empirical
loop: the title menu's scheme table at `$F741` is indexed **in
reverse**, scheme 2 is the 6/7/8/9/0 key set (not a "cursor"
joystick), and — settled with three independent checks — **the
cassette has no horizontal pixel precision** (byte-granular scroll
only; the precision the eye sees is *vertical*, `$DCAC` shifting
sprite data by `altitude & 7`) ([RE-LOG §65](RE-LOG.md),
[scroll-horizontal.md](disasm/scroll-horizontal.md)).  The
documentation pass then produced [PLAYBOOK.md](PLAYBOOK.md) — the
methodology guide and cassette anatomy map — and cross-linked the
whole doc set.

## Phase 7 — The fidelity audit and the Historic/Modern split (July 6)

The reckoning.  A full audit of `native/` against the disasm docs
(four parallel review passes) confirmed that alongside the faithful
pixel plumbing, the port had accumulated **a second, invented rule
set** — an auto level-advance that bypassed the `$F868` dive gate,
laser-vs-decor with invented scores, enemy-ship respawns, a fuel
economy (ambient drain + fuel-death) citing Z80 addresses that
exist in no disassembly, respawn invincibility, in-game music the
cassette never had, and synth SFX in the "cassette-faithful" sound
mode ([RE-LOG §66](RE-LOG.md)).

The remediation established the port's current shape:

- **HISTORIC mode (default)** — cassette rules only, every
  behavioural constant traced to its Z80 routine.  Four contested
  constants were settled by going back to the disassembler: the
  `$D95D` speed ramp caps at 7; the fuel station compares **raw**
  `$E583` (the MEMORY-MAP summary was wrong, the instruction trace
  right); the laser's cyan-default colour was *right in the code
  and wrong in the doc*; the worker "4-frame animation" was an
  invention — `$F0F1` is zeros and only `$F071` is ever drawn.
- **MODERN mode (H key / `--modern`)** — the embellishments,
  deliberately kept and clearly fenced: endless procedural depths
  6+ (the generator reborn — it now emits pages in the cassette's
  own data formats so every faithful subsystem runs unchanged on
  generated caves), laser-vs-decor, ship respawns, fuel pressure,
  respawn grace, in-game music (the real `(duration, pitch)`
  `$5E88` stream), Hall-of-Fame persistence.
- Always-on extras (accepted by decree): Shift precision and the
  N-key sound modes.

And one last discovery, found *while* restoring the boss: the
"speed table" at `$EE84` is **never written by any instruction in
the binary**.  The boss's shifting visual bands are leftover
loader bytes — `B7 ED DB` — **uninitialized memory, shipped in
1985 and drawn on screen ever since**, now faithfully reproduced
([boss.md](disasm/boss.md)).

---

## The lessons, in one place

Each phase re-taught some version of the same four rules — they're
the spine of [PLAYBOOK.md](PLAYBOOK.md):

1. **The binary is the spec; the emulator is the oracle.**  Every
   dispute in this history was settled by running or disassembling
   the original — never by intuition, and (twice) *against* our own
   documentation.
2. **Inventions creep in silently; audits catch them.**  The
   port went "too creative" twice — the sinusoid-cave demonstrator
   (Phase 3) and the game-flow rule set (Phase 7) — and both times
   the fix was the same: diff against the truth, delete or fence
   the fiction.
3. **Corrections are content.**  The shovel mix-up, the laser
   saga, the `$DCAC` mislabel, the "+15" fuel-station error — all
   preserved in the record, because the wrong turn documents why
   the right answer is right.
4. **Fidelity and creativity can coexist — behind a flag.**  The
   end state isn't "no inventions"; it's *labelled* inventions:
   historic by default, modern by choice.

## Where to go next

- [README](../README.md) — what the finished thing looks like
- [PLAYBOOK.md](PLAYBOOK.md) — the method, distilled and reusable
- [RE-LOG.md](RE-LOG.md) — the full notebook behind this chronicle
- [CURIOSITIES.md](CURIOSITIES.md) — the treasures along the way
- [native/README.md](../native/README.md) — the port as it stands
