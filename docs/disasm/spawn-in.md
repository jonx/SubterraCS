# Spawn-in animation — `$E135` and the `$F6C7` respawn loop

The "dots fly in to form the ship" animation that runs every
time the player enters a level — at the start of a new level AND
on every respawn after death.  Sister of [death.md](death.md)
(both use the same `$E199` painter and the same 8-particle
attribute mechanism, but with different seed tables and motion).

## The outer respawn loop — `$F6C7..$F6EF`

```
F6C4  CD F2 F6    CALL $F6F2      ; level-load (level-load.md)
F6C7  00          NOP
F6C8  CD 35 E1    CALL $E135      ; ★ SPAWN-IN ANIMATION ★
F6CB  CD 91 F8    CALL $F891      ; print "LEVEL N" + score line
F6CE  CD 5D DC    CALL $DC5D      ; player attribute paint
F6D1  CD F7 D7    CALL $D7F7      ; ← enter main loop (= $D7FB body)
F6D4  3E 01       LD A,$01
F6D6  32 86 E5    LD ($E586),A    ; facing = 1 (right)
F6D9  3A 88 E5    LD A,($E588)
F6DC  3D          DEC A           ; lives--
F6DD  CA 3B F7    JP Z,$F73B      ; if was 1 → game over
F6E0  32 88 E5    LD ($E588),A    ; store lives
F6E3  CD 19 E3    CALL $E319      ; reset $E597 ship table from $E48D
F6E6  CD 9B E2    CALL $E29B      ; clear $EE9E bullets + boss
F6E9  CD 47 E3    CALL $E347      ; repaint HUD chrome
F6EC  CD 1A DB    CALL $DB1A      ; ★ repaint scenery ★
F6EF  C3 C7 F6    JP $F6C7        ; LOOP back to spawn-in
```

`$F6F2` only runs ONCE — at the very first level entry from
`$F610`'s title-menu chain.  After that, the loop at
`$F6C7..$F6EF` runs forever:

1. **`$E135`** — 40-frame spawn-in animation
2. **`$F891`** — print level/score line in the HUD strip
3. **`$DC5D`** — paint the 4 player-attribute cells
4. **`$D7F7`** — enter the main game loop (= `$D7FB`'s body)
   - On RETURN (via `$D8A8` death restoring SP), control falls
     through to F6D4
5. **F6D4..F6EF** — post-death restore: facing, lives--,
   per-level data reset, HUD repaint, scenery repaint
6. **`JP $F6C7`** — back to step 1, with a fresh spawn-in

So the spawn-in fires:
- Once per **level start** (after `$F6F2`'s level-load)
- Once per **respawn** (after each `$DBC8 → $D8A8` death cycle)

The level scenery is repainted (`$DB1A`) AFTER the lives
decrement, BEFORE the next iteration's spawn-in — so the user
sees: black/dim → scenery paints in → dots fly in → ship
appears (= main loop drawing `$DCF5`).

## `$E135` — the spawn-in routine

```
E135  00          NOP
E136  21 41 E8    LD HL,$E841     ; ★ SPAWN seed table (32 bytes)
E139  11 81 E8    LD DE,$E881     ; live particle scratch
E13C  01 20 00    LD BC,$0020
E13F  ED B0       LDIR            ; copy seeds → live

E141  CD 99 E1    CALL $E199      ; initial paint
E144  06 28       LD B,$28        ; 40 outer iterations
E146  C5          PUSH BC
E147  3A 7B E5    LD A,($E57B)    ; level colour
E14A  08          EX AF,AF'
E14B  CD 99 E1    CALL $E199      ; paint particles with level colour

; step each of 8 particles by (DX, DY)
E14E  DD 21 81 E8 LD IX,$E881
E152  06 08       LD B,$08
E154  11 04 00    LD DE,$0004
E157  00          NOP
E158  DD 7E 00    LD A,(IX+$00); ADD A,(IX+$02); LD (IX+$00),A   ; x += dx
E161  DD 7E 01    LD A,(IX+$01); ADD A,(IX+$03); LD (IX+$01),A   ; y += dy
E16A  DD 19       ADD IX,DE
E16C  10 E9       DJNZ $E157

E16E  3E 07       LD A,$07        ; white
E170  08          EX AF,AF'
E171  CD 99 E1    CALL $E199      ; paint particles white

; pseudo-random sound (one beep per outer iteration, pitch from $2Axx table)
E174  ED 5F       LD A,R; LD L,A; LD H,$2A      ; HL = $2A00 + R-register low
E179  C1          POP BC; PUSH BC
E17B  CB C0       SET 0,B                       ; force B odd
E17D  3E 10       LD A,$10
E17F  D3 FE       OUT ($FE),A
E181  EE 10       XOR $10                       ; flip speaker bit
E183  4E          LD C,(HL); CB B9 RES 7,C      ; C = low 7 bits of (HL)
E186  23          INC HL
E187  0D          DEC C; 00 00 NOP×2
E18A  20 FB       JR NZ,$E187                   ; delay loop
E18C  D3 FE       OUT ($FE),A
E18E  10 EF       DJNZ $E17F

E190  C1          POP BC
E191  10 B3       DJNZ $E146      ; next outer iteration

E193  0E 06       LD C,$06; CD 77 E2 CALL $E277  ; setup post-spawn-in
E198  C9          RET
```

40 iterations of: paint level-colour → step → paint white → beep.
Same 3-stage colour alternation as `$DBDA` (death).  Total visible
duration ≈ 40 frames + sound delays ≈ 1 second.

## The spawn-in seed table — `$E841`

8 records × 4 bytes (X, Y, DX, DY), with **Y in bus-counter
space** (visualY = `$BF - storedY`) and **DY also in bus-counter
space** (visualΔY = `-DY`, so positive cassette DY moves UP):

| # | Cassette bytes | Cassette decoded | Port-screen decoded (Y, DY negated) |
|---|----------------|------------------|--------------------------------------|
| 1 | `30 BC 02 00`  | X=48, sY=188, DX=+2, DY=0   | X=48,  Y=3,   DX=+2, DY=0   |
| 2 | `58 44 01 03`  | X=88, sY=68,  DX=+1, DY=+3  | X=88,  Y=123, DX=+1, DY=-3  |
| 3 | `08 94 03 01`  | X=8,  sY=148, DX=+3, DY=+1  | X=8,   Y=43,  DX=+3, DY=-1  |
| 4 | `30 6C 02 02`  | X=48, sY=108, DX=+2, DY=+2  | X=48,  Y=83,  DX=+2, DY=-2  |
| 5 | `D0 94 FE 01`  | X=208,sY=148, DX=-2, DY=+1  | X=208, Y=43,  DX=-2, DY=-1  |
| 6 | `F8 6C FD 02`  | X=248,sY=108, DX=-3, DY=+2  | X=248, Y=83,  DX=-3, DY=-2  |
| 7 | `D0 44 FE 03`  | X=208,sY=68,  DX=-2, DY=+3  | X=208, Y=123, DX=-2, DY=-3  |
| 8 | `D0 BC FE 00`  | X=208,sY=188, DX=-2, DY=0   | X=208, Y=3,   DX=-2, DY=0   |

So 4 particles start on the left edge (X ∈ {8, 48, 88}) and 4 on
the right (X ∈ {208, 248}); 2 are at the top (Y=3) and 2 at the
bottom (Y=123), the rest mid-vertical.  All horizontal DX point
INWARD (left-side particles move right; right-side move left).
Vertical DY varies — top particles stay flat, mid particles
drift up.

## Side-by-side with the death-animation seed table `$E861`

```
E861  80 00 00 FF  80 00 01 FE  80 00 02 00  80 00 02 03
E871  80 00 00 02  80 00 FF 02  80 00 FD 00  80 00 FE FD
```

All death particles start at **X=$80 (mid-screen)** with **Y=$00**
(which `$DBE5..$DBF7` overrides to `$BF - altitude` so they emanate
from the player's current Y).  Only (DX, DY) differs per record:

| # | Cassette DX, DY | Port-screen DX, DY |
|---|-----------------|---------------------|
| 1 | (0, -1)         | (0, +1)   down     |
| 2 | (+1, -2)        | (+1, +2)  right+down |
| 3 | (+2, 0)         | (+2, 0)   right     |
| 4 | (+2, +3)        | (+2, -3)  right+up |
| 5 | (0, +2)         | (0, -2)   up        |
| 6 | (-1, +2)        | (-1, -2)  left+up  |
| 7 | (-3, 0)         | (-3, 0)   left      |
| 8 | (-2, -3)        | (-2, +3)  left+down |

Asymmetric (no uniform magnitude); the port previously used a
clean ±2 fan that didn't match the cassette's distinct radial
character.

## C# port mapping

| Cassette | C# location | Notes |
| -------- | ----------- | ----- |
| `$E135` | `Explosion.TriggerSpawnIn` + 40-frame `Tick` loop | seeds match `$E841` (port pre-inverts Y/DY) |
| `$DBC8`/`$DBDA` death | `Explosion.Trigger` + 64-frame `Tick` loop | seeds match `$E861` (DY pre-negated); `TickDying` now runs the cassette's FOUR passes + the `$DC43` dim (RE-LOG §66) |
| `$F731 CALL $DB1A` (level slide-in) | `World.TickPlaying` drives `Scroll.ScrollOneStep` over 60 frames using `StateTicks` | each `ScrollOneStep` ports one outer iteration of $DB1A (scroll up + paint new bottom row) |
| `$F6EC CALL $DB1A` (respawn slide-in) | `World.Respawn` calls `Scroll.Reset()`; TickPlaying loop replays the slide-in | matches cassette's $F6EC..$F6EF JP $F6C7 loop |
| `$F6C8 CALL $E135` (level start spawn-in) | `World.TickPlaying` fires `TriggerSpawnIn` on the frame `Scroll.ScrollComplete` flips true | matches the cassette flow $F731 returns → $F6C8 calls $E135 |
| `$F6EF JP $F6C7 → $F6C8` (every respawn spawn-in) | Same — slide-in completes after respawn → triggers spawn-in | |
| Entity / worker / ship / bullet draws gated until `Scroll.ScrollComplete` | `DrawPlaying` wraps the entity foreach + ship/worker/boss/bullet draws | matches cassette's $D7F7 main loop NOT starting until $E135 returns at $F6CB; $F1A5 entity dispatcher first runs from $D80A in the loop body |
| Ship sprite hidden during spawn-in | `hidePlayer` includes `Explosion.Spawning` | port of the cassette flow where `$DCF5` doesn't run until after `$E135` returns |
| `$DC1D` Y < $41 cull (death) | `Explosion.Tick`: `if (!_converge && Y > 126) continue;` | bus-counter inversion was previously misread as `Y < 0x41` in port-screen, freezing wrong half |
| `$E199` particle painter | `Explosion.Draw` 8-cell attribute paint | bitmap untouched (cassette only flashes attributes) |
| `$E17F..$E18E` per-iteration beep | (not ported) | cassette plays one pseudo-random pitch per outer iteration; port uses Sfx triggers elsewhere |
| `$F891` "LEVEL N" print | (not ported) | could add a 1-second "LEVEL N" banner during spawn-in |
| `$DC5D` player attribute paint | (handled inline in player draw) | |

## History

The spawn-in port was originally written as a converge animation
but had three faithfulness bugs vs the cassette:

1. **Death velocities were a clean ±2 fan** instead of the
   asymmetric `$E861` pattern.  Fixed by extracting actual
   cassette bytes and pre-negating DY for port-screen Y.
2. **Spawn-in only fired on `LoadLevel`, not `Respawn`.**  Per
   `$F6EF JP $F6C7 → $F6C8`, cassette re-fires `$E135` on every
   respawn — added `Explosion.TriggerSpawnIn` to `Respawn`.
3. **The player was drawn during spawn-in** (only hidden during
   the initial scroll-in).  Cassette doesn't run `$DCF5` until
   after `$E135` returns and the main loop starts.  Added
   `Explosion.Spawning` to the `hidePlayer` gate.
4. **The death-particle floor cull at `$DC1D` was misread.**  The
   cassette's `LD A,(IX+1); CP $41; JR C,$DC31` skips a
   particle's X/Y step when storedY < $41 — which in bus-counter
   space means visualY > $7E (below playfield).  The port had
   the literal `Y < 0x41` check, freezing the upper half instead.

5. **The level-start sequencing was wrong.**  Cassette is strictly
   sequential per `$F6F2..$F6CB`: slide-in scenery → spawn-in dots
   → main loop (ship + entities draw).  The port had the slide-in
   gated by a 140-frame global delay (`_frameCounter >= 140`),
   triggered spawn-in concurrently with the slide-in (= dots
   converging into empty cave), and drew entities + workers + ships
   + bullets from frame 0 even before scenery existed.  Also,
   `Respawn` skipped the slide-in entirely.  Fixed by:
   - Driving the slide-in off `StateTicks` (per-state entry) so it
     starts on every Playing-state entry (initial level + every
     respawn) and runs over 60 frames.
   - Firing `TriggerSpawnIn` from `TickPlaying` exactly when the
     slide-in completes (matches `$F731 → $F6C8`).
   - Resetting `Scroll` in `Respawn` so the slide-in replays after
     death (matches `$F6EC CALL $DB1A`).
   - Wrapping entity / worker / ship / bullet draws in
     `if (Scroll.ScrollComplete)` so they only appear once the
     main loop would have started in the cassette.
   - Removing the redundant `Explosion.TriggerSpawnIn` calls from
     `LoadLevel` and `Respawn` (slide-in completion handles it).
