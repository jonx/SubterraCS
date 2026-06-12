# Player ship — physics, sprite stage, draw

The player's Stryker.  Screen X is hard-fixed at columns 15/16
(x = 120, verified — see `$E8A1` below and
[scroll-horizontal.md](scroll-horizontal.md)); vertical altitude is
pixel-precise via UP/DOWN keys; the L key scrolls the level
horizontally in 8 px byte-columns.  Drawn as a 16×8 XOR sprite
shifted to its sub-cell Y inside a 16×16 staging window.

## Address inventory

| Addr | Meaning |
| ---- | ------- |
| `$E584` | Altitude (= sprite Y, 0..$78) |
| `$E585` | Vertical SpeedShift (acceleration counter) |
| `$E586` | DirectionState (bit 0 = facing left) |
| `$E587` | Current level (0..5) |
| `$E588` | Lives counter (game-over when DEC reaches 0) |
| `$E63B..$E66B` | Player sprite source — right-facing first, then left |
| `$E8A1..$E8A8` | Static HOME quadrant addresses (`$400F $4010 $402F $4030`) — copied to `$E8C9` and `$E8F1` by level init `$E2FC`; fixes ship X at columns 15/16 |
| `$E8A9..$E8C8` | Staged sprite work area (filled by `$E3F4`) |
| `$E8C9..$E8D0` | 4 quadrant screen addresses for the player draw |
| `$E8D1..$E8F0` | Previous-frame "undo" buffer |

## `$E3F4` — sprite staging

```
E3F4  LD HL,$E8A9; LD DE,$E8AA; LD BC,$001F
E3FD  LD (HL),$00; LDIR              ; clear 32 bytes of work area
E401  LD DE,$E8A9                     ; dest = work area
E404  LD HL,$E63B                     ; src = right-facing sprite
E407  LD BC,$0000
E40A  LD A,($E586); BIT 0,A
E40F  JR NZ,$E413
E411  LD C,$10                        ; facing LEFT → offset src by 16
E413  ADD HL,BC
E414  LD C,$10
E416  LDIR                            ; copy 16 bytes from src to work area
E418  RET
```

Stages the correct-facing 16-byte sprite into `$E8A9` for `$DCF5`
to consume.  Called once per frame from `$DCAC`.

## `$DCAC` — vertical sub-cell positioning (sprite-data shifter)

Called from main loop at `$D804`.  Calls `$E3F4` to stage the
16×8 sprite at the TOP of the 32-byte work area, then — when
`altitude & 7 != 0` — shifts the staged sprite BYTES down one
scanline per loop pass, `altitude & 7` times.  (An earlier
revision of this doc mislabelled this as an "address bank
shifter"; the shifted region `$E8A9..$E8C8` is the sprite data
itself, not addresses.)

Per pass: all four 8-byte quadrant buffers shift down one byte;
the byte falling off the bottom of the top-left quadrant wraps
into the top of the bottom-left quadrant (`$E8B0 → $E8B9`), the
top-right's into the bottom-right's (`$E8B8 → $E8C1`), and the
freshly-exposed first scanlines are zeroed.  The quadrant SCREEN
addresses at `$E8C9` stay char-aligned (recomputed by `$DDEB` =
`row base + $10`); the sub-cell offset lives entirely in the
data.  **This is how the cassette achieves 1-pixel vertical
movement on a char-aligned draw** — there is no horizontal
counterpart (see
[scroll-horizontal.md](scroll-horizontal.md)).

## `$DCF5` — XOR player draw

```
DCF5  LD IX,$E8C9             ; IX = 4-quadrant address table
DCF9  LD DE,$E8A9              ; DE = staged sprite bytes
DCFC  EX AF,AF'; XOR A; EX AF,AF'   ; A' = 0 (collision-flag carrier)

DCFF  LD L,(IX+$00); LD H,(IX+$01)   ; HL = first quadrant top-left addr
DD05  CALL $DB0E                       ; H ← attribute address
DD08  LD H,A
DD09  LD (HL),$47                      ; paint attribute (yellow bright)
DD0B  INC HL; LD (HL),$47
DD0E  LD BC,$001F; ADD HL,BC; LD (HL),$47   ; (more attrs for bottom)
DD14  INC HL; LD (HL),$47

DD17  LD C,$04                          ; 4 quadrants
DD19  LD H,(IX+$01); LD L,(IX+$00)
DD1F  LD B,$08                          ; 8 scanlines per quadrant
DD21  LD A,(DE)                          ; sprite byte
DD22  AND A; JR Z,$DD2E
DD25  INC (HL); DEC (HL); JR Z,$DD2C    ; test (HL) is zero
DD29  EX AF,AF'; SCF; EX AF,AF'         ; collision detected!
DD2C  XOR (HL); LD (HL),A                ; XOR sprite into bitmap

DD2E  INC H                              ; next scanline
DD2F  INC DE
DD30  DJNZ $DD21                         ; loop 8 scanlines

DD32  INC IX × 2; DEC C; JP NZ,$DD19    ; next quadrant
DD3A  EX AF,AF'; CALL C,$DD4A            ; if collision flag set → death chain
DD3E  LD BC,$0028; LD HL,$E8A9; LD DE,$E8D1; LDIR
                                          ; copy current sprite → undo buffer
DD49  RET
```

So `$DCF5`:
1. Paints the attribute cells around the player (bright yellow).
2. For each of the 4 quadrants (8×8 cells), XOR-blits the sprite
   bytes from `$E8A9` into the bitmap.
3. On any XOR collision (= sprite hit existing scenery pixels),
   sets a collision flag; at the end calls `$DD4A` (death) if
   the flag was set.
4. Copies the current sprite to `$E8D1` so the NEXT frame can
   XOR-erase the previous one (= flickerless XOR-erase pattern).

## `$D95D` — UP/DOWN vertical movement

See [ship-ai.md](ship-ai.md) for the trace.  Updates `$E584`
(altitude) using `$E585` (SpeedShift) as an acceleration counter.

## `$D9C8` — L-key horizontal scroll trigger

See [scroll-horizontal.md](scroll-horizontal.md).  Dispatches to
`$DA23` (scroll-left) or `$DA62` (scroll-right) based on facing.

## `$F868` — page-advance gate

```
F868  LD A,($E583); AND A; RET NZ        ; if game-state lock, bail
F86C  LD A,($E584); CP $75; RET C         ; if altitude < $75, bail
F870  LD A,($E587); LD HL,$E77D; ADD HL,DE
F875  BIT 0,(HL); RET Z                   ; if level-cleared flag not set, bail
F878  LD HL,$0000; LD ($E459),HL          ; (zero out a score word? wrong addr)
F87E  ...
F880  CALL $F6F2                          ; → load next level
```

Triggers level-advance when altitude reaches `$75` (= 117, just
above the HUD strip) AND the level-cleared flag is set (= all
workers picked up).  Calls `$F6F2` which runs the 9-helper
level-load chain (see [level-load.md](level-load.md)).

## C# port

In `World`:
- `Altitude` field = `$E584`; vertical input updates it via
  `$D95D` semantics.
- `FacingLeft` bool = `$E586` bit 0; toggled by Left/Right input.
- Player drawn at `(PlayerX - 8, PlayerY)` where
  `PlayerX = 128` (= byte 15) and `PlayerY = Altitude`.
- Sprite source = `assets/extracted/player-e63b.bin` (32 bytes:
  first 16 = right, next 16 = left).  No on-the-fly staging
  needed because we just pick the right buffer at draw time.
- Page-advance triggered by `Altitude >= 0x75` AND
  `Workers.RemainingThisLevel == 0` (replacing the cassette's
  `$E77D[level]` flag check).
