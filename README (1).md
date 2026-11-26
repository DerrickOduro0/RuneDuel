# Rune Duel (working title)

An online 1v1 mage duel game. Players choose a class, climb a league ladder, and are crowned champion at the end of each month. This file describes **what to build right now**. Read all of it before writing code.

## Scope for this build

Build only these four things:

1. **Login screen**
2. **Account creation, followed by class selection**
3. **Matchmaking** (finding an opponent)
4. **League system** (ranks, points, monthly reset)

**Do not build the battle screen / fight console.** After matchmaking finds an opponent, show a simple placeholder screen ("Match found: battle coming soon"). Do not build dice, spells, music, ads, purchases, tournaments, chat, or friend challenges. If something is not on this page, do not add it. Ask first.

## Tech stack

- **Backend: C#** with **ASP.NET Core Web API** (current LTS version of .NET). All game logic and rules live here.
- **Database: MySQL**, accessed through **Entity Framework Core** (Pomelo MySQL provider) with migrations.
- **Front end (temporary): plain HTML, CSS, and JavaScript**, served as static files and calling the API with `fetch`. Do not use Next.js, React, or any framework for now. The front end is a thin display layer, and nothing about the rules may live in it. It will be thrown away when I rebuild the UI in Unity.
- **Unity later:** I will rebuild the front end in Unity and keep this exact backend. Build the backend so a Unity client can use it with zero changes:
  - REST over HTTPS with JSON only. No server-rendered pages.
  - Versioned routes, for example `/api/v1/...`.
  - Token-based login (JWT in the `Authorization` header), not browser cookies.
  - Every feature must be reachable through the API. Nothing may work only through the HTML page.
  - One consistent error format (code and message) for every failure.
  - Generate API documentation (OpenAPI / Swagger).
- **Matchmaking for now:** the client polls a status endpoint every 2 seconds. Real-time (SignalR) comes later with the battle screen, and Unity supports it.
- **The Wheel:** run as a scheduled background job inside the backend, plus a protected test endpoint to trigger it manually. Test and admin endpoints must be off in production.
- **Project layout:** keep the league logic, point rules, and class data in a separate plain C# project (no web or database code inside) so it can be unit tested and later reused for bot-vs-bot balance testing. Add unit tests for league points, promotion, relegation, and the Wheel.

## About the existing HTML

I will provide HTML for the screens. Keep my layout and styling. Wire the screens together and make them work. Do not redesign them. If a screen I need is missing, build a plain, clean placeholder and tell me.

## Screen flow

```
Login  ->  (new player) Create Account -> Choose Class -> Home
Login  ->  (returning player) Home
Home   ->  Matchmaking  ->  Match Found (placeholder)
Home   ->  League
```

### 1. Login screen
- Fields: email (or username) and password. Buttons: Log in, Create account.
- Clear error messages for a wrong password, unknown account, or empty fields.
- A returning player goes straight to Home. A player who made an account but never chose a class goes to class selection.

### 2. Create account, then choose class
- Account fields: username (unique, shown to other players), email, password.
- Right after the account is created, the player must **choose a class** before reaching Home.
- The class screen shows the four classes below as cards: name, one-line playstyle, core rule, and the six runes (name and meaning).
- The player confirms one class. It is saved to their account. (Changing class later is not part of this build.)
- New accounts start in **League 10 (Apprentice)** with zero points and a **hidden skill rating** at a default value.

### 3. Home
A simple hub showing: username, chosen class, current league and points, and two buttons: **Find a match** and **League**.

### 4. Matchmaking
- The player presses Find a match and enters a queue. Show a searching state with a Cancel button.
- Pair players with a similar **hidden skill rating**. The wait should widen the allowed rating gap gradually, so nobody waits forever.
- For a player's first 20 matches, match them only with other new players where possible (protected bracket).
- When two players are paired, both see **Match found** with the opponent's username, class, and league. Then show the placeholder screen. No fight happens yet.
- A player can only be in one queue at a time. Leaving or closing the page removes them from the queue.

### 5. League system
Players climb a ladder of **10 leagues**, like football divisions.

| League | Name |
|---|---|
| 10 | Apprentice (everyone starts here) |
| 9 to 2 | Name them in a single config list I can edit |
| 1 | Archmage (top) |

- Each league has a points bar. Filling it promotes the player. Emptying it relegates them. Nobody falls below League 10.
- Starting values, all editable in one config file: win = +20 points, loss = -10 points, and the first 3 wins of a season are worth double.
- **Match results do not exist yet** because there is no battle. Build the points logic and add an admin or test button that records a win or loss, so I can test promotion, relegation, and the monthly reset.
- The League screen shows: my league and points bar, the leaderboard (top players overall), and the Hall of Fame (past season champions).

#### The Wheel (monthly reset)
At the end of each month, the Wheel turns, automatically:

1. **Crown the champion.** The #1 player is saved permanently in the Hall of Fame with the season name. A player needs at least **30 matches** that season to be eligible.
2. **Drop everyone down.** Players in the top 3 leagues fall **3 leagues**. Everyone else falls 1 or 2. Nobody goes below League 10.
3. **Start the new season.** Season points reset. The hidden skill rating does **not** reset, so matchmaking stays fair.

Provide a way for me to trigger the Wheel manually for testing.

## Classes (four)

Each class has its own D6 of six real Elder Futhark runes and one core rule. All four share the same D8 of forms (not needed yet, but keep the data ready).

| Class | Playstyle | Core rule | Runes (meaning) |
|---|---|---|---|
| **Flamecaller** | Risk and power | Every re-roll costs 1 HP | Kenaz (torch), Sowilo (sun), Nauthiz (need-fire), Thurisaz (giant's thorn), Hagalaz (hail, destruction), Dagaz (dawn, breakthrough) |
| **Frostwarden** | Defense and patience | Unspent shield carries over (max 8) | Isa (ice), Algiz (protection), Uruz (raw strength), Othala (home), Berkano (growth), Laguz (water) |
| **Fateweaver** | Control | Once per turn, move your D8 up or down by 1 | Perthro (fate, cast lots), Ansuz (divine word), Tiwaz (justice), Mannaz (the mind), Gebo (gift, exchange), Fehu (wealth) |
| **Gravebound** | Comeback | Last Stand: below half HP, all spells get +2 | Eihwaz (yew, death and rebirth), Jera (harvest), Ingwaz (seed), Raidho (the road), Ehwaz (the horse, carrying on), Wunjo (joy, last laugh) |

Keep class names, descriptions, and runes in one data file so I can edit them without touching code.

## Rules for how you build it

- **The server is the source of truth.** The browser only displays. Account, class, league points, queue, and season state are stored and decided on the server, never trusted from the browser. This lets me rebuild the front end in Unity later with no backend changes.
- **Keep game data in config files, not code:** class list, league names, point values, matchmaking gap, protected-bracket size, minimum matches for champion.
- Passwords must be stored hashed, never in plain text.
- Work in small steps. After each of the five parts above, stop and tell me what works and how to test it.
- Do not add features, libraries, or screens beyond this page without asking.

## Done means

- I can create an account, choose a class, log out, log back in, and land on Home with my class and league shown.
- Two accounts can queue at once and both see Match found.
- I can record test wins and losses and watch a player get promoted and relegated.
- I can trigger the Wheel and see: a champion saved to the Hall of Fame, top-league players dropped 3 leagues, and hidden ratings unchanged.

## Not now (for later)

Battle screen, dice and spells, the Flow / Bind / Break state system, sound and music, character art, ads, purchases, tournaments, friend challenge links, class changes.
