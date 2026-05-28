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
| 2 | (LEFT — shift+keys group) | (used internally by D8F4 only) |
| 3 | DOWN | `$D991` AND $08 → altitude++ |
| 4 | UP   | `$D964` AND $10 → altitude-- |
| 5 | (RIGHT toggle) | `$D949` BIT 5 → facing flip |

Plus `$E460` bit 0 = latched fire-released flag (so the player
can't hold fire to spam shots — must release between).

## Scheme handlers

### `$F741` table — title-menu indexed

```
$F741: $FB71  $F177  $F14E  $F0F9  $D8F4  ...
       (1)    (2)    (3)    (4)    (default)
```

- Option 1 (`$FB71`) — Sinclair JOYSTICK
- Option 2 (`$F177`) — INTERFACE 2 JOYSTICK
- Option 3 (`$F14E`) — KEMPSTON JOYSTICK
- Option 4 (`$F0F9`) — CURSOR JOYSTICK
- `$D8F4` — KEYBOARD (active by default in our snapshots)

### `$D8F4` — KEYBOARD

```
D8F4  LD HL,$E45F
D8F7  LD A,$BF; IN A,($FE); CPL; AND $03; LD (HL),A
                                ; read keys 6/7/8/9/0 (half-row $BFFE)
                                ; keep only bits 0..1 → fire (key 0) + key 9
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

So KEYBOARD scheme:
- `0` key = FIRE (with release-debounce via `$E460`)
- `9` key = the L-equivalent for horizontal scroll
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

### `$F0F9` — CURSOR (5/6/7/8/0)

Reads `$EFFE` (cursor keys 5..0 on the Spectrum keyboard map)
plus the `$E460` debounce latch for fire-release detection.

### `$FB71` — SINCLAIR (Sinclair-1 joystick, keys 6-0)

(Not yet disassembled — at the very top of the menu list.)

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
