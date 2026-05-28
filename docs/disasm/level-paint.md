# Level paint — `$DB1A` / `$DB7A` / `$DAF2` / `$E104`

The chain that turns the per-level packed buffer at `$60F4`+
into the on-screen scenery (hillside, tree, surface, mini-map).

## `$DB1A` — level paint outer loop

Called once per level from `$F6F2`'s level-load chain (at `$F731`)
and once at game-start from `$F6EC`.  Reads the per-level sprite
pointer, then runs 16 iterations of {scroll up; draw bottom char
row; advance source pointer}.

```
DB1A  21 6D E5    LD HL,$E56D        ; per-level sprite-pointer table base
DB1D  ED 5B 87 E5 LD DE,($E587)      ; DE = level number (word)
DB21  3A 7B E5    LD A,($E57B)       ; A = per-level colour byte
DB24  08          EX AF,AF'           ; save in A'
DB25  CB 23       SLA E              ; E = level * 2
DB27  16 00       LD D,$00
DB29  19          ADD HL,DE          ; HL = $E56D + level*2
DB2A  5E          LD E,(HL)
DB2B  23          INC HL
DB2C  56          LD D,(HL)          ; DE = per-level pointer (e.g. $60F4 for L1)
DB2D  D5          PUSH DE
DB2E  DD E1       POP IX             ; IX = source pointer

DB30  06 10       LD B,$10           ; OUTER LOOP: 16 char rows
DB32  C5          PUSH BC
                                      ; --- per-iteration sound: scaled beep
DB33  3A 87 E5    LD A,($E587); LD C,A
DB37  3E 0A       LD A,$0A; SUB C
DB3A  CB 27       SLA A; SLA A        ; A = (10-level)*4 — beep length
DB3E  CB 21       SLA C               ; C = level*2
DB40  4F          LD C,A
DB41  AF          XOR A; OUT ($FE),A  ; speaker low
DB44  EE 10       XOR $10             ; A = $10
DB46  41          LD B,C
DB47  10 FE       DJNZ $DB47         ; delay
DB49  0D          DEC C
DB4A  20 F6       JR NZ,$DB42        ; loop
DB4C  AF          XOR A; OUT ($FE),A  ; speaker off
                                      ; --- the actual paint
DB4F  CD 7A DB    CALL $DB7A          ; scroll bitmap + attrs UP one char row
DB52  11 E0 48    LD DE,$48E0         ; DE = bitmap address of bottom char row
                                      ;     ($48E0 = pixel (0,120))
DB55  06 20       LD B,$20            ; INNER LOOP: 32 cols
DB57  C5          PUSH BC
DB58  D5          PUSH DE
DB59  DD E5       PUSH IX
DB5B  E1          POP HL              ; HL = IX (current source pointer)
DB5C  CD F2 DA    CALL $DAF2          ; blit one tile (HL → DE)
DB5F  DD 23       INC IX              ; advance source by 1 tile-index byte
DB61  D1          POP DE
DB62  13          INC DE              ; advance dest by 1 column
DB63  C1          POP BC
DB64  10 F1       DJNZ $DB57          ; loop 32 cols

DB66  21 E0 59    LD HL,$59E0         ; attr address: last char row col 0
DB69  06 20       LD B,$20            ; 32 cells
DB6B  08          EX AF,AF'           ; restore colour from A'
DB6C  77          LD (HL),A
DB6D  23          INC HL
DB6E  10 FC       DJNZ $DB6C          ; paint 32 attr cells with colour
DB70  08          EX AF,AF'

DB71  01 E0 00    LD BC,$00E0         ; +224 (stride padding past the 32 indices)
DB74  DD 09       ADD IX,BC           ; advance source pointer by 224
DB76  C1          POP BC
DB77  10 B9       DJNZ $DB32          ; outer loop
DB79  C9          RET
```

**Total source bytes per outer iteration**: 32 (consumed by INC IX
× 32) + 224 (stride) = **256**.  Total per level: 16 × 256 = 4096
— exactly the per-level buffer size.

**Layout**: char row R (0-indexed from the top, after the 16
scrolls have completed) gets its tile indices from
`($60F4) + R * 256 .. + R * 256 + 31`.  This is because iter 1 is
the first drawn (and so is scrolled up 15 times to end at row 0);
iter K ends at row K-1.

**Verified empirically**: at f200 in the emulator, char row 2
cols 21..24 use tiles 161, 164, 165, 168.  The bytes at
`$60F4 + 2*256 + 21..24` are `$A1, $A4, $A5, $A8` — exact match.

**Called by**: `$F6EC`, `$F731` (both within the level-load chain).
**Calls**: `$DB7A` (scroll), `$DAF2` (tile blit).

## `$DB7A` — scroll bitmap + attrs UP one char row

Called from `$DB1A` at every outer iteration.  Scrolls the play
area (y=0..127 plus attrs rows 0..23) UP by 8 scanlines = one
char row.

```
DB7A  21 20 58    LD HL,$5820        ; attr addr at row 1 col 0
DB7D  11 00 58    LD DE,$5800        ; attr addr at row 0 col 0
DB80  01 E0 01    LD BC,$01E0        ; 32 × 15 = 480 bytes (rows 1..15)
DB83  ED B0       LDIR                ; scroll attributes UP 1 row
                                      ;   (rows 1..15 → rows 0..14;
                                      ;    row 15 untouched)

DB85  21 00 40    LD HL,$4000        ; bitmap addr (band 0 top)
DB88  11 20 40    LD DE,$4020        ; bitmap addr ONE CHAR ROW DOWN
DB8B  E5          PUSH HL; D5 PUSH DE
DB8D  0E 0F       LD C,$0F            ; 15 char-row iterations
DB8F  06 20       LD B,$20            ; 32 byte cols
DB91  1A          LD A,(DE)           ; read src
DB92  77          LD (HL),A           ; write dst (bitmap byte moves up)
DB93  79          LD A,C; AND $07; CP $01
DB98  20 02       JR NZ,$DB9C        ; if (C & 7) != 1, skip the zeroing
DB9A  97          SUB A; LD (DE),A    ; zero the source (every 8th iteration)
DB9C  23          INC HL; INC DE
DB9E  10 F1       DJNZ $DB91          ; loop 32 cols
DBA0  0D          DEC C
DBA1  28 12       JR Z,$DBB5          ; row done
DBA3  79          LD A,C; AND $07; AND A
DBA7  28 16       JR Z,$DBBF         ; (handles band-crossing — see below)
DBA9  FE 07       CP $07
DBAB  20 E2       JR NZ,$DB8F        ; next char-row column iteration
DBAD  D5          PUSH DE
DBAE  11 00 07    LD DE,$0700
DBB1  19          ADD HL,DE
DBB2  D1          POP DE
DBB3  18 DA       JR $DB8F            ; jump-back for next col

DBB5  D1          POP DE; E1 POP HL
DBB7  14          INC D; INC H        ; advance both pointers by one scanline
DBB9  7C          LD A,H; FE 48 CP $48
DBBC  20 CD       JR NZ,$DB8B         ; loop until H == $48 (end of band 0)
DBBE  C9          RET
DBBF  E5          PUSH HL
DBC0  21 00 07    LD HL,$0700; 19 ADD HL,DE
DBC4  EB          EX DE,HL; E1 POP HL
DBC6  18 C7       JR $DB8F            ; band-cross adjust for DE
```

The `+$0700` jumps at `$DBAD` and `$DBBF` are how the routine
crosses Spectrum bitmap band boundaries (offsets $0800 within
the bitmap = band-2 start).

**Called by**: `$DB1A`.
**Touches**: `$4000..$47FF` (band 0 bitmap), `$5800..$59DF`
(attribute area minus the last row).

## `$DAF2` — tile blit (8 bytes from master bank)

The actual pixel-data copy.  Reads a tile *index* from `(HL)`,
multiplies by 8, adds `$B0F4` to get the tile's 8-byte data, and
copies it scanline-by-scanline to the screen address in DE.

```
DAF2  ; (entry — preserves IX)
DAF3  6E          LD L,(HL)           ; L = tile index
DAF4  26 00       LD H,$00
DAF6  29          ADD HL,HL            ; HL = idx × 2
DAF7  29          ADD HL,HL            ; × 4
DAF8  29          ADD HL,HL            ; × 8 (bytes per tile)
DAF9  01 F4 B0    LD BC,$B0F4         ; master tile-bank base
DAFC  09          ADD HL,BC           ; HL = $B0F4 + idx × 8
DAFD  06 08       LD B,$08            ; 8 scanlines
DAFF  7E          LD A,(HL)
DB00  12          LD (DE),A           ; copy scanline byte to screen
DB01  14          INC D                ; advance to next scanline (Spectrum interleave: INC H within a band moves down one scanline)
DB02  23          INC HL
DB03  10 FA       DJNZ $DAFF
DB05  C9          RET
```

Note `INC D` advances the DESTINATION pointer by `$0100` —
exactly one scanline within a char-row band in Spectrum's
interleaved layout.

**Called by**: `$DB1A` (level paint), entity dispatcher
($F1EF), HUD draw ($E046).
**Reads**: master tile bank at `$B0F4`.
**Writes**: bitmap at DE.

## `$E104` — mini-map walker (separate from the level paint)

NOT part of the level-paint chain — this is the routine that
paints the bottom-strip mini-map (y=160..191) by walking the
same `$60F4..$70F4` buffer.  Reads each non-zero source byte and
ORs a pixel into the screen.

Documented elsewhere; see [the mini-map section in MEMORY-MAP.md](../MEMORY-MAP.md)
and the [`MiniMap.cs`](../../native/SubterraCS.Core/MiniMap.cs)
port.

## Data tables touched

| Address | Purpose | Stride |
| ------- | ------- | ------ |
| `$E56D` | Per-level sprite-pointer table | 2 bytes × 6 levels |
| `$E57B` | Active per-level colour byte (set by `$F6F2` from `$E57C+level`) | 1 byte |
| `$E587` | Current level number (word; high byte usually 0) | 2 bytes |
| `$60F4` | Level 1's packed scenery + mini-map data | 4096 bytes |
| `$70F4` | Level 2's packed data | 4096 |
| `$80F4` | Level 3's | 4096 |
| `$90F4` | Level 4's | 4096 |
| `$A0F4` | Level 5's | 4096 |
| `$B0F4` | Level 0's data (overlaps with master tile bank) | 4096 |
| `$B0F4` | Master tile bank | 384 tiles × 8 bytes |
