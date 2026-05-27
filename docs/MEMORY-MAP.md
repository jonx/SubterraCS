# Memory map — Subterranean Stryker (running snapshot)

This file accumulates every named address we identify while reversing.
Group by region. New entries get added as we go.

## 16 K ROM (Spectrum 48 K system ROM, not in snapshot)

| Address  | Name        | Notes |
| -------- | ----------- | ----- |
| `$1601`  | CHAN-OPEN   | Open a channel; A = channel number. Standard ROM routine. |
| `$1F3D`  | PAUSE-1     | Inner loop of the BASIC `PAUSE` command — snapshot's PC sits here. |
| `RST 10` | PRINT-A     | Print the character in A using the currently open stream. |

## RAM ($4000–$5AFF) — Spectrum video memory

Loaded with the game's title screen at boot (the `LOAD "" CODE` that
puts the bitmap at `$4000` and attributes at `$5800`). At gameplay
time the game's own draw routines write here directly.

## RAM ($5B00–$5BFF) — printer buffer

Spectrum system area. Game may repurpose this.

## RAM ($5C00–$5CB5) — system variables

Standard 48 K Spectrum sysvars. Notables touched by the game:

| Address  | Name      | Notes |
| -------- | --------- | ----- |
| `$5C7B`  | STKBOT    | Bottom of the BASIC calculator stack. Game sets it to `$E62B` (giving itself the run of `$E62B-$FFFF`). |
| `$5C8D`  | ATTR-P    | Permanent attributes. BASIC loader pokes 71 here (bright white on black). |
| `$5CBB`  | FLAGS2    | Game pokes 111 / 244 here to suppress the "press any key" prompt during LOAD. |

## RAM ($5CB6–$5CCA) — channels / data area

## RAM ($5CCB–$5D32) — BASIC loader program

Line 10 of the loader: BORDER NOT PI / POKE / CLEAR / LOAD "" CODE /
RANDOMIZE USR 28350 / POKE / LOAD "" CODE / POKE / PAUSE NOT PI /
RANDOMIZE USR 62973. See [RE-LOG.md §7](RE-LOG.md).

## RAM ($5E88–$E62A) — game data and code, block A

| Address  | Name             | Notes |
| -------- | ---------------- | ----- |
| `$6EBE`  | PreGameEntry     | First `RANDOMIZE USR` from BASIC; runs once before the main game starts. |
| `$E3B2`  | InitHelper       | Called twice early in MainEntry, presumably resets a UI/state structure. |

## RAM ($E62B–$F4FF) — buffers (STKBOT moved here)

The game points STKBOT (`$5C7B`) at `$E62B`, giving the BASIC
calculator stack ~3.5 KB. The game itself likely doesn't use the
calculator stack but moves it out of the way of its own data.

## RAM ($F5FD–$FFFF) — game code, block B

| Address  | Name             | Notes |
| -------- | ---------------- | ----- |
| `$F5FD`  | MainEntry        | Real game entry. Sets up screen, prints title, polls keyboard. |
| `$F82B`  | TitleStringTable | `AT 8,8 INK 0 PAPER 0 "BY  MIKE FOLLIN"` then UDG decorations, `$FF`-terminated. |
| `$FF57`  | Flag57           | First touch in `MainEntry`: `RES 1,(HL)` clears bit 1 of `$FF57`. Purpose TBD. |
