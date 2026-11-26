# Rune Duel

This is the first playable build of the requested login, class selection, matchmaking, and league systems. The supplied 192-combination battle prototype is intentionally not wired into the app because the brief says the battle screen is out of scope.

## Run locally

1. Install .NET 8 SDK or newer.
2. From this folder run `dotnet run --project src\RuneReign.Api`.
3. Open the URL printed by ASP.NET Core. Swagger is at `/swagger`.

Without a MySQL connection string the API uses a persistent SQLite development database at `src\RuneReign.Api\runereign.db`. Your accounts and matches survive API restarts. For MySQL, set `ConnectionStrings__MySql` to a Pomelo-compatible connection string before starting. The game rules and editable data are in [src\RuneReign.Domain\GameRules.json](src/RuneReign.Domain/GameRules.json).

Test endpoints (`/api/v1/test/results` and `/api/v1/test/wheel`) are disabled when `ASPNETCORE_ENVIRONMENT=Production`.

## Tests

Run `dotnet test RuneReign.sln`.

## Reactive Next.js frontend

The original plain frontend remains in `src\RuneReign.Api\wwwroot`. The reactive Next.js frontend is in `frontend`.

```powershell
cd frontend
npm install
npm run dev
```

Keep the API running on `http://127.0.0.1:5187`, then open `http://localhost:3000`.

## Combat

When matchmaking finds an opponent, choose **Enter combat**. Each turn both players submit a class rune, shared D8 form, and Break/Flow/Bind declaration. The API resolves the triangle, damage, shield, healing, and self-damage, then persists the result. The browser polls the combat status so the opponent's action appears without a refresh.

cd C:\Users\derek\Game\RuneReign

dotnet run --project .\src\RuneReign.Api\RuneReign.Api.csproj -- `  --urls http://127.0.0.1:5187`
--environment Development
