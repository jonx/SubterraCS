# Entities — `$F1A5` / `$F1EF` / `$F1BC` / `$F2BC`

Partial.  Documents what we've decoded so far.  The per-type AI
subroutines (each entity kind's individual movement code) are
NOT yet decoded.

## `$F1A5` — entity dispatcher entry

```
F1A5  3A 93 F5    LD A,($F593)         ; A = 4-frame slice counter
F1A8  3C          INC A
F1A9  E6 03       AND $03
F1AB  32 93 F5    LD ($F593),A         ; counter = (counter+1) mod 4
F1AE  3A BB F1    LD A,($F1BB)         ; A = active entity count
F1B1  47          LD B,A
F1B2  DD 2A B9 F1 LD IX,($F1B9)        ; IX = active entity list base
F1B6  C3 D8 F1    JP $F1D8             ; jump to the per-entity loop
```

So `$F593` cycles 0..3 → different code paths in the per-entity
processor for different "time slices".

## `$F1BC` — load per-level entity list

```
F1BC  21 E2 F2    LD HL,$F2E2          ; per-level entity-COUNT table
F1BF  ED 5B 87 E5 LD DE,($E587)        ; DE = level
F1C3  16 00       LD D,$00
F1C5  19          ADD HL,DE             ; HL = $F2E2 + level
F1C6  7E          LD A,(HL)             ; A = entity count
F1C7  32 BB F1    LD ($F1BB),A          ; store as active count
F1CA  CB 23       SLA E                 ; E = level × 2
F1CC  21 94 F5    LD HL,$F594          ; per-level entity-LIST-PTR table
F1CF  19          ADD HL,DE             ; HL = $F594 + level*2
F1D0  5E          LD E,(HL); 23 INC HL; 56 LD D,(HL)
F1D3  ED 53 B9 F1 LD ($F1B9),DE          ; store as active list ptr
F1D7  C9          RET
```

**Per-level entity counts at `$F2E2`** (decoded from
build/post-game.bin): `06 0A 09 0D 12 19` — 6, 10, 9, 13, 18, 25
entities for levels 0..5.

**Per-level entity-list pointers at `$F594`** (12 bytes, 6 × 2):
`$F2E8, $F2EB, $F33B, $F383, $F3EB, $F47B`.

**Level 0 anomaly**: `$F2E8 → $F2EB` is only 3 bytes apart, so
level 0 either shares records with level 1 or uses a different
format.  Levels 1..5 are uniform 8-byte records.

## `$F1D8` — entity walker

```
F1D8  F3          DI
F1D9  C5          PUSH BC; DD E5 PUSH IX
F1DC  CD EF F1    CALL $F1EF           ; process one entity
F1DF  01 08 00    LD BC,$0008
F1E2  DD E1       POP IX
F1E4  DD 09       ADD IX,BC            ; advance to next entity (stride 8)
F1E6  C1          POP BC
F1E7  10 F0       DJNZ $F1D9
F1E9  FD 21 3A 5C LD IY,$5C3A
F1ED  FB          EI
F1EE  C9          RET
```

Confirms **8-byte stride per entity** matching the IX layout
documented in MEMORY-MAP §`$F1EF`.

## `$F1EF` — per-entity processor

```
F1EF  00          NOP
F1F0  FD 21 A0 F5 LD IY,$F5A0          ; entity-type table base
F1F4  DD 5E 00    LD E,(IX+$00)        ; type id
F1F7  16 00       LD D,$00
F1F9  CB 23       SLA E; CB 23 SLA E    ; type × 4 (each $F5A0 entry is 4 bytes)
F1FD  FD 19       ADD IY,DE             ; IY = $F5A0 + type*4
F1FF  DD 6E 02    LD L,(IX+$02)        ; frame index

F202  3A 93 F5    LD A,($F593); AND A
F206  JP NZ,$F213                       ; if slice != 0, skip frame advance
F209  LD A,L                            ; A = frame
F20A  LD H,(IY+$02)                     ; H = max_frames
F20D  DEC H
F20E  INC A; AND H                      ; frame = (frame+1) & (max-1)
F210  LD (IX+$02),A                     ; store advanced frame

F213  LD H,$00
F215  LD A,($F593); BIT 0,A
F21A  RET NZ                            ; if bit 0 of slice set, no draw

F21B  LD A,($E583); LD B,A
F21F  LD A,(IX+$01)                     ; A = record's +1 byte ("Y")
F222  SUB B                              ; A = recY - $E583
F223  CP $1F                             ; if (recY - $E583) ≥ 31, skip draw
F225  RET NC

F226  EX AF,AF'                         ; save offset in A'
F228  LD L,(IX+$02); LD H,$00
F22D  ADD HL,HL × 5                     ; HL = frame × 32 (bytes per frame)
F232  LD E,(IY+$00); LD D,(IY+$01)
F238  ADD HL,DE                          ; HL = sprite data ptr
...
F26D  LD L,(IX+$03); LD H,(IX+$04)      ; HL = TopAddr
F273  EX AF,AF'                          ; A = recY - $E583
F274  LD C,A; F275 EX AF,AF'; F276 LD B,$00
F278  ADD HL,BC                          ; HL = TopAddr + (recY - $E583)
F279  CALL $F2BC                         ; blit 8-row sprite column at HL
```

### KEY FINDING — entity gate + screen position formula

```
gate:       (rec.+1 − $E583) must be < $1F (else skip drawing)
screen_addr = TopAddr + (rec.+1 − $E583)
```

Verified facts (all from disassembly + reading `at-fXXX.bin`):

- Every `TopAddr` stored in the cassette has `x_byte = 0`
  (verified across all 5 playable levels).
- The record's `+1` byte is the entity's **world-byte position**
  (0..255 along the 256-byte-wide level).
- `$E583` is the **world-scroll cursor** updated by `$DB06`
  (called from both scroll routines `$DA54` / `$DA93`).
- The 32-byte visible window slides over the world as `$E583`
  increments; entities outside `[E583, E583+31]` are not drawn
  (and, since their +1 byte isn't transferred to any other
  location, are not collidable either).

### Worked example — level 1 record 0

```
record: type=$02 +1=$11 frame=$01 topAddr=$48A0 botAddr=$48C0 flags=$0d
```

At `$E583=0`: offset = 17 < 31 → drawn at `$48A0 + 17 = $48B1` →
pixel (136, 104).

At `$E583=18`: offset = -1 wraps as `($FF) → CP $1F` fails (NC)
→ entity hidden.  Record dormant until `$E583` decreases.

### Level 1 visibility table

| `$E583` value | Visible records | Notes |
| ------------- | --------------- | ----- |
| 0   | rec[0] (Y=17), rec[9] (Y=15) | starting view |
| 18  | rec[1] (Y=48) enters window | scroll right ~18 bytes |
| 53  | rec[2-4] (Y=83) enter | several scrolls later |
| 109 | rec[5-6] (Y=139) enter | further right |
| 149 | rec[7] (Y=179) enters | |
| 178 | rec[8] (Y=208) enters | near end of level |

## Per-level entity records — 8-byte format

From MEMORY-MAP §`$F1EF`, confirmed by reading level 1's records
at `$F2EB`:

| Offset | Meaning |
| ------ | ------- |
| +0     | Type id (index into the `$F5A0` table; × 4 → entry) |
| +1     | y coordinate (in some y-scaled unit) |
| +2     | Animation frame index |
| +3, +4 | Top-half screen address (Spectrum bitmap, lo/hi) |
| +5, +6 | Bottom-half screen address |
| +7     | Flag / facing byte |

Native port extracted these to `assets/extracted/level-entities-f2e8.bin`
and decodes them via `LevelEntities.cs`.
