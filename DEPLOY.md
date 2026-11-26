# Hosting Rune Duel for free

| Part | Where | Notes |
|---|---|---|
| Database | Supabase (PostgreSQL) | Free plan pauses after a week with no activity; data is kept. |
| Game server (API) | Render, free web service | Sleeps after 15 minutes with no players; the first visit waits about a minute. |
| Game pages (front end) | Vercel, Hobby plan | Always on. Sends `/api/v1/...` calls on to Render. |

Locally nothing changes: with no PostgreSQL connection set, the API uses SQLite (`runereign.db`) as before.

## 1. Push the code to GitHub

From the `RuneReign` folder:

```
git add -A
git commit -m "Host on Render + Supabase"
git push
```

## 2. Game server on Render

1. In Render: **New > Blueprint**, connect GitHub and pick this repository. Render reads `render.yaml`.
2. It asks for one value, **ConnectionStrings__Postgres**. Use this form, with your real database password:

   ```
   Host=<supabase pooler host>;Port=5432;Database=postgres;Username=<supabase user>;Password=<your password>
   ```

   The host and user are on Supabase under **Connect > Session pooler**. The `postgresql://...` link also works, but only if the password has no `@ : / # ?` characters, so the form above is safer.
3. Click **Apply**. The first build takes several minutes.
4. When it shows **Live**, open `https://<your-service>.onrender.com/api/v1/health`. It should show `{"status":"ok"}`.
   On first start the server creates all the tables in Supabase and fills in the game content by itself.
5. Copy the service address (for example `https://rune-duel-api.onrender.com`).

Render also creates **Jwt__Key**, the secret that signs logins. Never change it, or everyone is logged out. If you move hosts later, copy it across.

## 3. Game pages on Vercel

1. **Add New > Project**, import the repository.
2. **Root Directory:** `frontend`. Preset: Next.js. Leave the build settings as they are.
3. **Environment variable:** `API_URL` = the Render address, with no `/` at the end.
4. **Deploy.** If you change `API_URL` later, redeploy, because it is read during the build.

## 4. Make yourself admin

`render.yaml` lists the admin usernames in **Admin__Users** (`WhiteMisty,Wizzpa,Diero`). The hosted database starts empty, so register one of those names on the live site **right after the first deploy**: it becomes admin straight away. Then open `/admin`.
To change the list, edit **Admin__Users** in Render (Environment tab). You can also make or remove admins from the admin page's Players tab.

## Everyday use

- **Updates:** push to GitHub. Render and Vercel both rebuild by themselves.
- **Database changes:** after changing the models, add a migration (`dotnet ef migrations add <Name>` in `src/RuneReign.Api`). The server applies new migrations when it starts.
- **Item pictures** uploaded on the admin page are stored in the database (1 MB each), so redeploys never lose them.

## Moving to a paid server later (no data lost)

1. Put the game in maintenance for a few minutes.
2. Copy the database: `pg_dump` from Supabase, then `pg_restore` (or `psql`) into the new PostgreSQL.
3. Start the API there with the same **Jwt__Key** and the new connection.
4. Point `API_URL` (or your domain) at the new server.

Everything, including item pictures, lives in the database, so it all comes across.
