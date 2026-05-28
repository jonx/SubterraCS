# HUD — `$E046` / `$E0BE` / `$E0F1` / `$E785`

The HUD is drawn in two phases:

1. **Level-load** (once per level): `$E347` walks the print-stream
   table at `$E785` via `RST 10`, painting the labels and the
   empty-bar UDG-A "corner brackets" in their fixed positions.
2. **Per-frame** (every game-loop tick): `$E046` prints the
   live values (depth, score, rescued) and calls `$E0BE` for each
   bar to clear the cells past the value boundary, leaving the
   filled portion visible.

## `$E046` — per-frame HUD updater

```
E046  21 27 50    LD HL,$5027         ; bitmap addr for SCORE row
E049  22 5D E4    LD ($E45D),HL       ; save as print position
E04C  2A 59 E4    LD HL,($E459)       ; HL = current score
E04F  CD F6 DF    CALL $DFF6          ; print score (6 digits)
E052  AF          XOR A
E053  CD 1E E0    CALL $E01E

E056  21 3D 50    LD HL,$503D         ; RESCUED row position
E059  22 5D E4    LD ($E45D),HL
E05C  2A 69 E4    LD HL,($E469)       ; HL = rescued count
E05F  26 00       LD H,$00            ; force 1-byte
E061  CD 09 E0    CALL $E009          ; print 2 digits

E064  21 07 50    LD HL,$5007         ; DEPTH row position
E067  22 5D E4    LD ($E45D),HL
E06A  3A 87 E5    LD A,($E587)
E06D  CD 1E E0    CALL $E01E          ; print depth (1 digit)

E070  3A EA E0    LD A,($E0EA)        ; A = small counter
E073  3C          INC A
E074  E6 0F       AND $0F
E076  32 EA E0    LD ($E0EA),A
E079  47          LD B,A
E07A  20 0B       JR NZ,$E087        ; if not 0, skip random regen
E07C  ED 5F       LD A,R; E6 07 AND $07
E080  20 02       JR NZ,$E084
E082  3E 03       LD A,$03
E084  32 EB E0    LD ($E0EB),A

E087  3A EB E0    LD A,($E0EB)
E08A  CB 38       SRL B; CB 38 SRL B
E08C  04          INC B; 05 DEC B
E090  28 07       JR Z,$E099
E092  05          DEC B; 05 DEC B; 05 DEC B
E095  28 02       JR Z,$E099
E097  F6 40       OR $40              ; OR bright bit

E099  21 00 5A    LD HL,$5A00         ; attr addr (HUD area)
E09C  06 47       LD B,$47            ; 71 cells
E09E  77          LD (HL),A
E09F  23          INC HL
E0A0  10 FC       DJNZ $E09E          ; paint 71 cells with attr A

E0A2  21 60 5A    LD HL,$5A60         ; another attr address
E0A5  06 07       LD B,$07
E0A7  77          LD (HL),A
E0A8  23          INC HL
E0A9  10 FC       DJNZ $E0A7          ; paint 7 more cells

E0AB  21 47 52    LD HL,$5247         ; SHIELD bar bitmap addr
E0AE  3A 64 E4    LD A,($E464)        ; shield value (or scratch — see note)
E0B1  CD BE E0    CALL $E0BE          ; clear cells past boundary

E0B4  21 67 52    LD HL,$5267         ; FUEL bar bitmap addr
E0B7  3A 66 E4    LD A,($E466)        ; fuel value
E0BA  CD BE E0    CALL $E0BE

E0BD  C9          RET
```

**Note on `$E464`/`$E466`**: empirically these oscillate
between near-full (`$5F`) and partial (`$1E`) values across
sequential frame snapshots — possibly scratch variables used
during multi-pass screen draws rather than canonical shield/fuel.
Needs further trace to confirm.  See RE-LOG §35.

## `$E0BE` — bar fade driver

Called for each bar (SHIELD, FUEL) with HL = bitmap address of
the bar's middle scanline and A = current value (range 0..`$60`
= 0..96).

```
E0BE  00          NOP
E0BF  FE 60       CP $60              ; max?
E0C1  C8          RET Z                ; if full, nothing to clear
E0C2  D0          RET NC               ; clamp upper bound

E0C3  5F          LD E,A
E0C4  CB 3B       SRL E; CB 3B SRL E   ; E = A / 4 (boundary cell index)
E0C8  1C          INC E; 1D DEC E      ; test E==0
E0CA  28 03       JR Z,$E0CF
E0CC  16 00       LD D,$00
E0CE  19          ADD HL,DE            ; HL = bar_start + A/4

E0CF  0E FF       LD C,$FF             ; mask "all bits set" = leave full
E0D1  CD F1 E0    CALL $E0F1           ; paint 4 scanlines of $FF at HL

E0D4  0E 00       LD C,$00             ; mask "all clear"
E0D6  23          INC HL
E0D7  CD F1 E0    CALL $E0F1           ; paint 4 scanlines of $00 at HL+1

E0DA  EB          EX DE,HL
E0DB  21 EC E0    LD HL,$E0EC          ; partial-fill mask table
E0DE  E6 03       AND $03              ; A & 3 (sub-cell position)
E0E0  4F          LD C,A
E0E1  06 00       LD B,$00
E0E3  09          ADD HL,BC            ; HL = $E0EC + (A & 3)
E0E4  4E          LD C,(HL)            ; C = partial-fill mask byte
E0E5  EB          EX DE,HL
E0E6  CD F1 E0    CALL $E0F1           ; paint partial mask
E0E9  C9          RET                  ; (continued)
```

**Range**: 0..`$60` = 0..96.  Each bar is 24 cells × 4 quanta = 96.
**Cell size**: 8×8 pixels.  Each cell's "fill" is the middle 4 scanlines.

## `$E0EC` — partial-fill mask table

```
E0EC: 00 C0 F0 FC  FF E5 71 24
```

First four entries (`$00, $C0, $F0, $FC`) are the boundary-cell
masks: 0, 2, 4, 6 left-aligned pixels.  Combined with the 4
quanta per cell, this gives 24×4 = 96 levels of bar resolution.

The remaining `$FF E5 71 24` may be for FUEL or another use.

## `$E0F1` — paint 4 scanlines

```
E0F1  E5          PUSH HL
E0F2  71          LD (HL),C            ; write mask to scanline N
E0F3  24          INC H                 ; advance to scanline N+1
E0F4  71          LD (HL),C
E0F5  24          INC H
E0F6  71          LD (HL),C
E0F7  24          INC H
E0F8  71          LD (HL),C            ; total 4 scanlines written
E0F9  E1          POP HL
E0FA  C9          RET
```

`INC H` is the Spectrum interleave trick: advancing the high
byte of the bitmap pointer moves down ONE pixel row within a
char-row band.

## `$E785` — HUD print-stream table

Walked by `$E347` (level-load) via `RST 10` to print the static
labels + the UDG-A bar cells.

```
E785  10 06 11 00 16 10 00              ; INK 6 (yellow), PAPER 0, AT 16,0
E78C  "DEPTH :" 0D                       ; row 16 + newline
E794  "SCORE :"                          ; row 17
E79B  16 11 16 "RESCUED:" 0D             ; AT 17,22 "RESCUED:"
E7A7  "SHIELD:" 10 00 11 00 20            ; row 18 prefix
E7B3  10 02 90×5  10 03 90×5  10 06 90×5  10 05 90×5  10 04 90×4 0D
                                          ; SHIELD stripe: 5r+5m+5y+5c+4g
E7D6  11 00 10 06 "FUEL  :" 11 00 10 00 20
E7E6  10 02 90×5  10 03 90×5  10 06 90×5  10 05 90×5  10 04 90×?
                                          ; FUEL stripe (same pattern)
E7FE  $FF                                ; terminator
```

`$90` = UDG-A (the "corner brackets" character at `$E62B`).
24 of them per bar gives the 24-cell bar widths.

## `$E62B` — UDG-A: bar cell corner brackets

```
$E62B:  88 80 00 00 00 00 80 88
```

The repeated cell unit of the bars.  Decoded:

```
.X...X..
.X......
........
........
........
........
.X......
.X...X..
```

When fresh (just-drawn by RST 10 / `$E785`), every bar cell has
this pattern.  When `$E0BE` runs each frame it sets the middle 4
scanlines to `$FF` (full) or `$00` (empty), producing the
familiar full-bar look (`88 80 FF FF FF FF 80 88` on screen).

Verified unchanged between boot snapshot and mid-gameplay RAM —
the game does not redefine UDG-A.
