"use client";

import { useEffect, useMemo, useState } from "react";
import "./admin.css";

// The admin page. Only accounts marked as admin on the server can load or change anything here;
// the server checks every request, so this page cannot be used by anyone else.

async function call(path, options = {}) {
  const token = typeof window !== "undefined" ? window.localStorage.getItem("runeToken") : null;
  const isForm = options.body instanceof FormData;
  const response = await fetch(`/api/v1${path}`, {
    ...options,
    headers: {
      ...(isForm ? {} : { "Content-Type": "application/json" }),
      ...(token ? { Authorization: `Bearer ${token}` } : {})
    }
  });
  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(data.error?.message || (response.status >= 502 ? "The game server is waking up. Try again in a moment." : `Request failed (${response.status}).`));
    error.status = response.status;
    throw error;
  }
  return data;
}

const TABS = [
  ["overview", "Overview"], ["players", "Players"], ["matches", "Matches"], ["items", "Items"], ["talismans", "Talismans"],
  ["attributes", "Attributes"], ["settings", "Game settings"], ["announce", "Announce"]
];
const STATS = ["power", "guard", "vigor", "focus", "grit", "edge", "pierce", "insight", "resolve"];
const RARITIES = ["common", "rare", "epic"];

function ago(value) {
  if (!value) return "never";
  const seconds = (Date.now() - new Date(value.endsWith?.("Z") ? value : value + "Z").getTime()) / 1000;
  if (seconds < 60) return "just now";
  if (seconds < 3600) return `${Math.floor(seconds / 60)} min ago`;
  if (seconds < 86400) return `${Math.floor(seconds / 3600)} h ago`;
  if (seconds < 86400 * 30) return `${Math.floor(seconds / 86400)} d ago`;
  return new Date(value).toLocaleDateString();
}
function date(value) {
  if (!value) return "—";
  return new Date(value.endsWith?.("Z") ? value : value + "Z").toLocaleString([], { dateStyle: "medium", timeStyle: "short" });
}
const pct = (v) => (v === null || v === undefined ? "—" : `${v}%`);

export default function AdminPage() {
  const [state, setState] = useState("loading"); // loading | denied | ready
  const [me, setMe] = useState(null);
  const [tab, setTab] = useState("overview");
  const [toast, setToast] = useState(null);
  const [openPlayer, setOpenPlayer] = useState(null);
  const [openMatch, setOpenMatch] = useState(null);

  useEffect(() => {
    call("/me").then((profile) => {
      setMe(profile);
      setState(profile.isAdmin ? "ready" : "denied");
    }).catch(() => setState("denied"));
  }, []);

  function notify(text, bad = false) {
    const id = Date.now();
    setToast({ text, bad, id });
    setTimeout(() => setToast((t) => (t && t.id === id ? null : t)), 3200);
  }

  if (state === "loading") return <main className="adm adm-center"><p>Checking your access…</p></main>;
  if (state === "denied") return <main className="adm adm-center">
    <div className="adm-denied"><h1>Admins only</h1><p>Sign in to the game with an admin account first, then open this page again.</p><a href="/">Back to the game</a></div>
  </main>;

  const shared = { notify, onPlayer: setOpenPlayer, onMatch: setOpenMatch };
  return <main className="adm">
    <header className="adm-top">
      <div><span className="adm-eyebrow">RUNE DUEL</span><h1>Admin</h1></div>
      <span className="adm-who">Signed in as <b>{me.username}</b> · <a href="/">Back to the game</a></span>
    </header>
    <nav className="adm-tabs">{TABS.map(([key, label]) => <button key={key} className={tab === key ? "on" : ""} onClick={() => setTab(key)}>{label}</button>)}</nav>
    <section className="adm-body">
      {tab === "overview" && <Overview {...shared} />}
      {tab === "players" && <Players {...shared} />}
      {tab === "matches" && <Matches {...shared} />}
      {tab === "items" && <Items {...shared} />}
      {tab === "talismans" && <Talismans {...shared} />}
      {tab === "attributes" && <Attributes {...shared} />}
      {tab === "settings" && <Settings {...shared} />}
      {tab === "announce" && <Announce {...shared} />}
    </section>
    {openPlayer && <PlayerDetail id={openPlayer} me={me} {...shared} onClose={() => setOpenPlayer(null)} />}
    {openMatch && <MatchDetail id={openMatch} {...shared} onClose={() => setOpenMatch(null)} />}
    {toast && <div className={`adm-toast ${toast.bad ? "bad" : ""}`} role="status">{toast.text}</div>}
  </main>;
}

function useLoad(path) {
  const [data, setData] = useState(null);
  const [error, setError] = useState("");
  const load = () => call(path).then((d) => { setData(d); setError(""); }).catch((e) => setError(e.message));
  useEffect(() => { load(); }, [path]);
  return [data, setData, error, load];
}

/* ---------- Overview ---------- */
function Overview({ onMatch }) {
  const [data, , error, load] = useLoad("/admin/overview");
  const [stats, , statsError, loadStats] = useLoad("/admin/stats");
  if (error || statsError) return <p className="adm-error">{error || statsError}</p>;
  if (!data || !stats) return <p className="adm-muted">Loading…</p>;
  const cards = [
    ["Players", data.players], ["Active today", stats.activeToday], ["Active this week", stats.activeWeek], ["New today", data.newToday],
    ["PvP duels today", data.duelsToday], ["Practice today", data.practiceToday], ["Duels in progress", data.activeDuels], ["Searching now", data.searching],
    ["Coins held", data.coins.toLocaleString()], ["Gems held", data.gems.toLocaleString()], ["Admins", data.admins], ["Banned", data.banned]
  ];
  const peak = Math.max(1, ...stats.days.map((d) => Math.max(d.signups, d.duels)));
  return <>
    <div className="adm-head"><h2>Overview</h2><button className="ghost" onClick={() => { load(); loadStats(); }}>Refresh</button></div>
    <div className="adm-stats">{cards.map(([label, value]) => <div key={label}><b>{value}</b><span>{label}</span></div>)}</div>

    <div className="adm-two">
      <div className="adm-panel">
        <h3>Last 14 days</h3>
        <div className="adm-legend"><span className="k-sign">New players</span><span className="k-duel">PvP duels</span></div>
        <div className="adm-days">{stats.days.map((d) => <div key={d.day} title={`${d.day}: ${d.signups} new, ${d.duels} duels`}>
          <i className="k-sign" style={{ height: `${(d.signups / peak) * 100}%` }} />
          <i className="k-duel" style={{ height: `${(d.duels / peak) * 100}%` }} />
          <small>{d.day.slice(8)}</small>
        </div>)}</div>
      </div>
      <div className="adm-panel">
        <h3>Class balance (finished PvP duels)</h3>
        {stats.classes.length === 0 ? <p className="adm-muted">No finished PvP duels yet.</p> :
          <table className="adm-table flat"><thead><tr><th>Class</th><th>Duels</th><th>Wins</th><th>Draws</th><th>Win rate</th></tr></thead>
            <tbody>{stats.classes.map((c) => <tr key={c.className}><td><b>{c.className}</b></td><td>{c.stats.duels}</td><td>{c.stats.wins}</td><td>{c.stats.draws}</td>
              <td><Bar value={c.stats.winRate} /></td></tr>)}</tbody></table>}
        <p className="adm-hint">Around 50% means balanced. A draw counts as half a win. Mirror matches (same class on both sides) count once for each side.</p>
      </div>
    </div>

    <div className="adm-panel">
      <h3>Latest duels</h3>
      <MatchTable rows={stats.recent} onMatch={onMatch} />
    </div>
  </>;
}

function Bar({ value }) {
  if (value === null || value === undefined) return <span className="adm-muted">—</span>;
  const tone = value > 58 ? "hot" : value < 42 ? "cold" : "ok";
  return <span className={`adm-bar ${tone}`}><i style={{ width: `${value}%` }} /><b>{value}%</b></span>;
}

/* ---------- Players ---------- */
function Players({ notify, onPlayer }) {
  const [query, setQuery] = useState("");
  const [rows, setRows] = useState(null);
  const [error, setError] = useState("");
  async function search(q = query) {
    try { setRows(await call(`/admin/players?q=${encodeURIComponent(q)}`)); setError(""); } catch (e) { setError(e.message); }
  }
  useEffect(() => { search(""); }, []);
  return <>
    <div className="adm-head"><h2>Players</h2>
      <form className="adm-inline" onSubmit={(e) => { e.preventDefault(); search(); }}><input className="adm-search" placeholder="Name or email" value={query} onChange={(e) => setQuery(e.target.value)} /><button>Search</button></form>
      <p className="adm-muted">Most recently active first. Click a player to see everything about them and make changes.</p>
    </div>
    {error && <p className="adm-error">{error}</p>}
    {!rows ? <p className="adm-muted">Loading…</p> : <table className="adm-table click"><thead><tr><th>Player</th><th>Level</th><th>Class</th><th>League</th><th>Rating</th><th>Season</th><th>Coins</th><th>Gems</th><th>Joined</th><th>Last seen</th></tr></thead>
      <tbody>{rows.map((p) => <tr key={p.id} className={p.banned ? "banned" : ""} onClick={() => onPlayer(p.id)}>
        <td><b>{p.username}</b>{p.isAdmin && <span className="adm-tag">admin</span>}{p.banned && <span className="adm-tag bad">banned</span>}<small className="adm-block">{p.email}</small></td>
        <td>{p.level}</td>
        <td>{p.className || "—"}</td>
        <td>{p.league} · {p.points} pts</td>
        <td>{p.skillRating}</td>
        <td>{p.seasonWins}W / {p.seasonMatches}</td>
        <td>{p.coins.toLocaleString()}</td>
        <td>{p.gems.toLocaleString()}</td>
        <td>{ago(p.createdAtUtc)}</td>
        <td>{ago(p.lastSeenAtUtc)}</td>
      </tr>)}</tbody></table>}
    {rows && rows.length === 50 && <p className="adm-muted">Showing 50. Search to narrow it down.</p>}
  </>;
}

function PlayerDetail({ id, me, notify, onMatch, onClose }) {
  const [data, setData] = useState(null);
  const [error, setError] = useState("");
  const [wallet, setWallet] = useState(null);
  const [password, setPassword] = useState("");
  const [letter, setLetter] = useState({ subject: "", body: "" });
  const [catalog, setCatalog] = useState({ items: [], talismans: [] });
  const [giveItem, setGiveItem] = useState("");
  const [giveTalisman, setGiveTalisman] = useState("");
  const [busy, setBusy] = useState(false);

  async function load() {
    try {
      const d = await call(`/admin/players/${id}`);
      setData(d);
      setWallet({ coins: d.player.coins, gems: d.player.gems, banned: d.player.banned, isAdmin: d.player.isAdmin });
    } catch (e) { setError(e.message); }
  }
  useEffect(() => {
    load();
    Promise.all([call("/admin/items"), call("/admin/talismans")]).then(([i, t]) => setCatalog({ items: i.items, talismans: t })).catch(() => {});
  }, [id]);

  async function act(fn, done) {
    setBusy(true);
    try { await fn(); notify(done); await load(); } catch (e) { notify(e.message, true); }
    setBusy(false);
  }
  const self = data && data.player.username === me.username;
  const saveWallet = () => {
    if (wallet.banned && !data.player.banned && !window.confirm(`Ban ${data.player.username}? They will be signed out of the game.`)) return;
    act(() => call(`/admin/players/${id}`, { method: "PUT", body: JSON.stringify({ coins: Number(wallet.coins) || 0, gems: Number(wallet.gems) || 0, banned: wallet.banned, isAdmin: wallet.isAdmin }) }), "Player saved.");
  };

  return <div className="adm-drawer" role="dialog" aria-modal="true" onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}>
    <div className="adm-drawer-body">
      <button className="ghost adm-close" onClick={onClose}>Close</button>
      {error && <p className="adm-error">{error}</p>}
      {!data ? <p className="adm-muted">Loading…</p> : <>
        <div className="adm-profile">
          <span className="adm-avatar">{data.player.avatarUrl ? <img src={data.player.avatarUrl} alt="" /> : data.player.username[0].toUpperCase()}</span>
          <div>
            <h2>{data.player.username}{data.player.isAdmin && <span className="adm-tag">admin</span>}{data.player.banned && <span className="adm-tag bad">banned</span>}</h2>
            <p className="adm-muted">{data.player.email}</p>
            {data.player.motto && <p className="adm-motto">“{data.player.motto}”</p>}
            <div className="adm-actions left tight">
              {data.player.avatarUrl && <button className="ghost" disabled={busy} onClick={() => window.confirm("Remove this player's picture?") && act(() => call(`/admin/players/${id}/avatar`, { method: "DELETE" }), "Picture removed.")}>Remove picture</button>}
              {data.player.motto && <button className="ghost" disabled={busy} onClick={() => window.confirm("Clear this player's motto?") && act(() => call(`/admin/players/${id}/motto`, { method: "DELETE" }), "Motto cleared.")}>Clear motto</button>}
            </div>
          </div>
        </div>
        <div className="adm-facts">
          <div><span>Level</span><b>{data.player.level} · {data.player.xp.toLocaleString()} XP</b></div>
          <div><span>Class</span><b>{data.player.className || "Not chosen"}</b></div>
          <div><span>League</span><b>{data.player.league} · {data.player.points} pts</b></div>
          <div><span>Rating</span><b>{data.player.skillRating}</b></div>
          <div><span>This season</span><b>{data.player.seasonWins}W of {data.player.seasonMatches}</b></div>
          <div><span>All PvP</span><b>{data.record.pvp.wins}W · {data.record.pvp.losses}L · {data.record.pvp.draws}D</b></div>
          <div><span>PvP win rate</span><b>{pct(data.record.pvp.winRate)}</b></div>
          <div><span>Practice duels</span><b>{data.record.practice}</b></div>
          <div><span>Win streak</span><b>{data.player.winStreak} (best {data.player.bestWinStreak})</b></div>
          <div><span>Unfinished duels</span><b>{data.record.unfinished}</b></div>
          <div><span>Tutorial</span><b>{data.player.tutorialDone ? "Done" : "Not done"}</b></div>
          <div><span>Story chapter</span><b>{data.player.storyChapter}</b></div>
          <div><span>Joined</span><b>{date(data.player.createdAtUtc)}</b></div>
          <div><span>Last seen</span><b>{ago(data.player.lastSeenAtUtc)}</b></div>
          <div><span>Unread letters</span><b>{data.unreadLetters}</b></div>
        </div>

        <section className="adm-sec">
          <h3>Wallet and access</h3>
          <div className="adm-grid">
            <Field label="Coins"><input type="number" min="0" value={wallet.coins} onChange={(e) => setWallet({ ...wallet, coins: e.target.value })} /></Field>
            <Field label="Gems"><input type="number" min="0" value={wallet.gems} onChange={(e) => setWallet({ ...wallet, gems: e.target.value })} /></Field>
            <Field label="Banned"><Toggle value={wallet.banned} disabled={self} onChange={(v) => setWallet({ ...wallet, banned: v })} /></Field>
            <Field label="Admin"><Toggle value={wallet.isAdmin} disabled={self} onChange={(v) => setWallet({ ...wallet, isAdmin: v })} /></Field>
          </div>
          <div className="adm-actions"><button disabled={busy} onClick={saveWallet}>Save</button></div>
        </section>

        <section className="adm-sec">
          <h3>Set a new password</h3>
          <p className="adm-hint">For players who forgot theirs. Tell them the new one privately; they can keep using it.</p>
          <form className="adm-inline" onSubmit={(e) => { e.preventDefault(); act(() => call(`/admin/players/${id}/password`, { method: "POST", body: JSON.stringify({ password }) }), "Password changed.").then(() => setPassword("")); }}>
            <input type="text" autoComplete="off" placeholder="At least 8 characters" value={password} onChange={(e) => setPassword(e.target.value)} />
            <button disabled={busy || password.length < 8}>Set password</button>
          </form>
        </section>

        <section className="adm-sec">
          <h3>Items ({data.items.length})</h3>
          <ul className="adm-chips">{data.items.map((i) => <li key={i.key}>{i.name}{i.equipped && <em>worn</em>}
            <button className="ghost" disabled={busy} title="Take away" onClick={() => window.confirm(`Take ${i.name} from ${data.player.username}?`) && act(() => call(`/admin/players/${id}/items/${i.key}`, { method: "DELETE" }), `${i.name} removed.`)}>×</button></li>)}</ul>
          <div className="adm-inline">
            <select value={giveItem} onChange={(e) => setGiveItem(e.target.value)}><option value="">Give an item…</option>
              {catalog.items.filter((i) => !data.items.some((o) => o.key === i.key)).map((i) => <option key={i.key} value={i.key}>{i.name} ({i.kind})</option>)}</select>
            <button disabled={busy || !giveItem} onClick={() => act(() => call(`/admin/players/${id}/items`, { method: "POST", body: JSON.stringify({ key: giveItem }) }), "Item given.").then(() => setGiveItem(""))}>Give</button>
          </div>
        </section>

        <section className="adm-sec">
          <h3>Talismans ({data.talismans.length})</h3>
          <ul className="adm-chips">{data.talismans.map((t) => <li key={t.key}>{t.name}{t.equipped && <em>equipped</em>}
            <button className="ghost" disabled={busy} title="Take away" onClick={() => window.confirm(`Take ${t.name} from ${data.player.username}?`) && act(() => call(`/admin/players/${id}/talismans/${t.key}`, { method: "DELETE" }), `${t.name} removed.`)}>×</button></li>)}</ul>
          <div className="adm-inline">
            <select value={giveTalisman} onChange={(e) => setGiveTalisman(e.target.value)}><option value="">Give a talisman…</option>
              {catalog.talismans.filter((t) => !data.talismans.some((o) => o.key === t.key)).map((t) => <option key={t.key} value={t.key}>{t.name}</option>)}</select>
            <button disabled={busy || !giveTalisman} onClick={() => act(() => call(`/admin/players/${id}/talismans`, { method: "POST", body: JSON.stringify({ key: giveTalisman }) }), "Talisman given.").then(() => setGiveTalisman(""))}>Give</button>
          </div>
        </section>

        <section className="adm-sec">
          <h3>Send a letter</h3>
          <div className="adm-grid">
            <Field label="Subject" wide><input value={letter.subject} maxLength={120} onChange={(e) => setLetter({ ...letter, subject: e.target.value })} /></Field>
            <Field label="Message" wide><textarea rows={3} value={letter.body} onChange={(e) => setLetter({ ...letter, body: e.target.value })} /></Field>
          </div>
          <div className="adm-actions"><button disabled={busy || !letter.subject.trim() || !letter.body.trim()} onClick={() => act(() => call(`/admin/players/${id}/letter`, { method: "POST", body: JSON.stringify(letter) }), "Letter sent.").then(() => setLetter({ subject: "", body: "" }))}>Send to {data.player.username}</button></div>
        </section>

        <section className="adm-sec">
          <h3>Recent duels</h3>
          <MatchTable rows={data.matches} onMatch={onMatch} />
        </section>
      </>}
    </div>
  </div>;
}

/* ---------- Matches ---------- */
function Matches({ onMatch }) {
  const [status, setStatus] = useState("all");
  const [mode, setMode] = useState("all");
  const [q, setQ] = useState("");
  const [applied, setApplied] = useState("");
  const [rows, , error, load] = useLoad(`/admin/matches?status=${status}&mode=${mode}&q=${encodeURIComponent(applied)}`);
  return <>
    <div className="adm-head"><h2>Matches</h2>
      <form className="adm-inline" onSubmit={(e) => { e.preventDefault(); setApplied(q); }}>
        <select value={status} onChange={(e) => setStatus(e.target.value)}><option value="all">All</option><option value="active">In progress</option><option value="finished">Finished</option></select>
        <select value={mode} onChange={(e) => setMode(e.target.value)}><option value="all">Every mode</option><option value="ranked">Ranked</option><option value="casual">Casual</option><option value="practice">Practice</option></select>
        <input className="adm-search" placeholder="Player name" value={q} onChange={(e) => setQ(e.target.value)} />
        <button>Search</button><button type="button" className="ghost" onClick={load}>Refresh</button>
      </form>
      <p className="adm-muted">The 60 newest that match. Click a duel to see every round, or to close one that is stuck.</p>
    </div>
    {error && <p className="adm-error">{error}</p>}
    {!rows ? <p className="adm-muted">Loading…</p> : <MatchTable rows={rows} onMatch={onMatch} />}
  </>;
}

function MatchTable({ rows, onMatch }) {
  if (!rows?.length) return <p className="adm-muted">No duels yet.</p>;
  return <table className="adm-table click"><thead><tr><th>Started</th><th>Mode</th><th>Player A</th><th>Player B</th><th>Result</th><th>Turns</th></tr></thead>
    <tbody>{rows.map((m) => <tr key={m.id} onClick={() => onMatch(m.id)}>
      <td>{ago(m.createdAtUtc)}</td>
      <td><span className={`adm-tag ${m.mode.startsWith("practice") ? "dim" : m.mode === "ranked" ? "" : "alt"}`}>{m.mode}</span></td>
      <td><b>{m.playerA.name}</b><small className="adm-block">{m.playerA.className}{m.playerA.talisman ? ` · ${m.playerA.talisman}` : ""}</small></td>
      <td><b>{m.playerB.name}</b><small className="adm-block">{m.playerB.className}{m.playerB.talisman ? ` · ${m.playerB.talisman}` : ""}</small></td>
      <td>{m.status === "in progress" ? <span className="adm-tag alt">in progress</span> : m.status === "draw" ? "Draw" : <>Winner: <b>{m.winner}</b></>}</td>
      <td>{m.turns}</td>
    </tr>)}</tbody></table>;
}

function MatchDetail({ id, notify, onPlayer, onClose }) {
  const [data, setData] = useState(null);
  const [error, setError] = useState("");
  const load = () => call(`/admin/matches/${id}`).then(setData).catch((e) => setError(e.message));
  useEffect(() => { load(); }, [id]);
  async function close() {
    if (!window.confirm("Close this duel? Nobody wins and no rewards are given.")) return;
    try { await call(`/admin/matches/${id}/close`, { method: "POST" }); notify("Duel closed."); load(); } catch (e) { notify(e.message, true); }
  }
  const m = data?.match;
  const side = (p) => <div className="adm-side">
    <b>{p.name}</b><span>{p.className}</span><span>{p.talisman ? `Talisman: ${p.talisman}` : "No talisman"}</span><span>HP now: {p.hp}</span>
  </div>;
  return <div className="adm-drawer" role="dialog" aria-modal="true" onClick={(e) => { if (e.target === e.currentTarget) onClose(); }}>
    <div className="adm-drawer-body">
      <button className="ghost adm-close" onClick={onClose}>Close</button>
      {error && <p className="adm-error">{error}</p>}
      {!data ? <p className="adm-muted">Loading…</p> : <>
        <h2>{m.playerA.name} vs {m.playerB.name}</h2>
        <p className="adm-muted">{m.mode} · started {date(m.createdAtUtc)}{m.completedAtUtc ? ` · ended ${date(m.completedAtUtc)}` : " · still in progress"}</p>
        <div className="adm-versus">{side(m.playerA)}<span className="adm-vs">VS</span>{side(m.playerB)}</div>
        <div className="adm-facts">
          <div><span>Result</span><b>{m.status === "in progress" ? "In progress" : m.status === "draw" ? "Draw" : `${m.winner} won`}</b></div>
          <div><span>Turns</span><b>{m.turns}</b></div>
          <div><span>Points A / B</span><b>{data.rewards.pointsDeltaA} / {data.rewards.pointsDeltaB}</b></div>
          <div><span>Rating A / B</span><b>{data.rewards.ratingDeltaA} / {data.rewards.ratingDeltaB}</b></div>
          <div><span>Coins A / B</span><b>{data.rewards.coinsA} / {data.rewards.coinsB}</b></div>
          <div><span>Talisman uses A / B</span><b>{data.talismanUses.a} / {data.talismanUses.b}</b></div>
        </div>
        <p className="adm-log">{data.now.lastLog}</p>
        {m.status === "in progress" && <div className="adm-actions left"><button className="danger" onClick={close}>Close this duel (no result)</button></div>}
        <h3 className="adm-sub">Rounds</h3>
        {data.rounds.length === 0 ? <p className="adm-muted">No rounds played yet.</p> :
          <table className="adm-table"><thead><tr><th>#</th><th>{m.playerA.name}</th><th>{m.playerB.name}</th><th>HP after</th><th>What happened</th></tr></thead>
            <tbody>{data.rounds.map((r) => <tr key={r.turn}><td>{r.turn}</td><td>{r.stateA}<small className="adm-block">{r.runeA} · {r.formA}</small></td>
              <td>{r.stateB}<small className="adm-block">{r.runeB} · {r.formB}</small></td><td>{r.hpA} / {r.hpB}</td><td className="adm-muted">{r.log}</td></tr>)}</tbody></table>}
        <div className="adm-actions left">
          {m.playerA.name !== "The Shade" && <button className="ghost" onClick={() => { onClose(); onPlayer(m.playerA.id); }}>Open {m.playerA.name}</button>}
          {m.playerB.name !== "The Shade" && <button className="ghost" onClick={() => { onClose(); onPlayer(m.playerB.id); }}>Open {m.playerB.name}</button>}
        </div>
      </>}
    </div>
  </div>;
}

/* ---------- Items ---------- */
function Items({ notify }) {
  const [data, setData, error, load] = useLoad("/admin/items");
  const [selected, setSelected] = useState(null);
  const [form, setForm] = useState(null);
  const [filter, setFilter] = useState("all");
  const [search, setSearch] = useState("");
  const [busy, setBusy] = useState(false);

  const items = useMemo(() => (data?.items || []).filter((i) =>
    (filter === "all" || i.kind === filter) && (!search || i.name.toLowerCase().includes(search.toLowerCase()) || i.key.includes(search.toLowerCase()))), [data, filter, search]);

  function pick(item) {
    setSelected(item.key);
    setForm({ ...item, ...item.stats, isNew: false });
  }
  function startNew() {
    setSelected(null);
    setForm({ isNew: true, key: "", name: "", kind: data.slots[0]?.key || "helmet", icon: "✦", description: "", rarity: "common", priceCoins: 300, priceGems: 0, inShop: true, image: "", imageUrl: "", owned: 0, equipped: 0, ...Object.fromEntries(STATS.map((s) => [s, 0])) });
  }
  const set = (key, value) => setForm((f) => ({ ...f, [key]: value }));
  // Saved rows come back without the ownership counts, so keep the ones already shown.
  const merge = (saved) => setData((d) => ({ ...d, items: d.items.map((i) => (i.key === saved.key ? { ...i, ...saved } : i)) }));

  async function save() {
    setBusy(true);
    try {
      const body = {
        key: form.key, name: form.name, kind: form.kind, icon: form.icon, description: form.description, rarity: form.rarity, image: form.image,
        priceCoins: Number(form.priceCoins) || 0, priceGems: Number(form.priceGems) || 0, inShop: Boolean(form.inShop),
        ...Object.fromEntries(STATS.map((s) => [s, Number(form[s]) || 0]))
      };
      const saved = form.isNew
        ? await call("/admin/items", { method: "POST", body: JSON.stringify(body) })
        : await call(`/admin/items/${form.key}`, { method: "PUT", body: JSON.stringify(body) });
      await load();
      setSelected(saved.key);
      setForm((f) => ({ ...f, ...saved, ...saved.stats, isNew: false }));
      notify(form.isNew ? `${saved.name} created.` : `${saved.name} saved.`);
    } catch (e) { notify(e.message, true); }
    setBusy(false);
  }

  async function upload(file) {
    if (!file || form.isNew) return;
    setBusy(true);
    try {
      const body = new FormData();
      body.append("file", file);
      const saved = await call(`/admin/items/${form.key}/image`, { method: "POST", body });
      merge(saved);
      setForm((f) => ({ ...f, image: saved.image, imageUrl: saved.imageUrl }));
      notify("Picture uploaded. Players see it on their next refresh.");
    } catch (e) { notify(e.message, true); }
    setBusy(false);
  }

  async function resetImage() {
    setBusy(true);
    try {
      const saved = await call(`/admin/items/${form.key}/image`, { method: "DELETE" });
      merge(saved);
      setForm((f) => ({ ...f, image: saved.image, imageUrl: saved.imageUrl }));
      notify("Picture reset to the default file.");
    } catch (e) { notify(e.message, true); }
    setBusy(false);
  }

  if (error) return <p className="adm-error">{error}</p>;
  if (!data) return <p className="adm-muted">Loading…</p>;
  const totalPoints = form ? STATS.reduce((sum, s) => sum + (Number(form[s]) || 0), 0) : 0;
  return <>
    <div className="adm-head"><h2>Items</h2><button onClick={startNew}>New item</button></div>
    <div className="adm-split">
      <div className="adm-list">
        <div className="adm-filters">
          <input placeholder="Search items" value={search} onChange={(e) => setSearch(e.target.value)} />
          <select value={filter} onChange={(e) => setFilter(e.target.value)}><option value="all">All slots</option>{data.slots.map((s) => <option key={s.key} value={s.key}>{s.name}</option>)}</select>
        </div>
        {items.map((item) => <button key={item.key} className={`adm-row ${selected === item.key ? "on" : ""}`} onClick={() => pick(item)}>
          <Thumb item={item} />
          <span><b>{item.name}</b><small>{item.kind} · {item.rarity}{item.inShop ? "" : " · not in shop"} · owned by {item.owned ?? 0}</small></span>
          <em>{item.priceGems ? `${item.priceGems} gems` : `${item.priceCoins} c`}</em>
        </button>)}
      </div>
      <div className="adm-editor">
        {!form && <p className="adm-muted">Pick an item to edit it, or make a new one.</p>}
        {form && <>
          {!form.isNew && <div className="adm-facts small">
            <div><span>Owned by</span><b>{form.owned ?? 0} players</b></div>
            <div><span>Worn by</span><b>{form.equipped ?? 0} players</b></div>
            <div><span>Starter item</span><b>{form.isStarter ? "Yes, everyone gets it" : "No"}</b></div>
            <div><span>Total points</span><b>{totalPoints}</b></div>
          </div>}
          <div className="adm-image">
            <Thumb item={form} big />
            <div>
              <p className="adm-muted">{form.image ? "Uploaded picture" : "Default file: /items/" + (form.key || "<key>") + ".png"}</p>
              {form.isNew ? <p className="adm-muted">Save the item first, then upload its picture.</p> : <>
                <label className="adm-file"><input type="file" accept="image/png,image/jpeg,image/webp" disabled={busy} onChange={(e) => { upload(e.target.files?.[0]); e.target.value = ""; }} />Upload picture</label>
                {form.image && <button className="ghost" disabled={busy} onClick={resetImage}>Use default file</button>}
              </>}
              <p className="adm-hint">PNG, JPEG or WebP, up to 1 MB. Square works best; WebP keeps files small.</p>
            </div>
          </div>
          <div className="adm-grid">
            {form.isNew && <Field label="Key (permanent, e.g. storm-helm)"><input value={form.key} onChange={(e) => set("key", e.target.value.toLowerCase().replace(/[^a-z0-9-]/g, ""))} /></Field>}
            <Field label="Name"><input value={form.name} onChange={(e) => set("name", e.target.value)} /></Field>
            <Field label="Slot"><select value={form.kind} onChange={(e) => set("kind", e.target.value)}>{data.slots.map((s) => <option key={s.key} value={s.key}>{s.name}</option>)}</select></Field>
            <Field label="Rarity"><select value={form.rarity} onChange={(e) => set("rarity", e.target.value)}>{RARITIES.map((r) => <option key={r}>{r}</option>)}</select></Field>
            <Field label="Symbol (shown if no picture)"><input value={form.icon} onChange={(e) => set("icon", e.target.value)} maxLength={4} /></Field>
            <Field label="Price in coins"><input type="number" min="0" value={form.priceCoins} onChange={(e) => set("priceCoins", e.target.value)} /></Field>
            <Field label="Price in gems (0 = coins)"><input type="number" min="0" value={form.priceGems} onChange={(e) => set("priceGems", e.target.value)} /></Field>
            <Field label="Sold in the shop"><Toggle value={form.inShop} onChange={(v) => set("inShop", v)} /></Field>
            <Field label="Description" wide><textarea rows={2} value={form.description} onChange={(e) => set("description", e.target.value)} /></Field>
          </div>
          <h3 className="adm-sub">Attribute points (0–5 each; each attribute is capped in duels)</h3>
          <div className="adm-stat-grid">{STATS.map((s) => <Field key={s} label={s}><input type="number" min="0" max="5" value={form[s]} onChange={(e) => set(s, e.target.value)} /></Field>)}</div>
          <div className="adm-actions"><button disabled={busy} onClick={save}>{form.isNew ? "Create item" : "Save changes"}</button></div>
        </>}
      </div>
    </div>
  </>;
}

function Thumb({ item, big }) {
  const [failed, setFailed] = useState(false);
  const src = item.imageUrl || (item.key ? `/items/${item.key}.png` : "");
  useEffect(() => setFailed(false), [src]);
  return <span className={`adm-thumb ${big ? "big" : ""} r-${item.rarity}`}>{src && !failed ? <img src={src} alt="" onError={() => setFailed(true)} /> : <i>{item.icon || "✦"}</i>}</span>;
}

/* ---------- Talismans ---------- */
function Talismans({ notify }) {
  const [rows, setRows, error] = useLoad("/admin/talismans");
  const edit = (key, field, value) => setRows((r) => r.map((t) => (t.key === key ? { ...t, [field]: value, dirty: true } : t)));
  async function save(t) {
    try {
      const saved = await call(`/admin/talismans/${t.key}`, { method: "PUT", body: JSON.stringify({
        name: t.name, icon: t.icon, effect: t.effect, drawback: t.drawback, priceCoins: Number(t.priceCoins) || 0,
        enabled: t.enabled, ranked: t.ranked, rarity: t.rarity, budget: Number(t.budget) || 0
      }) });
      setRows((r) => r.map((x) => (x.key === t.key ? saved : x)));
      notify(saved.warnings.length ? `${saved.name} saved, but its text still leaves something out.` : `${saved.name} saved. New duels use it straight away.`, saved.warnings.length > 0);
    } catch (e) { notify(e.message, true); }
  }
  if (error) return <p className="adm-error">{error}</p>;
  if (!rows) return <p className="adm-muted">Loading…</p>;
  return <>
    <div className="adm-head"><h2>Talismans</h2>
      <p className="adm-muted">Each card shows what the talisman really does in duels (set in the game's code), then the text players read in the shop and in duels. If the text leaves a rule out, a warning tells you what to add.</p>
    </div>
    <div className="adm-cards">{rows.map((t) => <div key={t.key} className={`adm-card ${t.enabled ? "" : "off"}`}>
      <div className="adm-card-head">
        <span className="adm-tal-icon">{t.icon}</span>
        <div><b>{t.name}</b><small>{t.key} · {t.rarity} · {t.budget} power points{t.enabled ? "" : " · turned off"}{t.ranked ? "" : " · not in ranked"}</small></div>
      </div>

      <div className="adm-rules">
        <span className="adm-label">What it does in duels</span>
        <p>{t.rules.behaviour}</p>
        <ul>{t.rules.facts.map((f) => <li key={f}>{f}</li>)}</ul>
      </div>
      {t.warnings.length > 0 && <div className="adm-warn"><b>Text does not match the rules</b><ul>{t.warnings.map((w) => <li key={w}>{w}</li>)}</ul></div>}

      <div className="adm-facts small">
        <div><span>Owned by</span><b>{t.stats?.owned ?? 0}</b></div>
        <div><span>Equipped by</span><b>{t.stats?.equipped ?? 0}</b></div>
        <div><span>PvP duels</span><b>{t.stats?.pvp.duels ?? 0}</b></div>
        <div><span>PvP win rate</span><b>{pct(t.stats?.pvp.winRate)}</b></div>
      </div>

      <div className="adm-preview"><span className="adm-label">Players read</span><p><b>{t.name}</b> — {t.effect}{t.drawback ? <> <em>{t.drawback}</em></> : null}</p></div>

      <div className="adm-grid">
        <Field label="Name"><input value={t.name} onChange={(e) => edit(t.key, "name", e.target.value)} /></Field>
        <Field label="Symbol"><input value={t.icon} maxLength={4} onChange={(e) => edit(t.key, "icon", e.target.value)} /></Field>
        <Field label="Price in coins"><input type="number" min="0" value={t.priceCoins} onChange={(e) => edit(t.key, "priceCoins", e.target.value)} /></Field>
        <Field label="Rarity"><select value={t.rarity} onChange={(e) => edit(t.key, "rarity", e.target.value)}>{RARITIES.map((r) => <option key={r}>{r}</option>)}</select></Field>
        <Field label="Power points (budget)"><input type="number" min="0" max="10" value={t.budget} onChange={(e) => edit(t.key, "budget", e.target.value)} /></Field>
        <Field label="Effect text (what players read)" wide><textarea rows={2} value={t.effect} onChange={(e) => edit(t.key, "effect", e.target.value)} /></Field>
        <Field label="Cost text (the downside players read)" wide><input value={t.drawback} onChange={(e) => edit(t.key, "drawback", e.target.value)} /></Field>
        <Field label="Turned on (shop and duels)"><Toggle value={t.enabled} onChange={(v) => edit(t.key, "enabled", v)} /></Field>
        <Field label="Allowed in ranked"><Toggle value={t.ranked} onChange={(v) => edit(t.key, "ranked", v)} /></Field>
      </div>
      <div className="adm-actions"><button disabled={!t.dirty} onClick={() => save(t)}>Save</button></div>
    </div>)}</div>
  </>;
}

/* ---------- Attributes ---------- */
function Attributes({ notify }) {
  const [rows, setRows, error] = useLoad("/admin/attributes");
  const edit = (key, field, value) => setRows((r) => r.map((a) => (a.key === key ? { ...a, [field]: value, dirty: true } : a)));
  async function save(a) {
    try {
      const saved = await call(`/admin/attributes/${a.key}`, { method: "PUT", body: JSON.stringify({ name: a.name, effect: a.effect, cap: Number(a.cap) }) });
      setRows((r) => r.map((x) => (x.key === a.key ? saved : x)));
      notify(`${saved.name} saved. It applies to duels that start from now.`);
    } catch (e) { notify(e.message, true); }
  }
  if (error) return <p className="adm-error">{error}</p>;
  if (!rows) return <p className="adm-muted">Loading…</p>;
  return <>
    <div className="adm-head"><h2>Attributes</h2><p className="adm-muted">The cap is the most of each attribute that counts in a duel, however much gear a player wears.</p></div>
    <table className="adm-table"><thead><tr><th></th><th>Name</th><th>Cap</th><th>Effect text</th><th></th></tr></thead>
      <tbody>{rows.map((a) => <tr key={a.key}>
        <td className="adm-icon">{a.icon}</td>
        <td><input value={a.name} onChange={(e) => edit(a.key, "name", e.target.value)} /></td>
        <td className="narrow"><input type="number" min="0" max="10" value={a.cap} onChange={(e) => edit(a.key, "cap", e.target.value)} /></td>
        <td><input value={a.effect} onChange={(e) => edit(a.key, "effect", e.target.value)} /></td>
        <td><button disabled={!a.dirty} onClick={() => save(a)}>Save</button></td>
      </tr>)}</tbody></table>
  </>;
}

/* ---------- Game settings ---------- */
function Settings({ notify }) {
  const [rows, setRows, error] = useLoad("/admin/settings");
  const [search, setSearch] = useState("");
  const edit = (key, value) => setRows((r) => r.map((s) => (s.key === key ? { ...s, value, dirty: true } : s)));
  async function save(s) {
    try {
      const saved = await call(`/admin/settings/${s.key}`, { method: "PUT", body: JSON.stringify({ value: String(s.value) }) });
      setRows((r) => r.map((x) => (x.key === s.key ? saved : x)));
      notify(`${saved.key} is now ${saved.value}. It is live.`);
    } catch (e) { notify(e.message, true); }
  }
  if (error) return <p className="adm-error">{error}</p>;
  if (!rows) return <p className="adm-muted">Loading…</p>;
  const shown = rows.filter((s) => !search || `${s.key} ${s.description}`.toLowerCase().includes(search.toLowerCase()));
  return <>
    <div className="adm-head"><h2>Game settings</h2><input className="adm-search" placeholder="Search settings" value={search} onChange={(e) => setSearch(e.target.value)} /></div>
    <table className="adm-table"><thead><tr><th>Setting</th><th>What it does</th><th>Value</th><th></th></tr></thead>
      <tbody>{shown.map((s) => <tr key={s.key}>
        <td><code>{s.key}</code></td>
        <td className="adm-muted">{s.description}</td>
        <td className="mid"><input value={s.value} onChange={(e) => edit(s.key, e.target.value)} onKeyDown={(e) => { if (e.key === "Enter" && s.dirty) save(s); }} /></td>
        <td><button disabled={!s.dirty} onClick={() => save(s)}>Save</button></td>
      </tr>)}</tbody></table>
  </>;
}

/* ---------- Announce ---------- */
function Announce({ notify }) {
  const [subject, setSubject] = useState("");
  const [body, setBody] = useState("");
  const [busy, setBusy] = useState(false);
  async function send(e) {
    e.preventDefault();
    if (!window.confirm("Send this letter to every player?")) return;
    setBusy(true);
    try {
      const result = await call("/admin/announce", { method: "POST", body: JSON.stringify({ subject, body }) });
      notify(`Sent to ${result.sent} players.`);
      setSubject(""); setBody("");
    } catch (err) { notify(err.message, true); }
    setBusy(false);
  }
  return <>
    <div className="adm-head"><h2>Announce</h2><p className="adm-muted">Sends a letter from “The Rune Council” to every player's mailbox. To write to one player, open them on the Players tab.</p></div>
    <form className="adm-announce" onSubmit={send}>
      <Field label="Subject" wide><input value={subject} maxLength={120} onChange={(e) => setSubject(e.target.value)} /></Field>
      <Field label="Message" wide><textarea rows={8} value={body} onChange={(e) => setBody(e.target.value)} /></Field>
      <div className="adm-actions"><button disabled={busy || !subject.trim() || !body.trim()}>Send to everyone</button></div>
    </form>
  </>;
}

function Field({ label, wide, children }) {
  return <label className={`adm-field ${wide ? "wide" : ""}`}><span>{label}</span>{children}</label>;
}

function Toggle({ value, onChange, disabled }) {
  return <button type="button" className={`adm-toggle ${value ? "on" : ""}`} aria-pressed={Boolean(value)} disabled={disabled} onClick={() => onChange(!value)}><i /></button>;
}
