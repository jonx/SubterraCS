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

## `$F1EF` — per-entity processor (PARTIAL)

```
F1EF  00          NOP
F1F0  FD 21 A0 F5 LD IY,$F5A0          ; entity-type table base
F1F4  DD 5E 00    LD E,(IX+$00)        ; type id
F1F7  16 00       LD D,$00
F1F9  CB 23       SLA E; CB 23 SLA E    ; type × 4 (each F5A0 entry is 4 bytes)
F1FD  FD 19       ADD IY,DE             ; IY = $F5A0 + type*4
F1FF  DD 6E 02    LD L,(IX+$02)        ; frame index
F202  3A 93 F5    LD A,($F593)         ; slice counter (from $F1A5)
F205  A7          AND A
F206  C2 13 F2    JP NZ,$F213          ; if slice != 0, skip to $F213
F209  7D          LD A,L               ; A = frame
... (not yet decoded — branches into per-type AI based on slice
       and on frame index)
```

The branch on slice counter (`$F593`) at `$F206` means each
entity gets DIFFERENT code per slice — implementing the 4-frame
time-slicing of entity AI / animation.

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
