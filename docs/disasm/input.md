# Input — `$D8F0` dispatcher + 4 control schemes

The title menu lets the player pick one of four control schemes
(KEYBOARD / INTERFACE 2 / KEMPSTON / CURSOR).  Each scheme reads
hardware ports differently but writes the same bit pattern into
`$E45F` (the per-frame input flag byte) for the rest of the game
to consume.

## Dispatcher — `$D8F0`

```
D8F0  LD HL,($E461)
D8F3  JP (HL)             ; jump to the currently-selected handler
```

`($E461)` holds the address of the active scheme's handler.  Set
by the title-menu selection code; default at boot = `$D8F4`
(keyboard).

## Output: `$E45F` bit layout

All four handlers fill the bits of `$E45F` identically:

| Bit | Meaning | Read by |
| --- | ------- | ------- |
| 0 | FIRE pressed (latched via `$E460`) | `$DE44` BIT 0 → laser fire |
| 1 | LEFT/RIGHT key (= L on keyboard) | `$D9CB` BIT 1 → horizontal scroll |
| 2 | (LEFT row in `$D8F4`; "facing flipped" flag in `$F0F9`) | internal |
| 3 | DOWN | `$D991` AND $08 → altitude++ |
| 4 | UP   | `$D964` AND $10 → altitude-- |
| 5 | (RIGHT toggle) | `$D949` BIT 5 → facing flip |

Plus `$E460` bit 0 = latched fire-released flag (so the player
can't hold fire to spam shots — must release between).

## Scheme handlers

### `$F741` table — title-menu indexed

```
$F741: $FB71  $F177  $F14E  $F0F9  $D8F4
       (5)    (4)    (3)    (2)    (1)
```

**The menu indexes this table in REVERSE.**  The selector at `$F67C`
starts `B=5` and scans the `$F7FE` key bits from key 1 upward with
`SRL A / DJNZ`, so key 1 leaves `B=5` → `$F741[4]` = `$D8F4`, key 2
gives `B=4` → `$F741[3]` = `$F0F9`, etc.  Verified by emu-peek:
holding key 1 on the title installs `$E461=$D8F4`; key 2 installs
`$E461=$F0F9`.

- Option **1** (`$D8F4`) — KEYBOARD
- Option **2** (`$F0F9`) — joystick-style key row 6/7/8/9/0
- Option **3** (`$F14E`) — KEMPSTON
- Option **4** (`$F177`) — INTERFACE 2
- Option **5** (`$FB71`) — SINCLAIR

While the title loop runs, `$F660` re-defaults `($E461)` to `$FB71`
on every pass — so poking `$E461` before the menu has no effect;
the selection is whatever digit starts the game.

### `$D8F4` — KEYBOARD

```
D8F4  LD HL,$E45F
D8F7  LD A,$BF; IN A,($FE); CPL; AND $03; LD (HL),A
                                ; read half-row $BFFE = ENTER/L/K/J/H
                                ; keep bits 0..1 → fire (ENTER) + L
D8FF  RES 0,(HL)
D901  BIT 0,A
D903  JR Z,$D914                 ; fire NOT pressed → clear latch
... (fire-press latch via $E460 bit 0)

D918  LD A,$FE; IN A,($FE); CPL; AND $1F   ; read SHIFT/Z/X/C/V ($FEFE)
D91F  JR Z,$D923
D921  SET 2,(HL)                 ; bit 2 = LEFT row pressed

D923  LD A,$FD; IN A,($FE); CPL; AND $1F   ; read A/S/D/F/G ($FDFE)
D92A  JR Z,$D92E
D92C  SET 3,(HL)                 ; bit 3 = DOWN row pressed

D92E  LD A,$FE; IN A,($FE); CPL; AND $1F   ; re-read SHIFT row
D935  JR Z,$D939
D937  SET 5,(HL)                 ; bit 5 = RIGHT toggle (paired with bit 2)

D939  LD A,$FB; IN A,($FE); CPL; AND $1F   ; read Q/W/E/R/T ($FBFE)
D940  JR Z,$D944
D942  SET 4,(HL)                 ; bit 4 = UP row pressed

D944  ; if bit 2 set (LEFT row), flip $E586 facing
D944  BIT 2,(HL); INC HL
D947  JR Z,$D959
D949  BIT 5,(HL); JR Z,$D95B
D94D  LD A,($E586); XOR $01; LD ($E586),A   ; toggle facing
D955  RES 5,(HL); JR $D95B
D959  SET 5,(HL)                 ; arm RIGHT toggle
```

So KEYBOARD scheme (option 1):
- `ENTER` = FIRE (with release-debounce via `$E460`) — half-row
  `$BFFE` bit 0, **not** key 0 as an earlier revision of this doc
  claimed
- `L` = horizontal scroll — `$BFFE` bit 1, not key 9
- Any of SHIFT/Z/X/C/V = LEFT
- Any of A/S/D/F/G = DOWN
- Any of Q/W/E/R/T = UP
- Toggle facing via `$E586` when LEFT+RIGHT bits clash

### `$F14E` — KEMPSTON

```
F14E  XOR A
F150  IN A,($1F)              ; Kempston joystick port
F152  LD B,$00
F154  BIT 0,A; SET 3,B        ; right → UP bit 3?
F158  BIT 1,A; SET 4,B        ; left → UP bit 4?
F15C  BIT 2,A; SET 2,B        ; down → bit 2
F164  BIT 3,A; SET 1,B        ; up → bit 1
F16A  BIT 4,A; SET 0,B        ; fire → bit 0
F172  LD A,B; CPL
F174  JP $F101                 ; reuse $F0FF's fall-through to write to $E45F
```

Reads the Kempston port `$1F` (interface ROMs map this), translates
to `$E45F` bits, then jumps into the CURSOR-handler's tail at
`$F101` for the latch-store.

### `$F177` — INTERFACE 2 (Sinclair joystick)

Reads half-rows `$F7FE` (keys 1-5) and `$EFFE` (keys 0,9,8,7,6),
maps the bits onto B, falls into `$F101` like KEMPSTON.

### `$F0F9` — option 2 (keys 6/7/8/9/0, Sinclair port-1 arrangement)

Reads half-row `$EFFE` once, then an `RRA` chain peels the bits
(active-low; bit 0 = key 0 ... bit 4 = key 6):

```
F0FD  LD A,$EF; IN A,($FE); LD B,A    ; B = raw read for later
F107  RRA; JR NC,latch                 ; key 0  → FIRE (edge-latched via $E460 bit 0)
F116  RRA; JR C,+2; SET 4,C            ; key 9  → UP    ($E45F bit 4)
F11B  RRA; JR C,+2; SET 3,C            ; key 8  → DOWN  ($E45F bit 3)
F120  RRA; JR NC,horiz                 ; key 7 pressed → horizontal
F123  RRA; JR NC,horiz                 ; key 6 pressed → horizontal
F126  JR $F146                         ; neither → store C, done
horiz:
F128  LD E,$00; SET 1,C                ; bit 1 = horizontal scroll
F12C  BIT 3,B; JR NZ,+2; LD E,$01      ; E = 1 if the press was key 7
F132  LD A,($E586); AND $01; XOR E     ; compare wanted direction vs facing
F138  JR Z,$F146                       ;   same → just scroll
F13A  SET 2,C                           ;   differs → flag + flip facing
F13C  LD A,($E586); AND $01; XOR $01; LD ($E586),A
F146  LD A,C; LD ($E45F),A; POP HL; JP $D959
```

So option 2 is **6=LEFT, 7=RIGHT, 8=DOWN, 9=UP, 0=FIRE** — the
classic Sinclair Interface-2 *port 1* arrangement, **not** the
Protek cursor layout (5/6/7/8) an earlier revision of this doc
assumed.  Left/right presses flip `$E586` facing when needed, then
scroll (`$E45F` bit 1).  Empirically verified: holding key 8 sets
`$E45F=$08` and dives (`$E584` 0→$51 in 60 frames); key 7 sets
`$E45F=$02`.

`$F101` is the shared tail entry used by KEMPSTON/INTERFACE 2 —
they build the raw byte in their own way and jump in.

### `$FB71` — SINCLAIR (option 5)

(Not yet disassembled.)

## C# port

Our `Sdl2InputPump` already maps host keyboard keys into the
`GameInput` struct (Up/Down/Left/Right/Horizontal/Fire); the
cassette's control-scheme dispatch is mostly bypassed since we
don't have to emulate a Spectrum keyboard.

The interesting bits are:
- `($E460)` fire-release debounce — we approximate with a
  `_fireCooldown` counter in `World` that prevents spam.
- The `$E586` facing flip on LEFT-vs-RIGHT key combo — relevant
  for the original's "press direction key to flip facing" UX.
  Our port has explicit `Left`/`Right` GameInput flags, so the
  flip is direct (no SHIFT-row+key-row combo needed).

## Port-only addition — Shift precision modifier

The cassette's keyboard scheme treats SHIFT as part of the
LEFT key-group (any of SHIFT/Z/X/C/V = LEFT, per `$D918`
"IN A,($FE)" read of the bottom row of the keyboard matrix).
The port hijacks SHIFT for a different role:

- **Hold L-Shift or R-Shift** to enter precision mode.
- In precision mode, each direction key (Up/Down/Horizontal)
  fires **one step per press-edge** instead of accelerating
  while held.  Step sizes:
  - Up / Down: **1 pixel** of altitude per edge (the smallest
    altitude unit `($E584)` supports).
  - Horizontal (L / Left / Right): **1 pixel** of scroll per
    edge — port-only sub-byte composition (see below).
- To step again, RELEASE the direction key, then RE-PRESS it
  (while still holding SHIFT).  Continuous hold = exactly one
  step then frozen.
- Releasing SHIFT immediately reverts to the cassette's
  acceleration ramp (`SpeedShift` reset to 1, no mid-ramp
  carry-over).

Verified in `HeadlessTestRunner`:

| Inputs (50 frames) | Final altitude |
| ------------------ | --------------- |
| A held, no Shift   | 80 (acceleration ramp) |
| A held + Shift held | 1 (single edge, then frozen) |
| A pulsed 3-on/2-off + Shift held | = number of press-edges |

### Implementation

- `GameInput.Shift` — held-state flag.
- `Sdl2InputPump` maps both `SDLK_LSHIFT` (`0x400000E1`) and
  `SDLK_RSHIFT` (`0x400000E5`) into the flag.
- `HeadlessTestRunner` accepts `SHIFT` in `--keys=` schedules
  for reproducible tests.
- `World` keeps three private `_prevUp / _prevDown /
  _prevHorizontal` bools updated at the end of each
  `TickPlaying`; the Shift-precision branch reads
  `input.X && !_prevX` as the edge condition.

### Why this isn't a cassette behaviour

Nothing in `$D8F4..$DDA9` implements an "edge-only" mode — every
control scheme writes the held key state directly into `$E45F`,
and `$D95D` always runs the SpeedShift ramp.  Adding edge
detection at the cassette layer would have required spare RAM
for the per-key previous-state cache (which the original game
doesn't allocate).  This is purely a port quality-of-life
feature; the diff-vs-emu harness is unaffected because the
emulator never receives a SHIFT keystroke.

### Sub-byte horizontal scrolling (port-only)

The cassette's horizontal scroll routines `$DA23` (bitmap
LEFT/ship RIGHT) and `$DA62` (bitmap RIGHT/ship LEFT) use
`LDIR`/`LDDR` over 31 bytes per scanline — each call shifts the
entire bitmap by **exactly 1 byte = 8 pixels**.  Verified by
disasm: both routines have a single entry path from `$D9C8`,
no caller invokes them more than once per frame, and there's
no other scroll mechanism in the ROM.  So the cassette CAN'T
scroll horizontally at sub-byte precision.

This is fine in the original game because all walls + tile art
are byte-aligned (8 px grid), so the player and obstacles share
the same granularity.  But the Shift precision modifier in the
port aims for literal 1-pixel control, so we add an extension:

- `World.SubPixelScroll` (0..7) tracks the pixel offset within
  the current byte.  Total world-pixel X =
  `ScrollOffsetX * 8 + SubPixelScroll`.
- Shift+Horizontal edge advances `SubPixelScroll` by 1; on
  overflow it wraps and bumps `ScrollOffsetX`, which triggers a
  fresh `PaintLevelAtOffset` (byte-aligned, cassette-faithful).
- `LoadLevel` resets both `ScrollOffsetX` and `SubPixelScroll`
  to 0.
- `DrawPlaying` applies a post-shift over the **entire playfield
  bitmap** (`y=0..127`) after the level + every entity has been
  drawn — `ApplyPlayfieldSubPixelShift(fb, SubPixelScroll)`.
  Each scanline is bit-rotated left by `subPx` pixels in one
  pass (`out[col] = (in[col] << subPx) | (in[col+1] >> (8-subPx))`).
  Player is drawn AFTER this shift so it stays at fixed
  screen X=128.

**Why post-shift instead of compose-during-paint?**  An earlier
attempt did the sub-byte composition inside `PaintLevelAtOffset`
only, which shifted the LEVEL by 1 px but left workers / ships /
bullets / decor entities at byte-aligned positions.  Result: as
the user pressed Shift+L the level shifted but workers stayed
put, looking like "the miner moves away from the ship".  Doing
the shift on the composite playfield bitmap fixes that — the
cave + all entities shift together.  The player is the only thing
exempted (drawn after the shift), preserving the "ship stays at
fixed screen X" invariant.

Non-Shift L still uses the cassette's byte-aligned 8 px/frame
path.  `SubPixelScroll` stays 0 in normal play, so the post-shift
is a no-op — diff-vs-emu unaffected (verified 0% at f100/f300).
