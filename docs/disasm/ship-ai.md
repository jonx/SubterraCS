# Ship AI internals — `$E920` and its helper chain

The full disassembly of the enemy-ship AI dispatcher and the
~15 helper routines that drive ship movement, scenery collision,
bullet firing, and player-collision.  Companion to
[`enemies.md`](enemies.md) which has the top-level subsystem map.

## `$E920` — top-level AI dispatcher

Called once per frame from `$E8FD` (the entity supercaller).

```
E920  XOR A; LD ($E8FA),A         ; clear "spawn this frame" counter
E924  LD A,($EE73); XOR $01; LD ($EE73),A
E92C  RET Z                        ; every-other-frame skip
E92D  LD A,($E48B); INC A; AND $03; LD ($E48B),A    ; cycle = (cycle+1) mod 4

E936  EXX
E937  LD HL,$E597                  ; alt-bank HL' = ship table base
E93A  EXX
E93B  LD B,$07                     ; 7 ship slots

E93D  PUSH BC                       ; loop top — preserve B
E93E  EXX; PUSH HL                  ; save HL' before mutating
E940  LD DE,$E5DB                   ; sprite-data table base
E943  LD HL,($E48B); ADD HL,HL × 4  ; HL = cycle * 16  (4×16 banks, 8 sprite + 8 reserved)
E94A  ADD HL,DE                     ; HL = $E5DB + cycle*16
E94B  EX DE,HL                      ; DE = sprite-data ptr for this cycle
E94C  POP HL; EXX                   ; restore HL' (ship slot ptr)

E94E  LD HL,$5C79; INC (HL) × 2    ; bump sysvar counter (drives RNG)
E953  CALL $E910                    ; mutate ($EE7A) RNG state

E956  PUSH BC; EXX; POP BC          ; sync B (slot counter) into alt-bank
E959  LD C,B                        ; (preserved for later)
E95A  LD B,$0F                      ; B = $0F (some max?)

E95C  PUSH HL; EXX; POP HL          ; swap HL with HL' so HL points at slot record
E95F  INC HL × 2                    ; HL = (slot record) + 2 (= status byte)
E961  BIT 7,(HL)
E963  JP NZ,$E999                   ; if ALIVE → draw path

; --- empty-slot path: maybe spawn a new ship ---
E966  LD A,($E8F9); DEC A
E96A  JR Z,$E97F                   ; if $E8F9 == 1, skip spawn attempt
E96C  LD A,($E8FA); INC A; LD ($E8FA),A
E973  CP $07; CALL Z,$F93A         ; on 7th empty slot this frame, call $F93A
                                    ; (TBD — possibly "no more spawns" SFX/state)
E978  JR NZ,$E97F
E97A  LD A,$01; LD ($E8F9),A       ; latch "already attempted spawn"

; --- level-gated AI path ---
E97F  LD A,($E587); CP $06          ; if level < 6, skip the level-6-special path
E984  JP C,$EAA6                   ; → next slot

E987  CALL $EADE                    ; (re-)init random AI bytes in alt-bank HL'
E98A  CALL $EB5B                    ; scroll-tick + scenery probe; ZF = clear
E98D  JR Z,$E999                    ; if clear, draw at $E999
E98F  EXX; INC (HL)×3; INC HL; INC (HL); DEC HL; EXX
                                    ; bump 4 of the AI bytes (collision-bounce)
E997  JR $E98A                      ; retry scenery probe

; --- draw path (alive or just-spawned) ---
E999  CALL $EAB2                    ; range check (CF = out of scroll window)
E99C  JP NC,$EA2C                   ; if NC (= in range), skip to post-draw
E99F  EX AF,AF'
E9A0  CALL $EABD                    ; compute (HL, DE, A=offset) for blit
E9A3  CALL $E9AC                    ; blit cell 1 (top half)
E9A6  CALL $E9AC                    ; blit cell 2 (bottom half)
E9A9  JP $EA2C                      ; → post-draw
```

## `$EA2C..$EA74` — post-draw + movement

```
EA2C  EXX
EA2D  INC B; DEC B; CALL Z,$EA24  ; if B (alt-bank counter) == 0, special path
EA32  INC HL × 3; SET 7,(HL)      ; SET bit 7 of (HL+3)  (mark ?)
EA37  DEC HL; BIT 7,(HL); DEC HL × 2
EA3C  EXX
EA3D  JP Z,$EAA3                  ; if pre-set status (HL+2) bit 7 was CLEAR
                                  ; (i.e. ship was DEAD), go to fire-bullet path

; --- AI movement loop (live ship) ---
EA40  CALL $EB00                  ; step animation state
EA43  CALL $EB5B                  ; scroll-tick + scenery probe
EA46  JR Z,$EA4D                  ; if open, exit loop
EA48  CALL $EB47                  ; reverse direction (toggle bit 5,6)
EA4B  JR $EA40                    ; retry

EA4D  CALL $EAB2                  ; range check after move
EA50  JR NC,$EAA3                 ; out of range → fire path
EA52  EX AF,AF'
EA53  EXX
EA54  INC HL × 3; LD A,(HL)
EA58  AND $7F; JR Z,$EA5F
EA5C  INC A; AND $0F              ; advance animation frame counter
EA5F  LD (HL),A
EA60  DEC HL × 3
EA63  PUSH HL; LD HL,$0010; ADD HL,DE; EX DE,HL; POP HL  ; advance sprite ptr by +16
EA6A  EXX
EA6B  CALL $EABD                  ; compute blit offset
EA6E  CALL $EA77                  ; redraw cell 1
EA71  CALL $EA77                  ; redraw cell 2
EA74  JP $EAA3                    ; → fire path
```

## `$EAA3..$EAB1` — fire-bullet gate

```
EAA3  EXX; INC HL × 4; EXX
EAA9  C1 ... DJNZ $E93D          ; next slot or return
EAB1  RET

(JP $EAA3 from $EA3D / $EA74 doesn't return — it actually falls into $EB99 which falls into $EBB2 via the call site at $EAA3 → $EB99.  Re-check needed.)

Actually: $EAA3 hits `EXX; INC HL×4; EXX` then POP BC, DJNZ, returns.  The fire path is reached differently — let me re-verify by examining the original chain.  See $EB99 below.
```

## `$EB99` — fire-bullet caller

Falls through into `$EBB2` (the spawn routine documented in
[`enemies.md`](enemies.md)).  Called from `$EAA3` indirectly.

```
EB99  NOP
EB9A  EXX
EB9B  LD BC,$0003; ADD HL,BC; PUSH HL
EBA0  XOR A; SBC HL,BC
EBA3  EXX
EBA4  POP HL                       ; HL = slot record + 3 (= sub-byte)
EBA5  LD A,(HL); AND A; RET NZ    ; if sub-byte != 0, don't fire
EBA8  LD A,($E587); LD B,A         ; level → B
EBAC  LD A,R; AND $0F              ; random 0..15
EBB0  CP B; RET NC                 ; if random ≥ level, don't fire
                                   ; (fire rate scales with level)
EBB2  ...                          ; falls through to spawn-bullet ($EE9E)
```

## `$E910` — RNG state mutation

```
E910  NOP
E911  LD HL,($EE7A)        ; HL = current RNG state
E914  LD A,R                ; R = Z80 refresh register (counts opcodes)
E916  ADD A,(HL); ADD A,H
E918  LD L,A
E919  LD H,(HL)             ; chain through memory for an avalanche
E91A  AND $3F               ; mask
E91C  LD ($EE7A),HL         ; store new state
E91F  RET
```

A small chained-PRNG.  Used by `$E920` to seed per-ship behaviour.

## `$EADE` — ship-state init/refresh (alt-bank HL')

Writes 3 bytes at HL[0..2] to randomize the ship's AI state.

```
EADE  EXX
EADF  PUSH HL
EAE0  LD A,($5C79); LD (HL),A           ; HL[0] = sysvar counter
EAE4  LD A,R; AND $78; SUB $07; AND $7F; OR $01   ; A = random byte (odd, ≤ $7F)
EAEE  INC HL; LD (HL),$40                ; HL[1] = $40
EAF1  LD B,A
EAF2  LD A,($5C79); XOR B; AND $60; OR B; OR $80   ; combine status flags
EAFB  INC HL; LD (HL),A                  ; HL[2] = $80 | (xor result)
EAFD  POP HL; EXX; RET
```

So `$EADE` writes (sysvar, $40, $80|x) into HL[0..2] — looks like
ship's (frame_counter, init_marker, status_with_alive_bit) reset.

## `$EB00` — animation step

```
EB00  NOP
EB01  EXX; INC HL × 2
EB04  LD A,(HL); DEC HL
EB06  BIT 5,A
EB08  JR Z,$EB17

; bit 5 set: increment HL[1]
EB0A  EX AF,AF'
EB0B  LD A,(HL); INC A; CP $70
EB0F  JR C,$EB23                ; if A < $70, store and exit
EB11  DEC A; CALL $EB3E         ; else toggle bit 5 of HL[1] (clamp)
EB15  JR $EB23

; bit 5 clear: decrement HL[1]
EB17  EX AF,AF'
EB19  LD A,(HL); DEC A; CP $04
EB1D  JR NC,$EB23
EB1F  INC A; CALL $EB3E         ; clamp at 4 with bit-5 toggle

EB23  LD (HL),A                  ; store back
EB24  LD A,($EE7A)
EB27  PUSH HL; LD HL,$E8FB; CP (HL); POP HL
EB2D  JR C,$EB32
EB2F  CALL $EB52                 ; if RNG ≥ $E8FB, toggle bit 6 of HL[1]
EB32  EX AF,AF'
EB33  DEC HL; AND $40
EB36  JR Z,$EB3B
EB38  INC (HL)                   ; bump byte 0 if A & $40
EB39  JR $EB3C
EB3B  LD BC,$D935                ; (= some return-elsewhere?)
EB3C  RET
```

So `$EB00` advances the ship's animation/movement state byte at
HL[1] within range `[$04..$70]`, bouncing at the endpoints by
toggling bit 5 (which controls direction).  Also occasionally
toggles bit 6 (RNG-driven secondary direction).

## `$EB3E` / `$EB47` / `$EB52` — bit-toggle helpers

| Routine | Effect |
| ------- | ------ |
| `$EB3E` | Toggle bit 5 of HL[+1] (X-direction reverse) |
| `$EB47` | Toggle bits 5 AND 6 of HL[+2] (full direction reverse) |
| `$EB52` | Toggle bit 6 of HL[+1] (Y-direction reverse) |

## `$EB5B` — scroll-progress + scenery probe

```
EB5B  CALL $D827          ; bump scroll-progress ($EE74)
EB5E  EXX; PUSH HL; EXX; POP DE   ; DE = HL' (alt-bank pointer)
EB62  NOP
EB63  LD A,(DE)            ; A = world X byte from alt-bank
EB64  LD HL,($E579)        ; HL = level scenery base pointer
EB67  LD C,A; LD B,$00
EB6A  ADD HL,BC            ; HL += world X
EB6B  INC DE; LD A,(DE); DEC DE
EB6E  SRL A × 3            ; row = Y >> 3
EB74  LD C,B; LD B,A
EB76  ADD HL,BC            ; HL += row * 256
EB77  LD A,(HL); AND A
EB78  RET                  ; ZF = (HL) was 0 = open space
```

**`$EB62`** (the entry inside `$EB5B`) is the pure scenery-probe
without the scroll-tick.  Returns ZF=1 if the level tile at the
world position is 0 (passable).  Used by both the ship AI and the
player-vs-scenery collision at `$DFAF`.

## `$EB7A` — enemy-ship-vs-player collision

```
EB7A  PUSH HL; PUSH DE; PUSH AF
EB7D  LD DE,$E8C9          ; player's 4-quadrant address table
EB80  LD B,$04
EB82  LD A,(DE); CP L      ; address-low match?
EB84  JR NZ,$EB8C
EB86  INC DE; LD A,(DE); DEC DE; CP H   ; address-high match?
EB8A  JR Z,$EB94
EB8C  INC DE × 2; DJNZ $EB82
EB90  POP AF; POP DE; POP HL; RET
EB94  CALL $DD4A           ; HIT → fire collision/death chain
EB97  JR $EB90
```

Symmetric to `$EDC0` (bullet-vs-player) and `$EB7A` is the
ship-vs-player check: if the ship's drawn screen address matches
any player quadrant, fire `$DD4A`.

## `$EAB2` — scroll-window range check

```
EAB2  LD A,($E583); LD B,A
EAB6  EXX; LD A,(HL); EXX     ; A = ship's X (via alt-bank)
EAB9  SUB B; CP $20            ; A = X - $E583; if ≥ $20, CF=1 (out)
EABC  RET                       ; CF = out of window
```

Returns CF=1 if the ship is outside the visible 32-byte scroll
window.

## `$EABD` — compute display offset + advance pointers

```
EABD  NOP
EABE  EXX; INC HL; LD A,(HL); DEC HL; PUSH DE; EXX; POP DE
EAC4  LD C,A
EAC5  SRL C × 3                ; C = Y >> 3 (char-row index)
EACC  SLA C                    ; C *= 2 (word index)
EACE  LD B,$00
EAD0  LD IX,$E80F; ADD IX,BC   ; IX = $E80F + char_row*2
EAD6  AND $07; LD C,A          ; C = Y & 7 (pixel offset)
EAD9  EX DE,HL; SBC HL,BC; EX DE,HL   ; DE -= C
EADD  RET
```

Sets up IX, DE, A for the next blit call to `$E9AC`.

## `$EC4C` — boss tick body

Walks the boss's 20-byte slot starting at `$EE7D`:

```
EC4C  EXX
EC4D  LD HL,$EE7D         ; HL' = boss slot
EC50  LD DE,$EE8E          ; DE  = extended state
EC53  LD B,$14              ; B = 20 (slot size)
EC55  EXX

EC56  CALL $EAB2            ; range check (same as ships)
EC59  JP NC,$EC81          ; if out of range, JP to movement path

EC5C  EX AF,AF'
EC5D  CALL $EABD            ; compute offset
EC60  CALL $E9AC × 2        ; blit 2 cells (same as ships)

EC66..EC74  ; if alt-bank B (= $14) is non-zero, JR $EC81
           ; else: set HL[$7C] / RNG-store at $EE7E /
           ;       set $EE7D from $EE7A's byte / clear $EE7C
EC80  RET

EC81  ; movement path
EC82  LD A,($EE81); DEC A; JR NZ,$EC8A
EC88  LD A,$0C
EC8A  LD ($EE81),A          ; cycle EE81 in 1..12

EC8D  LD C,A; DEC C; SRL C × 2; LD B,$00
EC95  LD HL,$EE84; ADD HL,BC ; HL = $EE84 + (cycle/4)
EC99  LD A,(HL)
EC9A  LD HL,$EE8F; LD (HL),A; INC HL; LD (HL),A
                              ; mirror $EE84[i] → $EE8F, $EE90

ECA0  LD HL,$EE7D; LD A,(HL); LD C,A
                              ; C = boss X
ECA5  LD A,($E583); ADD A,$10  ; player byte = $E583 + 16 (not 15!)
ECAA  CP C
ECAB  JR Z,$ECD7              ; if equal, special path
ECAD  CALL C,$EBFF            ; sign helpers (-1 or +1)
ECB0  CALL NC,$EC06
ECB3  LD C,A
ECB4  LD HL,$EE7F
ECB7  LD A,($EE80); XOR C
ECBA  JR Z,$ECC6
ECBD  DEC (HL); JR NZ,$ECE6   ; decrement boss state byte
ECC0  LD A,C; LD ($EE80),A    ; remember new sign
ECC4  JR $ECE6
...
ECD7  ; same-column path: handle Y axis
ECD8  INC HL; LD C,(HL)
ECDA  LD A,($E584); CP C       ; compare altitude to boss Y
                              ; (more state munging follows)
```

The boss has its own movement algorithm:
- Picks a per-cycle "speed" from `$EE84` table (rotated by `$EE81`)
- Chases the player horizontally (X direction set by sign of
  `(E583+16) - boss.X`)
- Tracks Y via `$EE80` direction-state byte and altitude
- Mirrors state through `$EE8F`/`$EE90` for next-frame draw

## `$DFAF` — player-scenery probe + worker pickup

Called from main loop at `$D813`.

```
DFAF  LD HL,($E583); LD A,L; ADD $0F; LD L,A   ; player byte = scroll+15
DFB6  LD ($EE76),HL
DFB9  LD DE,$EE76
DFBC  CALL $EB62              ; probe scenery at (player_x, player_y)
DFBF  EX AF,AF'

DFC0  LD HL,$EE76; INC (HL)    ; player_x + 1
DFC3  EX DE,HL
DFC5  CALL $EB62              ; probe at (player_x+1, player_y)
DFC8  CP $01
DFCA  CALL Z,$DFEE            ; if returned $01, call $DFEE

; --- worker overlap check ---
DFCD  LD HL,$E589; LD A,($E583); CP (HL)
DFD3  RET NZ                   ; if $E583 != $E589, no match
DFD5  INC HL; LD B,(HL)
DFD7  LD A,($E584); CP B; JP Z,$DFE1
DFDE  DEC B; CP B; RET NZ      ; or B-1
DFE1  LD A,($E466); CP $5F     ; if fuel < $5F
DFE7  RET NC
DFE8  CALL $F90E              ; fuel-recharge sound
DFEB  JP $E419                  ; → fuel-refill animation

DFEE  EX AF,AF'; CP $01
DFF1  JP Z,$DBC8               ; if scenery tile == $01, die immediately
DFF4  EX AF,AF'; RET
```

So `$DFAF` does two things:
1. **Player-vs-scenery collision** — probes the level tile at the
   player's world position via `$EB62`.  If the tile is `$01`
   (= solid wall), jumps to `$DBC8` (death).
2. **Fuel pickup** — if the player's position matches `($E589)`
   (next pickup target), refills the fuel via `$F90E` + `$E419`.

## `$DCAC` — sprite-context maintenance

Called from main loop at `$D804`.  Maintains the `$E8B0..$E8C8`
table of player sprite addresses (4 quadrants × ~8 sub-position
banks per scanline within a char-row).

```
DCAC  CALL $E3F4               ; player sprite staging
DCAF  LD A,($E584); AND $07    ; A = altitude & 7 (sub-position)
DCB4  LD C,A; INC C; DEC C
DCB7  JR Z,$DCF1               ; if at char-row boundary, jump

; --- shift the address banks one step ---
DCB9  LD B,$07                  ; 7 shifts
DCBB  LD DE,$E8C8; LD HL,$E8C0
DCC1  CALL $DCC6                ; shift one bank
DCC4  JR $DCD4

DCC6  NOP                       ; shift helper:
DCC7  DEC HL; LD A,(HL); INC HL; LD (HL),A
DCCB  DEC DE; LD A,(DE); INC DE; LD (DE),A
DCCF  DEC HL; DEC DE; DJNZ $DCC6
DCD3  RET

DCD4  LD A,($E8B8); LD ($E8C1),A
DCDA  LD A,($E8B0); LD ($E8B9),A
DCE0  LD B,$07
DCE2  LD DE,$E8B8; LD HL,$E8B0
DCE8  CALL $DCC6
DCEB  XOR A; LD (HL),A; LD (DE),A
DCEE  DEC C; JR $DCB5           ; loop for each sub-position

DCF1  CALL $DDEB                ; alternate (boundary) path
DCF4  RET
```

This is the **player sprite bank-shifter** — as altitude changes
sub-pixel position within a char-row, the player's 4-quadrant
screen addresses at `$E8C0..$E8C8` need to shift to point at the
right scanlines.  `$DCAC` keeps that table coherent.

## Address inventory added by this trace

| Addr | Purpose |
| ---- | ------- |
| `$E910` | RNG state mutation helper |
| `$E97A` / `$E8F9` / `$E8FA` | Spawn-this-frame state |
| `$EADE` | Ship AI bytes init/refresh |
| `$EB00` | Animation step + range-clamp |
| `$EB3E` / `$EB47` / `$EB52` | Bit-toggle helpers (direction reverse) |
| `$EB5B` / `$EB62` | Scroll-tick + scenery probe |
| `$EB7A` | Enemy-ship-vs-player collision |
| `$EAB2` / `$EABD` | Range check + display-offset compute |
| `$EAA3` / `$EB99` | End-of-slot + fire-bullet gate |
| `$EC4C` / `$EC81` | Boss tick body + boss movement |
| `$EE7D..$EE8E` | Boss's 20-byte slot |
| `$EE7E` / `$EE7F` / `$EE80` / `$EE81` / `$EE84` | Boss AI state bytes |
| `$EE8F` / `$EE90` | Boss mirrored state for next-frame draw |
| `$DFAF` | Player-vs-scenery + fuel pickup |
| `$DCAC` | Player sprite bank-shifter |
| `$DFEE` | Wall-tile collision → death ($DBC8) |
| `$E589`,`$E58A` | Worker/pickup target coords |
