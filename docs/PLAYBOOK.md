# The Playbook — how to reverse-engineer a ZX Spectrum game and port it to C#

This is the document we wish we'd had on day one. It explains **what
we set out to do**, the **one trick that made all of it tractable**
(run the original binary in an emulator we control, then read the
game's own data straight out of its RAM), **why we built our own
tools instead of leaning on the excellent ones that already exist**,
and — at the end — a **field guide** you can follow to do the same
thing to a different Spectrum game.

If you only read one paragraph, read this one: **we never had to
fully disassemble a 25 KB machine-code game to port it.** We booted
it, played it under instrumentation, watched which bytes it touched,
and pulled its sprites, tiles, levels, and high-score table out of
memory as finished artifacts. Static disassembly was a *scalpel* we
reached for only when a specific behaviour refused to explain itself
— never a *survey* we ran over the whole image. That decision is the
spine of this whole project, and the rest of this page is about how
to make it for yourself.

> **New here?** Start with the [README](../README.md) for what the
> finished thing looks like, then come back. The companion docs are
> [RE-LOG.md](RE-LOG.md) (the day-by-day notebook, dead ends and
> all), [MEMORY-MAP.md](MEMORY-MAP.md) (every named address),
> [TOOLS.md](TOOLS.md) (every command, with what/why/how), and
> [CURIOSITIES.md](CURIOSITIES.md) (the fun stuff we found along the
> way).

---

## 1. The objective — three meanings of "ported"

"Port the game" sounds like one task. It is really three, stacked,
and you can stop at whichever one you want. We named them up front
(see [FEASIBILITY.md](FEASIBILITY.md)) so we'd always know which one
we were working on:

1. **Emulate it.** Boot the original 1985 binary, unmodified, on a
   Z80 CPU + 48 K Spectrum host we wrote ourselves, in a window, on
   macOS / Linux / Windows. The original code runs; we just give it
   a machine to run on. *This is the truth oracle for everything
   else.* If our reimplementation and the emulator disagree, the
   emulator is right.

2. **Understand it.** Know *why* it does what it does — where the
   sprites live, how damage is decided, how the level scrolls, why
   the ship flickers — well enough to write it down and predict it.

3. **Reimagine it.** A from-scratch C# game (`native/`, SDL2, zero
   game-logic dependencies) that reproduces the behaviour without a
   single original byte of Z80 — and is then free to *add* things
   the cassette never had (pixel-precise movement, a writable
   high-score table, the eight unused sounds).

The magic is that **target 1 is the tool that makes targets 2 and 3
cheap.** Most of this document is about exploiting that.

---

## 2. The method — port by *running*, not by *reading*

The naive way to port a machine-code game is to disassemble all of
it, understand every routine, and re-implement each one. For a 25 KB
game that is weeks of work and most of it is wasted: you'd lovingly
decode the tape-loader, the BASIC bootstrap, and the title-music
sequencer, none of which you need.

We did the opposite. The loop was:

```
       ┌─────────────────────────────────────────────────┐
       │  1. Boot the original binary in OUR emulator     │
       │  2. Drive it with scripted key input by frame    │
       │  3. Snapshot the 48 K of RAM at the right moment │
       │  4. Read the game's OWN data structures out of   │
       │     that RAM as finished artifacts               │
       │  5. Disassemble a routine ONLY when step 4 left  │
       │     a "why?" we couldn't answer empirically      │
       └─────────────────────────────────────────────────┘
```

Why this works: a running game has already done all the hard work
for you. The sprites are decoded and sitting in memory. The level
layout has been unpacked. The high-score table is populated. The
current player position, score, fuel, and lives are live variables.
You don't have to *understand* the loader to get the unpacked
tiles — you just have to **be there in memory after it ran**.

Concretely, the moves this unlocks:

* **Capture a RAM dump at a chosen frame.** `run-emu` boots the
  game, presses keys on a schedule (`--keys=120:fire,121:fire`),
  runs to frame *N*, and writes the full 48 K out. That dump
  (`build/post-game.bin`) is then a static file you can disassemble,
  hex-dump, or render — *with the game's runtime state baked in*.
  See [TOOLS.md](TOOLS.md) and [README §Generated files](../README.md).

* **Find data by watching writes, not by reading code.** The master
  tile bank at `$B0F4` wasn't found by reading the loader. It was
  found by tracing *backwards* from the routine writing to screen
  memory (`LD DE,$4000`) to the inner draw helper at `$DAF2`, where
  the `index → $B0F4 + index*8` indirection jumped out.
  (`scrwrite-trace` logs every screen write in a frame with PC,
  address, value, and `(x,y)` — so "what code drew this pixel?"
  becomes a lookup.) See [RE-LOG §14](RE-LOG.md).

* **Confirm behaviour by poking and observing.** Want to know what
  control scheme menu-key 2 selects? Don't trace the whole menu —
  `emu-peek` lets you hold a key and read `$E45F` (the per-frame
  input bitmask) to see exactly which bit lights up. That's how we
  nailed the *reverse-indexed* scheme table and the 6/7/8/9/0 key
  layout — after getting it **wrong twice** by guessing from the
  on-screen menu text. The empirical check is in
  [input.md](disasm/input.md) and [RE-LOG §65](RE-LOG.md).

* **Extract assets as images, not as theories.** `sprite-scan`
  reinterprets any memory region as a grid of cells and writes a
  contact-sheet PNG. You *see* the sprites and recognise them, then
  go find the address that holds them — rather than the reverse.

The throughline: **the binary is the spec, and the emulator is how
you interrogate it.** Disassembly is for the residue — the handful
of decisions (how much damage, which pixel counts as a hit, when the
page advances) that you can only get from the code itself.

---

## 3. Why we built our own emulator and our own tools

There are superb open-source Spectrum emulators and Z80
disassemblers. We used none of them. That looks like
not-invented-here syndrome; it was a deliberate bet that paid off.

**1. The emulator is the oracle — so it must be ours to trust and to
instrument.** Every claim in this project is ultimately checked
against "what does the real code do when it runs?" If the thing that
answers that question is a black box, every disagreement becomes
"is it them or is it me?" Because the CPU and ULA are ours
([Subterra.Spectrum](../src/Subterra.Spectrum/)), we can stop on any
opcode, dump RAM at any frame, log every write to a region, and add
a new probe in minutes. A general-purpose emulator is built to *run*
games, not to be *cross-examined* about one. Our `scrwrite-trace`,
`tile-trace`, `mem-write-trace`, `player-dump`, and `emu-peek`
commands are all "stop the world and tell me exactly what just
happened" — features that only exist because we own the machine.

**2. Owning the tools keeps the port honest and self-contained.**
The project's stated rule is that *a single reader can follow every
line from "load a snapshot" to "render the game"* with no
third-party Spectrum code in the path. A third-party emulator would
import thousands of lines nobody in this repo understands, and a
third-party disassembler would mean our listings and our runtime
could subtly disagree about an opcode and we'd never know. One Z80
implementation, used by both the emulator and the `disasm` command,
means the listing you read is produced by the same decoder that
executes the game.

**3. Limiting third-party *references* (not just code) avoids
poisoning the well.** There are existing maps and crack-intro notes
for some Spectrum games out there. Leaning on them turns a
reverse-engineering project into a transcription project, and it
imports other people's mistakes as if they were facts. We
deliberately worked from the binary, our emulator, and our own
observations — which is *why* we could find things prior write-ups
missed (the Star Wars high-score table, the developers' hidden
signatures, the eight sounds the shipped game never plays). When you
derive everything from the artifact itself, you discover what's
actually there instead of re-confirming what someone said was there.

**4. The tools are small because they're scoped to one game.** A
general toolkit has to handle every edge case. Ours only has to
handle *this cassette*, so each command is a short, legible file you
can read in one sitting. That legibility is the product, not just
the port. See [TOOLS.md](TOOLS.md)'s closing note, "Hand-written by
design."

The cost of this bet is real — you write a Z80 core, an ULA, a
screen decoder, and a PNG encoder before you extract a single
sprite. The payoff is that from then on, *every* question about the
game has a fast, trustworthy, instrument-it-yourself answer. For a
project whose whole point is understanding, that trade is lopsided
in our favour.

---

## 4. The toolkit, organised by the question it answers

[TOOLS.md](TOOLS.md) is the full reference (what / why / how for all
19 commands). Here they are grouped by the question you'd be asking
when you reach for them — which is how you'll actually navigate them
on a new game:

| You want to… | Reach for | Notes |
| --- | --- | --- |
| See the original screens | `render-scr`, `render-snapshot` | `.scr`/snapshot → PNG. First thing to run. |
| Get a running-state RAM dump | `run-emu` | Boot + scripted keys by frame + dump 48 K. The workhorse. |
| Drive the game and read a variable | `emu-peek` | Hold keys, read any address live. Confirms input/state. |
| Read code as Z80 | `disasm` | Same decoder the emulator uses — listing can't drift. |
| Search for an opcode pattern | `find-bytes` | Wildcards. Find every `LD DE,$4000` to locate draw code. |
| Inspect raw bytes | `hex`, `snapshot-info`, `unz80` | Decode `.z80`, dump regions, header facts. |
| Find where a routine returns to | `stack-walk` | Unwind the call stack at a captured PC. |
| Find a sprite/tile bank by eye | `sprite-scan` | Region → contact-sheet PNG. Recognise, then locate. |
| See what code drew a pixel | `scrwrite-trace` | Every screen write in a frame: PC, addr, value, (x,y). |
| See what code touched an address | `mem-write-trace`, `tile-trace` | Watchpoints on RAM, scoped to one frame. |
| Pull a known structure out | `player-dump`, `entity-bank`, `extract-all` | Decode the player/entity/all banks to files. |
| Hear a sound | `sfx-render` | Render the beeper engine's output to audio. |
| Compare two frames | `diff-frame` | What changed between captures. |

The pattern to notice: **most of these are "observe the running
game," not "read the static code."** That ratio *is* the
methodology.

---

## 5. Strategic, not systematic — the disassembly philosophy

We did real disassembly work, and it was not trivial. But we never
disassembled the whole cassette, and that was on purpose.

**The rule: disassemble down the dependency chain from a visible
behaviour, and stop the moment the behaviour is explained.** You
start from something you can see or measure on screen — "the ship
flickers," "the laser kills enemies," "damage drains the shield" —
and you peel back exactly the routines needed to explain *that one
thing*, then you stop. You do not "cover the file." Coverage is a
goal for a decompiler project; we had a game to ship.

What this looks like in practice — a few of the strategic cuts we
made, each its own annotated listing in [disasm/](disasm/):

* **Damage** is XOR-overlap, not coordinate comparison. We chased
  "how does the player take a hit?" into `$DCF5` and found the game
  reads back the pixels its own XOR draw landed on — *the bitmap is
  the collision system*. No `$DDC4` per-hit invincibility either:
  every overlapping frame drains the accumulator. See
  [damages.md](disasm/damages.md), [collision.md](disasm/collision.md),
  [RE-LOG §62](RE-LOG.md).

* **The laser** kills by the *target's* blitter finding the beam
  pattern `$EF` under itself (`$E9F0`), not by the laser checking
  for hits. We got this backwards once ("the laser hits nothing")
  before finding the check hiding in the enemy draw path — a perfect
  example of why you trace from the behaviour, not the name. See
  [laser.md](disasm/laser.md), [RE-LOG §62](RE-LOG.md).

* **Horizontal scroll** is byte-granular only — 8 px per frame via
  `LDIR`/`LDDR`, with *zero* sub-byte shift opcodes anywhere
  (`RL/RR (HL)` = `CB 16` / `CB 1E` appear nowhere). The ship's
  screen X is hard-fixed at columns 15/16. So the cassette *cannot*
  move the ship horizontally at pixel precision — proven by three
  independent checks, not assumed from watching gameplay. See
  [scroll-horizontal.md](disasm/scroll-horizontal.md),
  [RE-LOG §65](RE-LOG.md).

* **Vertical precision** *is* real and lives in `$DCAC`, which
  shifts the *staged sprite data* down `altitude & 7` scanlines
  inside the 16×16 window. (We mislabelled it an "address bank
  shifter" once; the correction is logged.) See
  [player.md](disasm/player.md).

Everything we deliberately **left alone**: the tape loader, the
BASIC bootstrap, the title-music *sequencer* internals (we render
its output rather than re-derive its note format), and large swaths
of `$5E88–$E62A` that we only know as "code/data, block A." None of
it blocked the port, so none of it got disassembled. That's not
laziness; it's the whole point. **A 25 KB game has maybe 3–4 KB of
routines that actually decide its feel. Find those, and you've
understood the game.**

The discipline that keeps this honest: every named routine or table
goes into **both** [RE-LOG.md](RE-LOG.md) (the *why* — the
investigation, including the wrong turns) and
[MEMORY-MAP.md](MEMORY-MAP.md) (the *what* — the address, in its
region), ideally in the same commit. The RE-LOG is narrative and
chronological; re-read it top-to-bottom before opening a new thread,
because a prediction you already made usually collapses the new
question.

---

## 6. Field guide — porting *your* Spectrum game

Here's the sequence, distilled, if you're pointing this method at a
different game. It mirrors the recommended order in
[FEASIBILITY.md](FEASIBILITY.md).

1. **Get the machine running first.** Write (or finish) a Z80 core
   and a 48 K host until the original snapshot boots to its title
   screen. Nothing else is checkable until this works, and from here
   on it's your oracle. Render the loading screen and the snapshot's
   screen RAM to PNG (`render-scr`, `render-snapshot`) as your first
   "it lives" milestone.

2. **Capture running state.** Add scripted input-by-frame and a
   "dump 48 K at frame N" command (`run-emu`). Now you can freeze the
   game at any moment and study it as a file.

3. **Find the assets by eye, then by address.** Sweep memory with a
   contact-sheet renderer (`sprite-scan`). When you recognise a
   sprite, you've found its address. Pull each bank to a standalone
   file (`player-dump`, `entity-bank`, `extract-all`).

4. **Trace the strategic routines, behaviour-first.** Pick one
   visible behaviour at a time (collision, scrolling, scoring).
   Trace it down the dependency chain with watchpoints
   (`scrwrite-trace`, `mem-write-trace`) and the disassembler, and
   stop when it's explained. Write it up in both your RE-LOG and your
   memory-map.

5. **Reimplement in your target language, checking against the
   oracle every step.** Each behaviour you re-create, run it
   side-by-side with the emulator. `diff-frame` makes "do they match?"
   a command, not a vibe.

6. **Strip the emulator last.** Once the from-scratch port
   reproduces the feel, the Z80 layer is only a reference. Keep it in
   the repo as the oracle, but the shipping game doesn't need it.

Two rules that save the most time:
* **Don't disassemble what you can observe.** Always prefer "boot it
  and watch" over "read the code." Disassembly is the fallback for
  the irreducible *why*.
* **Don't trust write-ups, including your own.** Derive from the
  artifact. We were wrong about the control scheme, the laser, and a
  routine's purpose — and only the emulator set us straight.

---

## 7. Anatomy of the cassette — a map of the 48 K image

This is the bird's-eye view: what's *where* in the loaded game, and
which parts we cracked open versus left sealed. For the
address-by-address index, see [MEMORY-MAP.md](MEMORY-MAP.md); each
"strategic" region below links to its annotated listing in
[disasm/](disasm/).

The Spectrum's 64 KB address space, as this game uses it:

```
 $0000 ┌────────────────────────────────────────────────┐
       │  16 K SYSTEM ROM (not in the snapshot)          │  We call a
       │  $1601 CHAN-OPEN · $1F3D PAUSE · RST 10 PRINT   │  few routines.
 $4000 ├────────────────────────────────────────────────┤
       │  SCREEN BITMAP  ($4000–$57FF)                    │  Drawn into
       │  ATTRIBUTES     ($5800–$5AFF)  32×24 colour cells│  directly by
       │  → loaded with the title/loading screen at boot │  the game.
 $5B00 ├────────────────────────────────────────────────┤
       │  printer buffer (game may reuse)                │
 $5C00 ├────────────────────────────────────────────────┤
       │  SYSTEM VARIABLES ($5C00–$5CB5)                 │  $5C7B STKBOT
       │  → STKBOT set to $E62B: the game claims         │  → $E62B
       │    $E62B–$FFFF as its own playground            │
 $5CCB ├────────────────────────────────────────────────┤
       │  BASIC loader program (the LOAD "" CODE / USR   │  See RE-LOG §7.
       │  bootstrap that pulls the rest in)              │
 $5E88 ├────────────────────────────────────────────────┤
       │  GAME CODE + DATA, "block A"  ($5E88–$E62A)     │  Mostly sealed.
       │  $6EBE PreGameEntry · $E3B2 InitHelper          │  We named only
       │  ...the bulk we never needed to disassemble...  │  what we needed.
 $B0F4 │   ► MASTER TILE BANK  ($B0F4, 3 KB)             │  → assets.md
 $B8F4 │   ► ENTITY SPRITE BANKS ($B8F4 type 0, …)       │  → entities.md
 $D8F0 │   ► INPUT DISPATCH + SCHEMES ($D8F0/$D8F4/$F0F9)│  → input.md
 $DA23 │   ► HORIZONTAL SCROLL  (LDIR $DA23 / LDDR $DA62)│  → scroll-horizontal.md
 $DAF2 │   ► TILE DRAW HELPER   ($DAF2 → $B0F4+index*8)  │  → level-paint.md
 $DCAC │   ► VERTICAL SUB-CELL SHIFT  (altitude & 7)     │  → player.md
 $DCF5 │   ► PLAYER XOR DRAW + DAMAGE READ-BACK          │  → damages.md
 $DDC4 │   ► SHIELD/DAMAGE ACCUMULATOR                   │  → collision.md
 $E1DE │   ► SPRITE XOR BLITTER (why it flickers)        │  → ship-ai.md
 $E45F │   ► PER-FRAME INPUT BITMASK                     │  → input.md
 $E584 │   ► PLAYER ALTITUDE / SHIP SCREEN Y             │  → player.md
 $E9F0 │   ► ENEMY DEATH CHECK (laser pattern $EF)       │  → laser.md
 $E62B ├────────────────────────────────────────────────┤
       │  GAME RUNTIME STATE  ($E62B–$FFFF)              │  Live variables:
       │  $E63B/$E64B player sprite frames               │  score, fuel,
       │  $E8A1 ship home-position table                 │  lives, rescued,
       │  $E8A9–$E8C8 staged sprite quadrant buffers     │  positions —
       │  $E459 SCORE · $E469 RESCUED · $E588 LIVES      │  read these out
       │  $F5FC title loop · $F741 scheme table          │  of a RAM dump!
       │  $FCDB HALL OF FAME (the Star Wars table)       │  → title-menu.md
 $FFFF └────────────────────────────────────────────────┘
```

Read this map as a confession of how little you have to crack. The
shaded "►" lines are the strategic routines — perhaps 3–4 KB of the
25 KB image — and they are *all* you need to understand the game's
feel. Everything between them in "block A" we left as a black box,
because the running emulator answered every question we'd otherwise
have asked the code. The runtime-state band at the top is where the
method pays off most directly: those are the game's own live
variables, and a single `run-emu` dump hands them to you decoded and
finished.

### The strategic regions, one line each

| Region | What it is | Cracked in |
| --- | --- | --- |
| `$B0F4` | Master tile bank (3 KB), `index → $B0F4 + index*8` | [assets.md](disasm/assets.md) |
| `$B8F4…` | Entity sprite banks (type 0 = the pickaxe swing, …) | [entities.md](disasm/entities.md) |
| `$D8F0` | Input dispatcher (`JP (HL)` via `($E461)`) + 5 schemes | [input.md](disasm/input.md) |
| `$DA23/$DA62` | Horizontal scroll — byte-granular `LDIR`/`LDDR` only | [scroll-horizontal.md](disasm/scroll-horizontal.md) |
| `$DAF2` | Inner tile-draw helper (the trail to `$B0F4`) | [level-paint.md](disasm/level-paint.md) |
| `$DCAC` | Vertical sub-cell shift — `altitude & 7` scanlines | [player.md](disasm/player.md) |
| `$DCF5` | Player XOR draw + damage read-back (collision = bitmap) | [damages.md](disasm/damages.md) |
| `$E1DE` | Sprite XOR blitter — the source of the flicker | [ship-ai.md](disasm/ship-ai.md) |
| `$E45F` | Per-frame packed input bitmask | [input.md](disasm/input.md) |
| `$E584` | Player altitude = ship screen Y; page gate at `$75` | [player.md](disasm/player.md) |
| `$E9F0` | Enemy/boss death: target's blitter finds `$EF` beam | [laser.md](disasm/laser.md) |
| `$F5FC` | Title loop + reverse-indexed scheme select `$F741` | [title-menu.md](disasm/title-menu.md) |
| `$FCDB` | Hall of Fame — the 1985 Star Wars easter egg | [title-menu.md](disasm/title-menu.md), [CURIOSITIES.md](CURIOSITIES.md) |

Full subsystem index with status: [disasm/README.md](disasm/README.md).

---

## Where to go next

* **Want the finished product?** → [README.md](../README.md)
* **Want the day-by-day story, dead ends included?** →
  [RE-LOG.md](RE-LOG.md)
* **Want every named address?** → [MEMORY-MAP.md](MEMORY-MAP.md)
* **Want to run a specific tool?** → [TOOLS.md](TOOLS.md)
* **Want the annotated Z80?** → [disasm/](disasm/)
* **Want the fun stuff?** → [CURIOSITIES.md](CURIOSITIES.md)
* **Want the original pre-port risk assessment?** →
  [FEASIBILITY.md](FEASIBILITY.md)
