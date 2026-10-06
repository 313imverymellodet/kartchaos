# KART CHAOS

Top-down kart battle for the browser. 2:30 matches for up to 8 drivers. Bots fill every empty seat, so a match never waits.

Live: https://kartchaos.vercel.app

## How it plays: COIN HEIST
- Drive with WASD or the arrow keys, or drag on the left half of a touch screen. Fire with SPACE or the item button.
- **Coins are your score.** They pop up around the arena; drive over them to grab them.
- **Hits spill coins.** Hit a kart and half the coins it's carrying burst out around it for anyone to grab (the hitter pockets one).
- **The golden kart.** Whoever carries the most coins (5+) wears the crown and glows gold. Hitting them spills 75%, so everyone hunts the crown.
- **The ring.** In the last 30 seconds a ring closes in and drags stragglers to the middle.
- Items from **?** boxes: Rocket, Triple, Homing, Mine, Shield, Boost (ramming while boosting counts as a hit).
- Whatever you're carrying at the buzzer goes to your wallet (plus a placing bonus) to unlock 14 cosmetic vehicles.
- Two arenas: **SPEEDWAY** and **PIT STOP**.

## How it's built
- Unity 6000.6.3f1 WebGL. All code is in `unity/Assets/Scripts`:
  - `Game.cs`: match flow, items, hits, camera.
  - `Kart.cs`: driving physics and visuals.
  - `Bot.cs`: bot AI.
  - `Arena.cs`: maps and 2D collision.
  - `Items.cs`: boxes, projectiles, vehicle list.
  - `Coins.cs`: coin heist rules (spill shares, golden kart, ring), coins and the ring visual.
  - `Net.cs`: wire format, plus the offline `LocalRoom`.
  - `UI.cs`: HUD and screens.
- Models: Kenney **Car Kit** and **Racing Kit** (CC0) in `unity/Assets/Resources/Kenney`. Audio is synthesized at boot.
- **Online:**
  - The `/kart` WebSocket lives on the shared server (`orbyt/server/kart.js`).
  - Each client simulates its own kart, and the room host also simulates the bots.
  - The victim's simulator reports hits (with its position); the server spills coins, owns every coin and keeps the scoreboard. Clients claim coins they touch, first claim wins.
  - Hosting moves to another player if the host's tab goes quiet.
- **Offline:** if the socket can't connect, `LocalRoom` runs the exact same match locally.
- `?dev=1` works on localhost only. It exposes `SendMessage("Game", ...)` hooks:
  - `DevEnd`: ends the match.
  - `DevGive` with `"1"` to `"6"`: gives you an item.
  - `DevFire`: uses it.
  - `DevHit`: scores a hit.
  - `DevAuto` with `"1"`: a bot drives your kart (preview videos).
  - The H key hides the UI.

## Build and deploy
```bash
"/c/Program Files/Unity/Hub/Editor/6000.6.3f1/Editor/Unity.exe" -batchmode -nographics -projectPath unity -executeMethod KartBuild.WebGL -quit -logFile build.log
node tools/serve.mjs 8130              # local test
npx vercel deploy --prod --yes         # site
node tools/portals.mjs --gd <id>       # CrazyGames / Poki / GameDistribution zips (see SUBMISSION.md)
```
