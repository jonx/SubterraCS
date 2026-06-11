# Level load — `$F6F2` and the nine helpers

`$F6F2` is the entry point for "advance to the next level".  It
increments the level counter and calls nine helpers in sequence
to set up the next level's state, scenery, and entities.

## `$F6F2` — level-advance entry

```
F6F2  00          NOP                    ; padding
F6F3  3A 87 E5    LD A,($E587)           ; A = current level
F6F6  3C          INC A                  ; level++
F6F7  FE 06       CP $06                 ; reached 6?
F6F9  20 01       JR NZ,$F6FC
F6FB  AF          XOR A                  ; wrap to 0
F6FC  32 87 E5    LD ($E587),A           ; store

F6FF  5F          LD E,A; 16 00 LD D,$00
F702  21 7C E5    LD HL,$E57C            ; per-level speed/colour table
F705  19          ADD HL,DE              ; HL = $E57C + level
F706  7E          LD A,(HL)
F707  32 7B E5    LD ($E57B),A           ; active colour byte

F70A  21 01 00    LD HL,$0001
F70D  22 74 EE    LD ($EE74),HL          ; reset scroll counter
F710  AF          XOR A
F711  32 84 E5    LD ($E584),A           ; altitude = 0

F714  CD 19 E3    CALL $E319             ; copy 32-byte init data
F717  CD E5 E2    CALL $E2E5             ; copy spawn schedule
F71A  AF          XOR A
F71B  32 7C EE    LD ($EE7C),A           ; clear $EE7C
F71E  32 67 E4    LD ($E467),A           ; clear $E467
F721  3C          INC A
F722  32 85 E5    LD ($E585),A           ; speed shift = 1

F725  CD C6 E2    CALL $E2C6             ; load per-level pointers
F728  CD 47 E3    CALL $E347             ; clear bottom half + paint HUD
F72B  CD 9B E2    CALL $E29B             ; clear live entity tables (below)
F72E  CD 9F F9    CALL $F99F             ; per-level fanfare (sound.md)
F731  CD 1A DB    CALL $DB1A             ; paint level scenery (see level-paint.md)
F734  CD 91 F8    CALL $F891             ; blank the player spawn cell (below)
F737  CD 5D DC    CALL $DC5D             ; player attribute paint
F73A  C9          RET
```

## `$E319` — copy 32-byte per-level init data

```
E319  21 8D E4    LD HL,$E48D            ; per-level init-data base
E31C  3A 87 E5    LD A,($E587)
E31F  07 07 07 07 07  RLCA × 5           ; A = level << 5 (level * 32)
E324  5F          LD E,A; 16 00 LD D,$00
E327  19          ADD HL,DE              ; HL = $E48D + level*32
E328  11 97 E5    LD DE,$E597            ; destination
E32B  01 20 00    LD BC,$0020            ; 32 bytes
E32E  ED B0       LDIR                   ; copy
E330  AF          XOR A
E331  32 F9 E8    LD ($E8F9),A
E334  CD BC F1    CALL $F1BC             ; load per-level entity list (see entities.md)
E337  C9          RET
```

**Source**: `$E48D` + level*32 — a 6×32 = 192-byte table of
per-level "init data" (purpose still TBD — format looks like
4-byte records, possibly object placements).

## `$E2C6` — load per-level pointers

```
E2C6  ED 5B 87 E5 LD DE,($E587)
E2CA  CB 23       SLA E                  ; level × 2
E2CC  16 00       LD D,$00
E2CE  21 6D E5    LD HL,$E56D            ; per-level sprite-pointer table
E2D1  19          ADD HL,DE              ; HL = $E56D + level*2
E2D2  4E          LD C,(HL); 23 INC HL; 46 LD B,(HL)
E2D5  ED 43 79 E5 LD ($E579),BC          ; active sprite-composition base

E2D9  21 8B E5    LD HL,$E58B            ; per-level second pointer table
E2DC  19          ADD HL,DE
E2DD  4E          LD C,(HL); 23 INC HL; 46 LD B,(HL)
E2E0  ED 43 89 E5 LD ($E589),BC          ; active second pointer

E2E4  C9          RET
```

Sets `($E579)` (the per-level sprite-composition base) to the
pointer for the active level — for level 1 this is `$60F4`, which
becomes the IX source in `$DB1A`.

## `$E2E5` — copy spawn schedule

```
E2E5  2A 87 E5    LD HL,($E587)
E2E8  26 00       LD H,$00
E2EA  29 29 29 29 29  ADD HL,HL × 5      ; HL = level * 32
E2EF  11 9D E6    LD DE,$E69D            ; spawn schedule table base
E2F2  19          ADD HL,DE              ; HL = $E69D + level*32
E2F3  11 5D E7    LD DE,$E75D            ; active schedule destination
E2F6  01 20 00    LD BC,$0020            ; 32 bytes
E2F9  ED B0       LDIR                   ; copy
E2FB  C9          RET
```

Copies the 32-byte per-level spawn schedule from `$E69D + level*32`
to the active scratch at `$E75D`.  See MEMORY-MAP §`$E69D`.

## `$E347` — clear + paint HUD chrome

Decoded in [`hud.md`](hud.md) (top of file).  Walks the `$E785`
string table through `RST 10`.

## More level-load helpers

### `$E29B` — clear all live entity tables

Called from `$F72B`.  Wipes the three dynamic tables and clears
the locks:

```
E29B  XOR A
E29C  LD HL,$E46B; LD DE,$E46C; LD BC,$001F; LDIR  ; clear $E46B..$E48A (player bullets)
E2A8  LD HL,$E8D1; LD DE,$E8D2; LD BC,$001F; LDIR  ; clear $E8D1..$E8F0 (player undo buffer)
E2B4  LD HL,$EE9E; LD DE,$EE9F; LD BC,$0023; LDIR  ; clear $EE9E..$EEC1 (enemy bullets)
E2C0  LD ($E583),A                                    ; scroll cursor = 0
E2C3  JP $E2FC                                        ; (extends to boss-flag clear)
```

`$E2FC..` continues to clear `$EE7C` (boss-active) plus
`$E8A1..` (more state).  Effectively a "fresh start" wipe for
all moving entities/projectiles when a new level loads.

### `$F891` — blank the player spawn cell

Called from `$F734` (and from `$F6CB` right after the `$E135`
spawn-in).  Prints a 12-byte stream at `$F89C`:
`PAPER 0; AT 0,15; "  "; AT 1,15; "  "` — blanks the 2×2 char
block at rows 0–1, cols 15–16.  That is exactly the cell the
player Stryker occupies at spawn (screen X=120..135, altitude 0),
so this guarantees the ship XOR-draws onto a clean black
background — no leftover menu text or scenery to trigger a
spurious `$DCF5` overlap-collision on frame one.

### `$F99F` — per-level fanfare

Called from `$F72E`.  Plays per-level music — full trace in
[sound.md](sound.md).

### `$DC5D` — player attribute paint

Called from `$F737`.  Sets up the player's attribute pattern
($43 = bright yellow on the player's quadrant cells).
Documented inline at the top of [player.md](player.md).

### `$E2C6` — per-level pointer load

Called from `$F725` (already documented above):
- `($E579) ← $E56D[level*2]` = level scenery base
- `($E589) ← $E58B[level*2]` = secondary level pointer (pickup target?)

### `$F8B4` — fuel-low warning SFX

Called by `$D879` when fuel drops below `$20`.  Dispatches a
19-byte alert at `$F8C5` via `$FA0A` — see [sound.md](sound.md).
