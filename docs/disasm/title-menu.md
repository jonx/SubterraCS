# Title menu — `$F5FC` loop + control-scheme select

The "SELECT CONTROL OPTION" screen.  Loops drawing the menu +
playing title music, waits for keys 1-5 to be pressed, then
sets up `($E461)` (the input dispatcher) and starts a new game.

## `$F5FC` — title-loop entry

```
F5FC  NOP
F5FD  LD HL,$FF57; RES 1,(HL)            ; clear a state flag

; setup print/UDG channels
F602  CALL $E3B2                          ; channel reset
F605  LD A,$02; CALL $1601               ; open channel 2 (screen)
F60A  LD HL,$E62B; LD ($5C7B),HL          ; UDG-A pointer (bar cells)

F613  XOR A; OUT ($FE),A                  ; silence speaker
F616  LD ($EE83),A                        ; clear boss-kill count
F619  LD A,$02; CALL $1601                ; channel 2 (again, idempotent)
F61E  LD HL,$E62B; LD ($5C7B),HL

; print the menu text via RST 10
F624  LD HL,$F82B                          ; HL = print-stream data
F627  LD A,(HL); CP $FF; JR Z,$F630      ; $FF = terminator
F62C  RST $10; INC HL; JR $F627

; pad with $96 chars
F630  LD B,$60; LD A,$96; RST $10; DJNZ $F632

; --- input poll ---
F637  LD A,$7F; IN A,($FE); AND $0C; RET Z
                                          ; keys M, N (bits 2, 3 of $7FFE)
                                          ; if neither pressed → return to caller

F63E  CALL $FAAF                          ; save print state
F641  CALL $F973                          ; (probably title-music tick)
F644  LD A,($5B54); JR Z,$F64E
F649  LD B,$78; HALT; DJNZ $F64B          ; 120-frame delay (~2 seconds)

F64E  CALL $FA32                          ; play music tick

F651  LD A,$F7; IN A,($FE); CPL; AND $1F  ; keys 1-5 ($F7FE)
F658  JR NZ,$F660                         ; (none → skip)
F65A  CALL $FCDB                          ; (some side effect)
F65D  CALL $FA32                          ; music

; --- default scheme = Sinclair ($FB71) ---
F660  LD HL,$FB71; LD ($E461),HL

; --- pick a "random" something using R-register ---
F666  LD A,R; AND $03; LD B,A
F66B  LD A,($5C79); AND $01; ADD A,B
F671  EX AF,AF'

; --- check keys 1-5 again ---
F672  LD A,$F7; IN A,($FE); CPL; AND $1F
F679  JP Z,$F610                          ; nothing pressed → restart loop

F67C  LD B,$05                             ; B = 5 (=> scheme index 5)
F67E  SRL A; JR C,$F687                   ; find lowest set bit
F682  DJNZ $F67E
F684  JP $F5FC                             ; unrecognized → restart

; --- key found, B = 5-index (1..5) ---
F687  LD E,B; DEC E; SLA E; LD D,$00      ; E = (B-1) * 2 (word offset)
F68D  LD HL,$F741; ADD HL,DE              ; HL = $F741[B-1] (scheme ptr)
F691  LD E,(HL); INC HL; LD D,(HL)
F694  LD ($E461),DE                        ; install scheme!

; --- start new game ---
F69C  LD A,$05; LD ($E588),A              ; lives = 5
F6A1  LD HL,$E77D                          ; clear 7 level-cleared flags
F6A4  LD DE,$E77E; LD BC,$0007; LD (HL),$00; LDIR
F6AE  LD HL,$0000
F6B1  LD ($E459),HL                        ; SCORE = 0
F6B4  LD ($E469),HL                        ; RESCUED = 0
F6B7  LD ($E467),HL                        ; per-level rescue counter = 0
... (more init then JP into the game loop)
```

So the title loop:
1. Prints menu strings from `$F82B` (terminated with `$FF`).
2. Polls for any keypress on row M/N to wake up music.
3. Polls keys `1..5`: each maps to one of 5 control schemes via
   the table at `$F741`.
4. On selection: installs `($E461)`, resets lives/score/rescued,
   then enters the main game loop.

## Menu text data — `$F82B`

```
F82B  ... print-stream bytes ending in $FF
```

Contains the lines:
- "SUBTERRANEAN STRYKER"
- "SELECT CONTROL OPTION TO BEGIN"
- "1. KEYBOARD"  (= but actually maps to FB71 SINCLAIR per index?)
- "2. INTERFACE 2 JOYSTICK"
- "3. KEMPSTON JOYSTICK"
- "4. CURSOR TYPE JOYSTICK"

Plus the company tag.  Exact byte content TBD (would need to
decode the print-stream including `AT row,col`, `INK n`, etc.
codes — see [hud-print.md cross-ref](hud.md)).

## Control-scheme table — `$F741`

| Index | Pointer | Scheme |
| ----- | ------- | ------ |
| 1 | `$FB71` | Sinclair joystick |
| 2 | `$F177` | Interface 2 |
| 3 | `$F14E` | Kempston |
| 4 | `$F0F9` | Cursor |
| 5 | `$D8F4` | Keyboard (extra option?) |

See [input.md](input.md) for the per-scheme handler details.

## C# port

`World.TickSplash` advances to title on FIRE.  The `Title` state
shows the cassette's captured menu screen
(`assets/extracted/title-menu-scr.bin`) and accepts **keys 1–5**
(port of the `$F672` poll) or FIRE to start.  The digit pressed
is recorded in `World.SelectedControlScheme` (1..5; 0 =
FIRE-started) — cosmetic, since `Sdl2InputPump` maps host keys
directly to `GameInput` regardless of scheme, but the menu now
honours the keys it displays.  `GameInput.MenuDigit` carries the
held digit; the headless harness accepts `"1".."5"` in `--keys=`
schedules.

Not ported: the HALL OF FAME idle screen (`$FCDB`, above) and
live menu-text rendering (we blit the captured screen instead).

## `$FCDB` — the HALL OF FAME screen

Called from the title loop at `$F65A` when NO key on the 1–5 row
is pressed (idle).  Fully decoded:

```
FCDB  CALL $E3B2; LD A,$02; CALL $1601    ; channel 2 (screen)
FCE3  LD DE,$FD9E; LD BC,$004B
FCE9  CALL $203C                          ; ROM PR-STRING — header text
FCEC  LD B,$08                            ; 8 score entries
FCEE  LD HL,$FDF5                         ; scores (8 × 16-bit)
FCF1  LD DE,$FE0F                         ; names (8 × 8 chars)
...   per-entry: AT row,col; print name; print score digits
```

The `$FD9E` header decodes (via Spectrum control codes) to:

```
S U B T E R R A N E A N
   S T R Y K E R
 - HALL  OF  FAME -
```

Default table — scores at `$FDF5` (LE 16-bit: 2900, 2820, 2422,
1402, 488, 487, 442, 240) and names at `$FE0F` (8 bytes each):

```
somebody, Wedge, Biggs, John D., Luke, Porkins, ...
```

**The default high-score names are Star Wars Red Squadron
pilots** (Wedge Antilles, Biggs Darklighter, Luke, Porkins) —
a 1985 easter egg sitting unnoticed in the data all along.

## Related

- [input.md](input.md) — what `($E461)` points to per scheme
- `$F973` — a plain `RET` (no-op); the actual title-music ticks
  are `$F64E`/`$F65D CALL $FA32`, gated by `$F637`'s M/N-key
  check (see [sound.md](sound.md) §Title-music gate)
