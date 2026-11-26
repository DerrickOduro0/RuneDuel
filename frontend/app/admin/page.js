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
    const error = new Error(data.error?.message || `Request failed (${response.status}).`);
    error.status = response.status;
    throw error;
  }
  return data;
}

const TABS = [
  ["overview", "Overview"], ["items", "Items"], ["talismans", "Talismans"], ["attributes", "Attributes"],
  ["settings", "Game settings"], ["players", "Players"], ["announce", "Announce"]
];
const STATS = ["power", "guard", "vigor", "focus", "grit", "edge", "pierce", "insight", "resolve"];
const RARITIES = ["common", "rare", "epic"];

export default function AdminPage() {
  const [state, setState] = useState("loading"); // loading | denied | ready
  const [me, setMe] = useState(null);
  const [tab, setTab] = useState("overview");
  const [toast, setToast] = useState(null);

  useEffect(() => {
    call("/me").then((profile) => {
      setMe(profile);
      setState(profile.isAdmin ? "ready" : "denied");
    }).catch(() => setState("denied"));
  }, []);

  function notify(text, bad = false) {
    setToast({ text, bad, id: Date.now() });
    setTimeout(() => setToast((t) => (t && Date.now() - t.id >= 2800 ? null : t)), 3000);
  }

  if (state === "loading") return <main className="adm adm-center"><p>Checking your access…</p></main>;
  if (state === "denied") return <main className="adm adm-center">
    <div className="adm-denied"><h1>Admins only</h1><p>Sign in to the game with an admin account first, then open this page again.</p><a href="/">Back to the game</a></div>
  </main>;

  return <main className="adm">
    <header className="adm-top">
      <div><span className="adm-eyebrow">RUNE DUEL</span><h1>Admin</h1></div>
      <span className="adm-who">Signed in as <b>{me.username}</b> · <a href="/">Back to the game</a></span>
    </header>
    <nav className="adm-tabs">{TABS.map(([key, label]) => <button key={key} className={tab === key ? "on" : ""} onClick={() => setTab(key)}>{label}</button>)}</nav>
    <section className="adm-body">
      {tab === "overview" && <Overview />}
      {tab === "items" && <Items notify={notify} />}
      {tab === "talismans" && <Talismans notify={notify} />}
      {tab === "attributes" && <Attributes notify={notify} />}
      {tab === "settings" && <Settings notify={notify} />}
      {tab === "players" && <Players notify={notify} me={me} />}
      {tab === "announce" && <Announce notify={notify} />}
    </section>
    {toast && <div className={`adm-toast ${toast.bad ? "bad" : ""}`} role="status">{toast.text}</div>}
  </main>;
}

function useLoad(path) {
  const [data, setData] = useState(null);
  const [error, setError] = useState("");
  const load = () => call(path).then(setData).catch((e) => setError(e.message));
  useEffect(() => { load(); }, [path]);
  return [data, setData, error, load];
}

function Overview() {
  const [data, , error, load] = useLoad("/admin/overview");
  if (error) return <p className="adm-error">{error}</p>;
  if (!data) return <p className="adm-muted">Loading…</p>;
  const cards = [
    ["Players", data.players], ["New today", data.newToday], ["PvP duels today", data.duelsToday], ["Practice today", data.practiceToday],
    ["Duels in progress", data.activeDuels], ["Searching now", data.searching], ["Coins held", data.coins.toLocaleString()], ["Gems held", data.gems.toLocaleString()],
    ["Admins", data.admins], ["Banned", data.banned]
  ];
  return <>
    <div className="adm-head"><h2>Overview</h2><button className="ghost" onClick={load}>Refresh</button></div>
    <div className="adm-stats">{cards.map(([label, value]) => <div key={label}><b>{value}</b><span>{label}</span></div>)}</div>
  </>;
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
    setForm({ isNew: true, key: "", name: "", kind: data.slots[0]?.key || "helmet", icon: "✦", description: "", rarity: "common", priceCoins: 300, priceGems: 0, inShop: true, image: "", imageUrl: "", ...Object.fromEntries(STATS.map((s) => [s, 0])) });
  }
  const set = (key, value) => setForm((f) => ({ ...f, [key]: value }));

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
      pick(saved);
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
      setData((d) => ({ ...d, items: d.items.map((i) => (i.key === saved.key ? saved : i)) }));
      setForm((f) => ({ ...f, image: saved.image, imageUrl: saved.imageUrl }));
      notify("Picture uploaded. Players see it on their next refresh.");
    } catch (e) { notify(e.message, true); }
    setBusy(false);
  }

  async function resetImage() {
    setBusy(true);
    try {
      const saved = await call(`/admin/items/${form.key}/image`, { method: "DELETE" });
      setData((d) => ({ ...d, items: d.items.map((i) => (i.key === saved.key ? saved : i)) }));
      setForm((f) => ({ ...f, image: saved.image, imageUrl: saved.imageUrl }));
      notify("Picture reset to the default file.");
    } catch (e) { notify(e.message, true); }
    setBusy(false);
  }

  if (error) return <p className="adm-error">{error}</p>;
  if (!data) return <p className="adm-muted">Loading…</p>;
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
          <span><b>{item.name}</b><small>{item.kind} · {item.rarity}{item.inShop ? "" : " · hidden"}</small></span>
          <em>{item.priceGems ? `${item.priceGems} gems` : `${item.priceCoins} c`}</em>
        </button>)}
      </div>
      <div className="adm-editor">
        {!form && <p className="adm-muted">Pick an item to edit it, or make a new one.</p>}
        {form && <>
          <div className="adm-image">
            <Thumb item={form} big />
            <div>
              <p className="adm-muted">{form.image ? "Uploaded picture" : "Default file: /items/" + (form.key || "<key>") + ".png"}</p>
              {form.isNew ? <p className="adm-muted">Save the item first, then upload its picture.</p> : <>
                <label className="adm-file"><input type="file" accept="image/png,image/jpeg,image/webp" disabled={busy} onChange={(e) => { upload(e.target.files?.[0]); e.target.value = ""; }} />Upload picture</label>
                {form.image && <button className="ghost" disabled={busy} onClick={resetImage}>Use default file</button>}
              </>}
              <p className="adm-hint">PNG, JPEG or WebP, up to 5 MB. Square works best.</p>
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
          <h3 className="adm-sub">Attribute points (0–5, capped in duels)</h3>
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
      const saved = await call(`/admin/talismans/${t.key}`, { method: "PUT", body: JSON.stringify({ name: t.name, icon: t.icon, effect: t.effect, drawback: t.drawback, priceCoins: Number(t.priceCoins) || 0, enabled: t.enabled, ranked: t.ranked }) });
      setRows((r) => r.map((x) => (x.key === t.key ? saved : x)));
      notify(`${saved.name} saved. New duels use it straight away.`);
    } catch (e) { notify(e.message, true); }
  }
  if (error) return <p className="adm-error">{error}</p>;
  if (!rows) return <p className="adm-muted">Loading…</p>;
  return <>
    <div className="adm-head"><h2>Talismans</h2><p className="adm-muted">What each talisman does is set in code; here you change its text, price and where it can be used.</p></div>
    <div className="adm-cards">{rows.map((t) => <div key={t.key} className={`adm-card ${t.enabled ? "" : "off"}`}>
      <div className="adm-card-head"><b>{t.name}</b><small>{t.key} · {t.active ? "used in duel" : "triggers itself"} · {t.charges || "always"}{t.charges ? "× per duel" : ""}</small></div>
      <div className="adm-grid">
        <Field label="Name"><input value={t.name} onChange={(e) => edit(t.key, "name", e.target.value)} /></Field>
        <Field label="Price in coins"><input type="number" min="0" value={t.priceCoins} onChange={(e) => edit(t.key, "priceCoins", e.target.value)} /></Field>
        <Field label="Effect text" wide><textarea rows={2} value={t.effect} onChange={(e) => edit(t.key, "effect", e.target.value)} /></Field>
        <Field label="Cost text" wide><input value={t.drawback} onChange={(e) => edit(t.key, "drawback", e.target.value)} /></Field>
        <Field label="Enabled (in shop and duels)"><Toggle value={t.enabled} onChange={(v) => edit(t.key, "enabled", v)} /></Field>
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

/* ---------- Players ---------- */
function Players({ notify, me }) {
  const [query, setQuery] = useState("");
  const [rows, setRows] = useState(null);
  const [error, setError] = useState("");
  async function search(q = query) {
    try { setRows(await call(`/admin/players?q=${encodeURIComponent(q)}`)); setError(""); } catch (e) { setError(e.message); }
  }
  useEffect(() => { search(""); }, []);
  const edit = (id, field, value) => setRows((r) => r.map((p) => (p.id === id ? { ...p, [field]: value, dirty: true } : p)));
  async function save(p) {
    if (p.banned && !window.confirm(`Ban ${p.username}? They will be signed out of the game.`)) return;
    try {
      const saved = await call(`/admin/players/${p.id}`, { method: "PUT", body: JSON.stringify({ coins: Number(p.coins) || 0, gems: Number(p.gems) || 0, banned: p.banned, isAdmin: p.isAdmin }) });
      setRows((r) => r.map((x) => (x.id === p.id ? saved : x)));
      notify(`${saved.username} saved.`);
    } catch (e) { notify(e.message, true); }
  }
  return <>
    <div className="adm-head"><h2>Players</h2>
      <form className="adm-inline" onSubmit={(e) => { e.preventDefault(); search(); }}><input className="adm-search" placeholder="Name or email" value={query} onChange={(e) => setQuery(e.target.value)} /><button>Search</button></form>
    </div>
    {error && <p className="adm-error">{error}</p>}
    {!rows ? <p className="adm-muted">Loading…</p> : <table className="adm-table"><thead><tr><th>Player</th><th>Class</th><th>League</th><th>Rating</th><th>Coins</th><th>Gems</th><th>Banned</th><th>Admin</th><th></th></tr></thead>
      <tbody>{rows.map((p) => <tr key={p.id} className={p.banned ? "banned" : ""}>
        <td><b>{p.username}</b><small className="adm-block">{p.email}</small></td>
        <td>{p.className || "—"}</td>
        <td>{p.league} · {p.points} pts</td>
        <td>{p.skillRating}</td>
        <td className="narrow"><input type="number" min="0" value={p.coins} onChange={(e) => edit(p.id, "coins", e.target.value)} /></td>
        <td className="narrow"><input type="number" min="0" value={p.gems} onChange={(e) => edit(p.id, "gems", e.target.value)} /></td>
        <td><Toggle value={p.banned} disabled={p.username === me.username} onChange={(v) => edit(p.id, "banned", v)} /></td>
        <td><Toggle value={p.isAdmin} disabled={p.username === me.username} onChange={(v) => edit(p.id, "isAdmin", v)} /></td>
        <td><button disabled={!p.dirty} onClick={() => save(p)}>Save</button></td>
      </tr>)}</tbody></table>}
    {rows && rows.length === 50 && <p className="adm-muted">Showing the top 50. Search to narrow it down.</p>}
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
    <div className="adm-head"><h2>Announce</h2><p className="adm-muted">Sends a letter from “The Rune Council” to every player's mailbox.</p></div>
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
