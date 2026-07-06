# Rescuable workers — `$E75D` schedule + `$EF02` chain

The workers the player has to rescue.  Stored in a 8-record live
table at `$E75D`, populated at level-load from the per-level
schedule data at `$E69D + level*32`.  Drawn as small 8×8 sprites
on the playfield AND as flashing dots on the mini-map.

## Tables

| Addr | Size | Content |
| ---- | ---- | ------- |
| `$E69D..$E7BC` | 6 levels × 32 bytes | Per-level worker SCHEDULE source |
| `$E75D..$E77C` | 32 bytes (8 records × 4) | Live worker table for current level |
| `$F071..$F078` | 8 bytes | Worker sprite, WHITE variant |
| `$F0F1..$F0F8` | 8 bytes | Worker sprite, LEVEL-COLOUR variant |

Loaded at level-load by `$E2E5` (LDIR from `$E69D + level*32` to
`$E75D`) — see [level-load.md](level-load.md).

## Per-worker record (4 bytes)

| Offset | Meaning |
| ------ | ------- |
| +0     | World X (0..255 byte column) |
| +1     | Row (0..15, char-row index into `$E80F`) |
| +2     | Cycle / animation counter |
| +3     | Status: bit 5 = just-picked, bit 7 = picked-up (no longer drawn) |

Level 1's 8 workers (from `at-f100.bin`):
```
rec[0]: x=$49 row=$09  cycle=$01 flags=$40
rec[1]: x=$6D row=$06  cycle=$00 flags=$40
rec[2]: x=$83 row=$09  cycle=$01 flags=$40
rec[3]: x=$88 row=$09  cycle=$03 flags=$00
rec[4]: x=$CF row=$0E  cycle=$01 flags=$40
rec[5]: x=$F6 row=$0E  cycle=$01 flags=$40
rec[6]: x=$B9 row=$08  cycle=$00 flags=$40
rec[7]: x=$29 row=$0E  cycle=$00 flags=$40
```

Worker world-X spans 39..246; they're scattered across the wider-
than-screen world and gated by `$E583` like other entities.

## `$EF02` — main dispatcher

```
EF02  CALL $F02E      ; mini-map dot draw (XOR-toggled)
EF05  CALL $EF08      ; playfield draw + pickup detect
```

## `$EF08` — per-frame worker tick

```
EF08  LD IX,$E75D
EF0C  LD B,$08                   ; 8 slots
EF0E  PUSH BC
EF0F  BIT 5,(IX+$03)              ; just-picked flag?
EF13  JR Z,$EF1F                  ; no → normal pickup-check path

; --- just-picked path ---
EF15  RES 5,(IX+$03)              ; clear just-picked flag
EF19  SET 7,(IX+$03)              ; SET picked-up flag (= permanently picked)
EF1D  JR $EF28                    ; skip pickup check, just draw (with no
                                  ;   second-color draw — see EF38 BIT 7)

; --- normal path ---
EF1F  CALL $EFAE                  ; check player-near-worker
EF22  CALL Z,$EFE0                ; if so, do pickup action
EF25  JP Z,$EF45                  ; pickup happened → skip draw this frame

; --- draw cell with level colour ---
EF28  LD A,($E57B); EX AF,AF'     ; A' = level colour
EF2C  CALL $EF4E                  ; blit 8x8 cell @ ($F0F1 sprite)
EF2F  LD A,(IX+$02); INC A; AND $1F; LD (IX+$02),A   ; cycle++
EF38  BIT 7,(IX+$03)
EF3C  JP NZ,$EF45                 ; if picked-up, skip second draw

; --- draw cell with white ($07) — blink effect ---
EF3F  LD A,$07; EX AF,AF'
EF42  CALL $EF4E                  ; blit 8x8 cell @ ($F071 sprite)

EF45  LD DE,$0004; ADD IX,DE      ; next record
EF4A  POP BC; DJNZ $EF0E
EF4D  RET
```

So each worker:
- If just-picked (bit 5 of +3 set), permanently disable (bit 7 set)
  and skip the white-draw pass — produces a "freeze" frame at the
  pickup moment.
- If normal: check `$EFAE` for player overlap; if so call `$EFE0`
  (rescue), else draw the worker twice (level-color + white) for
  a blinking visual.

## `$EFAE` — player-near-worker check

```
EFAE  LD A,($E583); ADD A,$0E    ; player byte = scroll + 14
EFB3  CP (IX+$00); JR Z,$EFC9    ; match → pickup possible
EFB8  INC A; CP (IX+$00); JR Z,$EFC9   ; or +1
EFBE  INC A; CP (IX+$00); JR Z,$EFC9   ; or +2
EFC4  INC A; CP (IX+$00); RET NZ        ; or +3 (4-byte-wide pickup zone)

EFC9  LD A,($E584); SRL A × 3    ; A = altitude / 8 = char-row
EFD2  CP (IX+$01); RET Z          ; match → ZF (pickup)
EFD6  INC A; CP (IX+$01); RET Z   ; or row + 1
EFDB  INC A; CP (IX+$01); RET    ; or row + 2 (3-row pickup zone)
```

Pickup zone: 4 bytes wide × 3 char-rows tall (=32 px × 24 px).
Generous so the player doesn't need to be pixel-precise.

## `$EFE0` — pickup action

```
EFE0  PUSH AF
EFE1  BIT 7,(IX+$03); JR NZ,$F014   ; already picked → skip
EFE7  SET 5,(IX+$03)                 ; mark just-picked (handled next frame)

EFEB  LD HL,($E459); LD DE,$0032
EFF1  ADD HL,DE; LD ($E459),HL       ; SCORE += $32 = 50

EFF5  CALL $F016                      ; pickup chime
EFF8  LD HL,$E469; INC (HL)           ; RESCUED++
EFFB  DEC HL × 2; INC (HL)            ; $E467++ (level-rescued counter?)
EFFF  BIT 3,(HL); JR Z,$F014          ; if didn't hit 8, just exit
F003  LD (HL),$00                      ; reset counter

F005  ; all-rescued path for this level
F005  LD A,($E587); LD D,0; LD E,A
F00A  LD HL,$E77D; ADD HL,DE
F00F  LD (HL),$01                      ; ($E77D + level) = 1 = LEVEL CLEARED
F011  CALL $F922                       ; level-clear SFX / sound

F014  POP AF; RET
```

Each pickup:
- +50 score
- RESCUED counter (`$E469`) +1
- Per-level rescue counter (`$E467`) +1
- After 8 rescues per level (bit 3 of `$E467` set), mark
  `$E77D[level] = 1` and play the "all-rescued" sound

## `$EF4E` — per-record playfield draw

```
EF4E  LD A,($E583); LD B,A
EF52  LD A,(IX+$00); SUB B; CP $20; RET NC   ; range gate

EF59  LD E,(IX+$01); SLA E                     ; E = row * 2
EF5E  LD D,0; LD HL,$E80F; ADD HL,DE
EF64  LD E,(HL); INC HL; LD D,(HL); EX DE,HL   ; HL = scanline base addr
EF68  LD E,A; LD D,0; ADD HL,DE                 ; HL += (X - $E583)

EF6C..EF6F  EX AF, save+restore A
EF70  CP $07
EF72  JR Z,$EF79
EF74  LD DE,$F0F1                              ; level-color sprite
EF77  JR $EF9C

EF79  EX DE,HL; LD HL,$F071                    ; white sprite
EF7C..  more setup
```

Picks the sprite (`$F0F1` for level color, `$F071` for white)
then calls `$EF9C` to blit.

## `$EF9C` — 8-scanline cell blit

```
EF9C  PUSH HL
EF9D  LD B,$08
EF9F  LD A,(DE); LD (HL),A; INC H; INC DE
EFA3  DJNZ $EF9F                  ; 8 scanlines, INC H = +256 = next pixrow
EFA5  POP HL
EFA6  CALL $DB0E; LD H,A          ; HL → attr address
EFAA  EX AF,AF'; LD (HL),A; EX AF,AF'   ; write attr byte
EFAD  RET
```

Same blitter shape as `$E9AC` but for a single 8×8 cell instead
of a 16x16 sprite quartet.

## `$F02E` — mini-map dot toggle

Same overall shape as `$F1A5` ship dot draw but uses XOR/AND for
on/off cycling based on `($F070) bit 2`.  Workers appear as
**flashing dots** on the mini-map (versus ships which are steady).

```
F02E  LD B,$08; LD IX,$E75D
F035  PUSH BC
F036  LD A,$1F; LD B,(IX+$01); SLA B; SUB B; LD B,A    ; B = $1F - 2*row
F03F  LD C,(IX+$00); INC C                              ; C = X + 1
F043  LD A,($F070); BIT 2,A; JR Z,$F057                 ; cycle gate

F04A  BIT 7,(IX+$03)
F04E  JR NZ,$F05E                ; if picked-up, skip drawing
F050  CALL $E1E4; OR (HL); LD (HL),A     ; ON cycle: OR-draw
F055  JR $F05E

F057  CALL $E1E4; CPL; AND (HL); LD (HL),A  ; OFF cycle: AND-clear

F05E  ...; ADD IX,DE; DJNZ $F035
F066  ; advance ($F070) cycle counter
```

`$F070` cycles 0..7 every 2 frames (via `INC A; AND $07`).  Bit 2
of the counter alternates every 4 frames → workers blink at ~7
Hz.

## C# port status

Ported — `WorkerSchedule.cs`:

- `$E2E5` LDIR load from `level-schedules-e69d.bin`; 8 × 4-byte
  records; `$EFAE` 4-byte × 3-row pickup zone; `$EFE0` bit-5
  just-picked → bit-7 next frame (the one-frame freeze, drawn as
  the blank `$F0F1` stamp with level colour).
- Draw is the faithful OVERWRITE blit (`$EF9C LD (HL),A`, not
  XOR) of the single `$F071` sprite — dump-verified: `$F0F1` is
  8 zero bytes and nothing ever indexes past `$F078`, so an
  earlier port's "4-frame shovel-swing animation" was invention
  and is removed (RE-LOG §66).  The intra-frame level-colour/
  white attribute double-write is modelled as a per-frame
  shimmer.
- Mini-map dots flash via the `$F070` bit-2 gate at row
  `$A0 + 2·row`; ship dots stay steady.
- Rescue-all only sets the `$E77D[level]` cleared flag — the
  page advance stays behind the `$F868` dive gate in `World`.

## Related

- Sprite data at `$F0F1` / `$F071` — extract as small asset
- `$F016` rescue chime — port to `SfxKind.Pickup` or similar
- `$F922` all-rescued SFX — TBD
- `$E77D + level` = level-cleared flags
- `$E467` per-level rescue counter (bit 3 = 8-reached → cleared)
