# KART CHAOS: portal submission kit

## 1. Build the packages
```bash
node tools/portals.mjs
```
This writes `portals/kartchaos-crazygames.zip`, `portals/kartchaos-poki.zip` and `portals/kartchaos-gd.zip`. Each has `index.html` at the root and is about 5.5 MB. Upload the zip as an **HTML5** game, not as a Unity game.

What the portal builds include:
- **Ads:**
  - A midgame ad runs before each match (Poki every match; CrazyGames and GD skip the first, with at least 60 s between ads).
  - Rewarded ads give 2x coins after a match and +75 coins in the garage.
  - The game freezes and mutes during every ad.
- **Portal requirements:**
  - Gameplay start/stop events are sent.
  - No outbound links: the invite link is hidden on portals, and friends join with the room code instead.
  - Space and arrow keys can't scroll the page.
  - Analytics report per portal: `kartchaos_cg`, `kartchaos_poki`, `kartchaos_gd`.

## 2. Art (in `portal-art/`)
| File | Use |
|---|---|
| `cg-landscape-1920x1080.png`, `cg-portrait-800x1200.png`, `cg-square-800x800.png` | CrazyGames covers |
| `gd-512x512.png`, `gd-512x384.png`, `gd-200x120.png` | GameDistribution images |

Preview videos for CrazyGames are not made yet. Ask and I'll record them the same way as SNACK MERGE's.

## 3. Form answers (CrazyGames)
| Field | Answer |
|---|---|
| Game name | `KART CHAOS` |
| Game engine | **HTML5** |
| Progress save | **No, the game does not need progress save** (coins and unlocks are kept in browser storage) |
| Supports mobile | ✅ |
| Online multiplayer | ✅ |
| Muting audio through SDK | ☐ |

## 4. Store listing copy
**Title:** KART CHAOS

**Short description:** Grab item boxes, fire rockets, drop mines and spin out your rivals in fast 2:30 kart battles!

**Description:**
KART CHAOS is a fast top-down kart battle. Jump into an arena with up to 8 drivers, smash through **?** boxes and use what you get:
- Rockets.
- Triple shots.
- Homing missiles.
- Mines.
- Shields.
- Speed boosts.

Every hit you land is a point. Most points when the 2:30 timer runs out wins!

- Online matches start instantly, and bots fill every empty seat.
- Play with friends: make a private room and share the code.
- Two arenas: SPEEDWAY and PIT STOP.
- Earn coins and unlock 14 vehicles, from cute karts to a fire truck.

**Controls:**
- Keyboard: WASD or arrow keys to drive, SPACE to fire, ESC for the menu.
- Touch: drag on the left side to drive, tap the item button to fire.

**Tags / categories:** Racing, Multiplayer, Car, Action, Shooting, Battle, .io, Casual

**Category:** Racing (or Action)

**Age:** everyone. Cartoon kart bumping, no blood, no chat. Player names are filtered.

## 5. Funnel events to watch
- **Starts:** `boot`, then `play_click`, then `match_online` or `match_offline` (value = humans in the match).
- **Tutorial:** `tut_drove`, then `tut_item`, then `tut_done`.
- **During play:** `item_get`, `fire_*`, `first_hit`, `got_hit`.
- **End of match:** `end_online` or `end_offline` (value = place), `end_hits`.
- **Retention and quitting:** `play_again`, `quit_midmatch`.
- **Coins:** `coins_double`, `coins_ad`, `unlock_*`.
- **Friends:** `play_private`, `join_code`.
