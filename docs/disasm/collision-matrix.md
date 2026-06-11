# Collision matrix — who hits what

All interactions in the cassette where one entity tests against
another and acts on overlap.  Cross-references the dedicated MD
files where each routine is fully traced.

## The matrix

| Subject \\ Object | Player | Enemy ship | Enemy bullet | Boss | Worker | Decor | Scenery (cave) |
| ----------------- | ------ | ---------- | ------------ | ---- | ------ | ----- | -------------- |
| **Player ship**   | —      | `$EB7A`    | `$EDC0`      | `$EDC0`† | `$EFAE` pickup | `$DCF5` XOR overlap only | `$DFAF` `$EB62` |
| **Player laser**  | —      | (PORT only)| —            | (PORT only) | bullet-proof | (PORT only) | `$DEDA` self-limit |
| **Enemy ship**    | `$EB7A`| —          | —            | —    | —      | —     | `$EB5B` `$EB62` reverse |
| **Enemy bullet**  | `$EDC0`| —          | —            | —    | —      | —     | `$EB62` expire |
| **Boss**          | `$EDC0`†| —         | —            | —    | —      | —     | `$EB62` |

† = my reading; boss probably uses `$EDC0`-style address-match
since `$EC4C` calls `$E9AC` for draw (same as ships).

## Investigation note — System A entities ARE NOT damage sources

Per the cassette's `$DD4D` collision walker:
```
DD4D  LD HL,($E583); ADD A,$0F; LD ($EE76),HL  ; player position cache
DD58  test BOSS at $EE7D via $DD8C
DD65  iterate 7 SHIPS at $E597 via $DD8C
DD79  iterate 6 BULLETS at $EE9E via $DDAA
DD8B  RET
```

`$DD4D` only tests SHIPS, BULLETS, and the BOSS — not System A
entities at `$F2EB+`.  So in the original, **decor entities
(trees, pipes, even electric arcs) do NOT damage the player on
contact via this routine**.

The electric arc's damage in the original probably comes via
a DIFFERENT mechanism — possibly the `$DCF5` player draw's
XOR-collision-flag (= when the player sprite XORs into a non-zero
bitmap pixel, sets a flag that fires `$DD4A`).  Arcs would set
bits in the bitmap; player flying through would XOR-collide.

The C# port extends `EntityAI.Kind.ElectricArc` with explicit
`CollisionRule(-15, 0, 0, 0, false)` to give the arc its
"door-blocker" damage role.

## Detailed traces

### Player-vs-ENEMY-SHIP — `$EB7A`

```
EB7A  PUSH HL; PUSH DE; PUSH AF
EB7D  LD DE,$E8C9             ; player's 4 quadrant addresses
EB80  LD B,$04
EB82  LD A,(DE); CP L         ; address low byte match?
EB84  JR NZ,$EB8C
EB86  INC DE; LD A,(DE); DEC DE; CP H   ; address high byte match?
EB8A  JR Z,$EB94
EB8C  INC DE × 2; DJNZ $EB82
EB90  POP AF; POP DE; POP HL; RET
EB94  CALL $DD4A              ; HIT → fire $DDC4 hit chain
EB97  JR $EB90
```

Called from inside the ship-AI loop (`$E920` chain).  Compares
the SHIP'S currently-drawn screen address against each of the
player's 4 quadrant addresses.  If any match → `$DD4A` (= sound
+ shield drain + possible death).

### Player-vs-ENEMY-BULLET — `$EDC0`

Identical structure to `$EB7A` but called from `$ED01` per-frame
bullet tick.  Mirrors a bullet's screen address against
`$E8C9`; on match → `$DD4A`.  See [enemies.md](enemies.md).

### Player-vs-WORKER — `$EFAE` (pickup zone)

```
EFAE  LD A,($E583); ADD $0E      ; player byte = scroll+14
EFB3..EFC4  4-wide horizontal window check
EFC9  LD A,($E584); SRL ×3        ; player char-row
EFD2..EFDB  3-tall vertical window check
```

Wider zone than damage collisions (= 32 px × 24 px) so the
player doesn't need pixel-precision to grab a worker.  On hit,
`$EFE0` fires (+50 score, RESCUED++).  See
[workers.md](workers.md).

### Player-vs-SCENERY — `$DFAF` + `$EB62`

```
DFAF  LD HL,($E583); ADD $0F; LD ($EE76),HL
DFBC  CALL $EB62                  ; probe scenery at (player_byte, player_y)
DFC0  LD HL,$EE76; INC (HL)
DFC5  CALL $EB62                  ; probe at (player_byte+1, player_y)
DFC8  CP $01
DFCA  CALL Z,$DFEE                ; if probed tile == 1, → $DBC8 (DEATH)
```

`$EB62` is the per-pixel tile-index probe at the world position.
A returned tile value of `$01` = "solid wall" → death.  Player
probes 2 bytes wide (covers the ship's 16-pixel footprint).

### Enemy-ship-vs-SCENERY — `$EB5B` (reverse on hit)

The `$E920` AI's movement loop:
```
EA40  CALL $EB00                  ; step counter
EA43  CALL $EB5B                  ; scroll-tick + scenery probe
EA46  JR Z,$EA4D                  ; if open, exit loop
EA48  CALL $EB47                  ; toggle direction bits 5+6
EA4B  JR $EA40                    ; retry
```

So ships HIT scenery and immediately reverse direction.  No
damage.  Repeats until the ship finds a movement that doesn't
collide.  See [ship-ai.md](ship-ai.md).

### Enemy-bullet-vs-SCENERY — `$EB62` expire

`$ED01` per-frame bullet tick:
```
ED56  LD (X), ($EE76)
ED5C  LD (Y), ($EE77)
ED62  CALL $EB62                  ; probe scenery at bullet position
ED68  JP NZ,$ED7C                 ; hit → expire bullet
```

Bullets that fly into scenery just disappear.  No damage.  See
[enemies.md](enemies.md).

### Player-laser-vs-SCENERY — `$DEDA` self-limit + `$DF31` per-frame

Initial paint (`$DED4..$DEE4`) bails the loop as soon as it
encounters a non-zero bitmap pixel (= scenery).  So the beam
self-limits to its first hit at fire time.

Per-frame (`$DF31`) checks each beam byte for `(HL) != $EF` —
if scenery has overdrawn the beam, expire that segment.  See
[laser.md](laser.md).

### Player-laser-vs-ENEMY-SHIP/BOSS — RESOLVED: `$E9F0` in the TARGET's draw

(An earlier revision concluded "the laser hits nothing" from the
`$DF31` trace alone — wrong; corrected.)  The kill logic lives in
the ships'/boss's own blitter: `$E9AC → $E9F0` checks each screen
byte under the sprite for the beam pattern `$EF` before drawing.
On match the entity dies (alt-B life counter zeroed), the score
gains the remaining counter (≈15 ship / ≈20 boss), a kill jingle
plays 50% of the time (`$F958`), and an 8-particle explosion runs
(`$EDDB`).  Same philosophy as `$DCF5` player damage: the bitmap
IS the collision system.  Full trace in [laser.md](laser.md).

Laser-vs-DECOR remains port-only (System-A entities draw by
overwrite via `$F2BC`, which has no `$EF` check — they erase the
beam instead of dying to it).

## C# port status

| Cassette | Native location | Status |
| -------- | --------------- | ------ |
| `$EB7A`  ship-vs-player | `EnemyShips.TickAi` LastTickHits | done |
| `$EDC0`  bullet-vs-player | `EnemyBullets.Tick` returns hits | done |
| `$EFAE`/`$EFE0` worker pickup | `WorkerSchedule.Tick` rescue logic | done |
| `$DFAF`/`$EB62` player-vs-wall | inline `TickPlaying` tile probe | done |
| `$EB5B` ship-vs-scenery reverse | `EnemyShips.TickAi` X+Y tile probes | done |
| `$EB62` bullet-vs-scenery expire | `EnemyBullets.Tick` levelTiles probe | done |
| `$DEDA` laser self-limit | (skipped — we use entity AABB) | partial |
| Boss-vs-player | `World.TickPlaying` deathHits path (boss feeds coord walker) | done |
| Player-laser-vs-ship | `TickPlaying` bullet loop | port-only (cassette has none) |

## Damage cost summary

| Hit | Effect |
| --- | ------ |
| Player rams ship | `$DD4A` → drains `$E463` by `$40`; on underflow Shield-- |
| Bullet hits player | Same `$DD4A` chain |
| Boss touches player | Same `$DD4A` chain (same routine $EDC0) |
| Player wall hit | Immediate `JP $DBC8` (death, no shield) |
| Laser hits ship | Port-only: +50 score, ship dies with 128-frame respawn (cassette laser hits NOTHING — see above) |
| Laser hits worker | Workers bullet-proof in cassette ($EFE0 only fires on PICKUP, not hit) |
| Laser hits decor entity | Port-only: entity HP--, score per type |
