# Annotated Z80 disassembly — Subterranean Stryker

Per-subsystem disassembly with inline annotations.  Each file
documents one logical group of routines, with every decoded
address explained.  Cross-references between files use the
`addr@file` form (e.g. `$DAF2@scenery.md`).

The narrative — *why* we discovered something and what the
investigation looked like — stays in [`../RE-LOG.md`](../RE-LOG.md).
This folder is the *reference*: when re-entering a session, look
here first to avoid re-disassembling routines we've already
decoded.

The companion address index is [`../MEMORY-MAP.md`](../MEMORY-MAP.md)
which lists each address's purpose; this folder shows each
routine's *code*.

## Subsystems

| File | What's in it | Status |
| ---- | ------------ | ------ |
| [level-load.md](level-load.md) | The level-load chain from `$F6F2` through the nine helpers it calls (`$E319`, `$E2C6`, `$E2E5`, `$E347`, `$F1BC`) | partial |
| [level-paint.md](level-paint.md) | `$DB1A` outer paint loop + `$DB7A` scroll-up + `$DAF2` tile blit + `$E104` mini-map walker | **done** |
| [hud.md](hud.md) | `$E046` per-frame HUD updater + `$E0BE` bar driver + `$E0F1` cell paint + `$E785` print-stream table | **done** |
| [entities.md](entities.md) | `$F1A5` dispatcher + `$F1EF` per-entity draw + `$F2BC` 16×16 blit + `$F1BC` per-level loader | partial |
| [death.md](death.md) | `$DDC4` hit sound + shield-- + `$DBC8` death animation + `$DBDA` 8-particle paint + `$DC43` screen-dim + `$D8A8` lives check | **done** |
| [scroll-horizontal.md](scroll-horizontal.md) | `$D9C8` L-key handler + `$DA23` scroll-left + `$DA62` scroll-right + `$DAA9` fresh-column paint | **done** |
| [laser.md](laser.md) | `$DE41` fire-key handler + `$DEF0..$DF1B` per-frame beam tail-recede | **done** |
| [collision.md](collision.md) | `$DD4A` collision walker + `$DD8C` / `$DDAA` per-entity 8×8 box check | partial |
| [damages.md](damages.md) | `$DCF5` XOR-overlap trigger + `$EB7A` / `$EDC0` address-match + `$DDC4` chain — how the player takes damage | **done** |
| [enemies.md](enemies.md) | Top-level entity-subsystem map: ships / bullets / boss / workers | **done** |
| [ship-ai.md](ship-ai.md) | `$E920` ship AI + all helpers (`$EADE`, `$EB00`, `$EB5B`, `$EB7A`, `$EAB2/$EABD`, `$DFAF`, `$DCAC`) | **done** |
| [boss.md](boss.md) | `$EC10` spawn + `$EC4C` tick + `$EE7D` state | **done** |
| [workers.md](workers.md) | `$E75D` schedule + `$EF02` chain + pickup zone + `$EFE0` scoring | **done** |
| [input.md](input.md) | `$D8F0` dispatcher + 4 control schemes (`$D8F4` keyboard, `$F14E` Kempston, `$F177` Interface 2, `$F0F9` cursor, `$FB71` Sinclair) + `$E45F` bit layout | **done** |
| [player.md](player.md) | Player physics + `$E3F4` sprite stage + `$DCAC` bank shift + `$DCF5` XOR draw + `$F868` page-advance gate | **done** |
| [title-menu.md](title-menu.md) | `$F5FC` title loop + control-scheme selection + game-init | **done** |
| [main-loop.md](main-loop.md) | `$D7FB..$D826` per-frame dispatch map + `$E8FD` sub-chain + state inventory | **done** |
| [sound.md](sound.md) | `$FA0A` print-stream + `$5E88` Follin player + SFX entries (`$F8F9`, `$F90E`, `$F93A`) | partial |

## Conventions

* **Address format**: `$XXXX` for code, `($XXXX)` for indirected
  memory contents.
* **Listings**: 3-column format `addr | bytes | mnemonic` with a
  trailing `; comment` for each instruction whose purpose is
  non-obvious.
* **Cross-references**: when a routine calls another, link to its
  entry: `CALL [$DAF2](level-paint.md#daf2-tile-blit)`.
* **Side effects**: each routine entry lists which RAM addresses
  it reads / writes, so we can see data flow at a glance.
* **Verification**: when an annotation comes from empirical
  observation (mem-write-trace, pixel diff, etc.) rather than
  from the disasm alone, mark it `(verified by ...)`.
