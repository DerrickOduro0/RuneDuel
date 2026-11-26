"use client";

import { useCallback, useEffect, useMemo, useRef, useState } from "react";

const screens = {
  auth: "auth",
  classes: "classes",
  home: "home",
  arena: "arena",
  match: "match",
  league: "league",
  combat: "combat",
  realm: "realm",
  build: "build"
};


// Maintenance mode and the warning banner, checked every minute (also before signing in).
const maintenanceListeners = new Set();
let maintenanceState = { maintenance: false, message: null, backAtUtc: null, notice: null };
function setMaintenanceState(value) { maintenanceState = value; maintenanceListeners.forEach((listener) => listener(value)); }
function useMaintenanceState() {
  const [value, setValue] = useState(maintenanceState);
  useEffect(() => { maintenanceListeners.add(setValue); return () => maintenanceListeners.delete(setValue); }, []);
  return value;
}
async function checkStatus() {
  try {
    const response = await fetch("/api/v1/status");
    if (response.ok) setMaintenanceState(await response.json());
  } catch { /* offline: the next check will try again */ }
}
function localTime(value) {
  if (!value) return null;
  const date = new Date(value.endsWith?.("Z") || value.includes?.("+") ? value : `${value}Z`);
  return date.toLocaleString([], { weekday: "short", hour: "2-digit", minute: "2-digit" });
}

// Players see this instead of the game while maintenance is on. It checks again by itself.
function MaintenanceScreen({ state, onLogout, onAdminSignIn }) {
  const [checking, setChecking] = useState(false);
  async function again() { setChecking(true); await checkStatus(); setTimeout(() => setChecking(false), 600); }
  return <div className="maintenance-screen" role="alert">
    <div className="maintenance-card">
      <span className="maintenance-icon" aria-hidden="true"><Ico name="gear" size={42} /></span>
      <h1>Down for maintenance</h1>
      <p>{state.message || "Rune Duel is being updated. We will be back shortly."}</p>
      {state.backAtUtc && <p className="maintenance-back">Expected back: <b>{localTime(state.backAtUtc)}</b></p>}
      <p className="muted">This page checks every minute and opens the game as soon as it is back.</p>
      <div className="actions"><button onClick={again} disabled={checking}>{checking ? "Checking…" : "Check now"}</button>{onLogout && <button className="secondary" onClick={onLogout}>Log out</button>}</div>
      {onAdminSignIn && <button type="button" className="link-button maintenance-admin" onClick={onAdminSignIn}>Admin sign in</button>}
    </div>
  </div>;
}

// The free game server sleeps when nobody plays and takes about a minute to wake.
// While it wakes, the host answers with a gateway error, so reads are retried and a notice is shown.
const wakeListeners = new Set();
function setWaking(value) { wakeListeners.forEach((listener) => listener(value)); }
const WAKE_STATUSES = new Set([502, 503, 504]);

async function request(path, options = {}, token) {
  const method = (options.method || "GET").toUpperCase();
  const retry = method === "GET";
  const started = Date.now();
  let response;
  for (;;) {
    try {
      response = await fetch(`/api/v1${path}`, {
        ...options,
        headers: {
          "Content-Type": "application/json",
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
          ...(options.headers || {})
        }
      });
    } catch (networkError) {
      response = null;
    }
    // Maintenance also answers 503, but it is on purpose: show the maintenance screen instead of waiting.
    if (response?.status === 503) {
      const body = await response.clone().json().catch(() => null);
      if (body?.error?.code === "maintenance") {
        setWaking(false);
        setMaintenanceState({ ...maintenanceState, maintenance: true, message: body.error.message, backAtUtc: body.error.backAtUtc });
        const error = new Error(body.error.message || "The game is down for maintenance.");
        error.status = 503; error.code = "maintenance";
        throw error;
      }
    }
    const sleeping = !response || WAKE_STATUSES.has(response.status);
    if (!sleeping || !retry || Date.now() - started > 90000) break;
    setWaking(true);
    await new Promise((resolve) => setTimeout(resolve, 3000));
  }
  setWaking(false);
  if (!response) throw new Error("Could not reach the game server. Check your connection and try again.");
  const data = await response.json().catch(() => ({}));
  if (!response.ok) {
    const error = new Error(data.error?.message || (WAKE_STATUSES.has(response.status) ? "The game server is starting up. Try again in a moment." : "Request failed."));
    error.status = response.status;
    throw error;
  }
  return data;
}

// Any screen can open the profile panel: openProfile() for your own, openProfile("name") for another player.
const profileOpeners = new Set();
function openProfile(username = null) { profileOpeners.forEach((open) => open({ username })); }

// A player's picture, or their first letter when they have none.
function Avatar({ url, name, size = 42, className = "" }) {
  const [failed, setFailed] = useState(false);
  useEffect(() => setFailed(false), [url]);
  // size null: the stylesheet decides the size (the home profile chip).
  return <span className={`avatar ${className}`} style={size ? { width: size, height: size, fontSize: size * 0.42 } : undefined}>
    {url && !failed ? <img src={url} alt="" onError={() => setFailed(true)} /> : (name || "?").slice(0, 1).toUpperCase()}
  </span>;
}

// Friend requests and duel invites, checked in the background so the badge and invite banner stay current.
const friendListeners = new Set();
let friendState = { requests: 0, invites: [] };
function setFriendState(value) { friendState = value; friendListeners.forEach((listener) => listener(value)); }
function useFriendState() {
  const [value, setValue] = useState(friendState);
  useEffect(() => { friendListeners.add(setValue); return () => friendListeners.delete(setValue); }, []);
  return value;
}

function WakeNotice() {
  const [waking, setWakingState] = useState(false);
  useEffect(() => { wakeListeners.add(setWakingState); return () => wakeListeners.delete(setWakingState); }, []);
  if (!waking) return null;
  return <div className="wake-notice" role="status"><i aria-hidden="true" />Waking the server… this can take up to a minute.</div>;
}

export default function Home() {
  const [screen, setScreen] = useState(screens.auth);
  const [token, setToken] = useState(null);
  const [profile, setProfile] = useState(null);
  const [classes, setClasses] = useState([]);
  const [league, setLeague] = useState(null);
  const [match, setMatch] = useState({ status: "idle", opponent: null });
  const [combat, setCombat] = useState(null);
  const [realmPage, setRealmPage] = useState("characters");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);
  const pollRef = useRef(null);
  const [settings, setSettings] = useState(DEFAULT_SETTINGS);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [matchNotice, setMatchNotice] = useState("");
  const lastMatchStatus = useRef("idle");
  // Game content (classes, runes, forms, tips, arena modes, rules) comes from the server's database.
  const [content, setContent] = useState(null);
  const [realm, setRealm] = useState(null);
  const [realmBusy, setRealmBusy] = useState(false);
  const [realmNotice, setRealmNotice] = useState("");
  const [activeDuel, setActiveDuel] = useState(null);
  // An open friend challenge code this player is hosting: { status, code, expiresInSeconds }.
  const [challenge, setChallenge] = useState(null);
  const challengePollRef = useRef(null);

  const [contentError, setContentError] = useState("");
  const loadContent = useCallback(async () => {
    try {
      setContent(await request("/content"));
      setContentError("");
    } catch (value) {
      setContentError(value.status === 404
        ? "The game server is out of date. Stop the API, run dotnet build, then dotnet run."
        : "Could not load the game data from the server. Check that the API is running.");
    }
  }, []);
  useEffect(() => { loadContent(); }, [loadContent]);
  // Try again whenever the player opens the Arena without content.
  useEffect(() => { if (!content && screen === screens.arena) loadContent(); }, [screen, content, loadContent]);
  useEffect(() => {
    document.documentElement.dataset.reduceMotion = settings.reduceMotion ? "on" : "off";
    document.documentElement.dataset.compactHud = settings.compactHud ? "on" : "off";
  }, [settings]);

  // Settings are stored on the player's account, so they follow them to any device.
  async function updateSettings(next) {
    setSettings(next);
    try {
      setSettings(await request("/me/settings", { method: "PUT", body: JSON.stringify(next) }, token));
    } catch (value) {
      showError(value);
    }
  }

  async function refreshProfile(currentToken = token) {
    try { setProfile(await request("/me", {}, currentToken)); } catch {}
  }

  async function refreshActive(currentToken = token) {
    if (!currentToken) return null;
    try {
      const data = await request("/combat/active", {}, currentToken);
      setActiveDuel(data.active ? data : null);
      return data.active ? data : null;
    } catch {
      return null;
    }
  }

  useEffect(() => {
    if (token && (screen === screens.home || screen === screens.arena)) refreshActive();
  }, [screen, token]);

  // Friend challenges: pick up a code the player already has open, and keep checking until a friend joins.
  async function checkChallenge(currentToken = token) {
    const data = await request("/challenges/mine", {}, currentToken);
    if (data.status === "accepted" && data.matchId) {
      clearInterval(challengePollRef.current);
      setChallenge(null);
      await openCombat(data.matchId, currentToken);
      return;
    }
    if (data.status === "open") setChallenge(data);
    else { clearInterval(challengePollRef.current); setChallenge(null); }
  }
  function pollChallenge() {
    clearInterval(challengePollRef.current);
    challengePollRef.current = setInterval(() => checkChallenge().catch(() => {}), 1500);
  }
  useEffect(() => {
    if (!token || screen !== screens.arena) return;
    request("/challenges/mine", {}, token).then((data) => {
      if (data.status === "open") { setChallenge(data); pollChallenge(); }
      else if (data.status === "accepted" && data.matchId) openCombat(data.matchId);
    }).catch(() => {});
  }, [screen, token]);
  useEffect(() => () => clearInterval(challengePollRef.current), []);

  async function createChallenge() {
    setError("");
    try {
      setChallenge(await request("/challenges", { method: "POST" }, token));
      pollChallenge();
    } catch (value) {
      if (value.status === 409) { await refreshActive(); setNoticeToast(value.message); return; }
      showError(value);
    }
  }

  async function cancelChallenge() {
    clearInterval(challengePollRef.current);
    setChallenge(null);
    await request("/challenges/mine", { method: "DELETE" }, token).catch(showError);
  }

  async function joinChallenge(code) {
    setError("");
    try {
      const data = await request("/challenges/join", { method: "POST", body: JSON.stringify({ code }) }, token);
      clearInterval(challengePollRef.current);
      setChallenge(null);
      await openCombat(data.matchId);
      return true;
    } catch (value) {
      if (value.status === 409) await refreshActive();
      setNoticeToast(value.message || "Could not join that duel.");
      return false;
    }
  }

  // After a casual or friend duel: ask for a rematch. When both players have asked, the new duel opens.
  async function requestRematch() {
    if (!combat) return;
    setError("");
    try {
      const data = await request(`/combat/${combat.matchId}/rematch`, { method: "POST" }, token);
      if (data.rematch?.matchId) await openCombat(data.rematch.matchId);
      else setCombat(data);
    } catch (value) {
      showError(value);
    }
  }

  useEffect(() => {
    if (token && screen !== screens.auth) {
      window.localStorage.setItem("runeScreen", screen);
    }
  }, [screen, token]);

  const loadProfile = useCallback(async (currentToken = token) => {
    const data = await request("/me", {}, currentToken);
    setProfile(data);
    setClasses(await request("/classes"));
    request("/me/settings", {}, currentToken).then(setSettings).catch(() => {});
    if (data.className) {
      setScreen(screens.home);
      return;
    }

    setScreen(screens.classes);
  }, [token]);

  useEffect(() => {
    const storedToken = window.localStorage.getItem("runeToken");
    if (!storedToken) return;
    setToken(storedToken);
    loadProfile(storedToken).then(async () => {
      const savedScreen = window.localStorage.getItem("runeScreen");
      if (savedScreen === screens.combat) {
        const duel = await refreshActive(storedToken);
        if (duel) {
          await openCombat(duel.matchId, storedToken);
          return;
        }
        window.localStorage.setItem("runeScreen", screens.home);
      }
      if (savedScreen === screens.league) {
        setLeague(await request("/league", {}, storedToken));
        setScreen(screens.league);
        return;
      }
      if (savedScreen === screens.arena) {
        setScreen(screens.arena);
        return;
      }
      if (savedScreen === screens.build) {
        setScreen(screens.build);
        request("/realm", {}, storedToken).then(setRealm).catch(() => {});
        return;
      }
      if (savedScreen === screens.match) {
        const currentMatch = await request("/matchmaking/status", {}, storedToken);
        if (currentMatch.status === "ready" && currentMatch.matchId) {
          await openCombat(currentMatch.matchId, storedToken);
          return;
        }
        if (currentMatch.status === "idle") {
          setScreen(screens.arena);
          return;
        }
        setMatch(currentMatch);
        lastMatchStatus.current = currentMatch.status;
        setScreen(screens.match);
        startMatchPolling(storedToken);
      }
    }).catch((error) => {
      // Only a rejected login signs the player out. Maintenance or a sleeping server keeps them signed in for later.
      if (error?.code === "maintenance" || !(error?.status === 401 || error?.status === 403 || error?.status === 404)) return;
      window.localStorage.removeItem("runeToken");
      window.localStorage.removeItem("runeScreen");
      window.localStorage.removeItem("runeMatchId");
      setToken(null);
    });
  }, [loadProfile]);

  useEffect(() => () => clearInterval(pollRef.current), []);

  function showError(value) {
    setError(value instanceof Error ? value.message : value);
  }

  async function submitAuth(event, mode) {
    event.preventDefault();
    setLoading(true);
    setError("");
    const form = new FormData(event.currentTarget);
    const body = mode === "login"
      ? { identifier: form.get("identifier"), password: form.get("password") }
      : { username: form.get("username"), email: form.get("email"), password: form.get("password") };
    try {
      clearInterval(pollRef.current);
      window.localStorage.removeItem("runeScreen");
      window.localStorage.removeItem("runeMatchId");
      const data = await request(`/auth/${mode}`, { method: "POST", body: JSON.stringify(body) });
      window.localStorage.setItem("runeToken", data.token);
      setToken(data.token);
      await loadProfile(data.token);
    } catch (value) {
      showError(value);
    } finally {
      setLoading(false);
    }
  }

  async function chooseClass(className) {
    setLoading(true);
    setError("");
    try {
      const data = await request("/me/class", {
        method: "PUT",
        body: JSON.stringify({ className })
      }, token);
      setProfile(data);
      setScreen(screens.home);
    } catch (value) {
      showError(value);
    } finally {
      setLoading(false);
    }
  }

  async function logout() {
    setFriendState({ requests: 0, invites: [] });
    clearInterval(pollRef.current);
    if (token) {
      try {
        await request("/matchmaking/queue", { method: "DELETE" }, token);
      } catch (value) {
        showError(value);
      }
    }
    window.localStorage.removeItem("runeToken");
    window.localStorage.removeItem("runeScreen");
    window.localStorage.removeItem("runeMatchId");
    setToken(null);
    setProfile(null);
    setScreen(screens.auth);
  }

  async function refreshMatch(currentToken = token) {
    const data = await request("/matchmaking/status", {}, currentToken);
    const previous = lastMatchStatus.current;
    lastMatchStatus.current = data.status;
    if (data.status === "ready" && data.matchId) {
      clearInterval(pollRef.current);
      setMatch({ status: "idle", opponent: null });
      await openCombat(data.matchId, currentToken);
      return;
    }
    if (previous === "found" && data.status === "idle") {
      clearInterval(pollRef.current);
      setMatch({ status: "idle", opponent: null });
      setMatchNotice("You did not enter the match in time, so the search was ended.");
      setScreen(screens.arena);
      return;
    }
    if (previous === "found" && data.status === "searching") {
      setMatchNotice("");
      setMatch((current) => ({ ...data, mode: data.mode || current.mode }));
      setNoticeToast("The match was not accepted by everyone. Searching again…");
      return;
    }
    setMatch((current) => ({ ...data, mode: data.mode || current.mode }));
  }

  function setNoticeToast(message) {
    setError("");
    setMatchNotice(message);
    window.setTimeout(() => setMatchNotice((current) => (current === message ? "" : current)), 4000);
  }

  function startMatchPolling(currentToken = token) {
    clearInterval(pollRef.current);
    pollRef.current = setInterval(() => refreshMatch(currentToken).catch(showError), 800);
  }

  function openArena() {
    setError("");
    setMatchNotice("");
    setScreen(screens.arena);
  }

  async function findMatch(mode = "ranked") {
    setError("");
    setMatchNotice("");
    lastMatchStatus.current = "searching";
    setMatch({ status: "searching", opponent: null, mode });
    setScreen(screens.match);
    try {
      await request("/matchmaking/queue", { method: "POST", body: JSON.stringify({ mode }) }, token);
      await refreshMatch();
      startMatchPolling();
    } catch (value) {
      setScreen(screens.arena);
      if (value.status === 409) {
        await refreshActive();
        setNoticeToast(value.message);
        return;
      }
      showError(value);
    }
  }

  async function acceptMatch() {
    try {
      setError("");
      const result = await request("/matchmaking/accept", { method: "POST" }, token);
      lastMatchStatus.current = "found";
      await refreshMatch();
      if (result && result.ready) await refreshMatch();
    } catch (value) {
      showError(value);
    }
  }

  async function cancelMatch() {
    clearInterval(pollRef.current);
    lastMatchStatus.current = "idle";
    await request("/matchmaking/queue", { method: "DELETE" }, token).catch(showError);
    setMatch({ status: "idle", opponent: null });
    window.localStorage.removeItem("runeMatchId");
    setScreen(screens.arena);
  }

  // Maintenance: check now and every minute. When it ends, reload the player so the game picks up where it was.
  const maintenance = useMaintenanceState();
  useEffect(() => {
    checkStatus();
    const timer = setInterval(checkStatus, 60000);
    return () => clearInterval(timer);
  }, []);
  const wasDown = useRef(false);
  useEffect(() => {
    if (maintenance.maintenance) { wasDown.current = true; return; }
    // Back from maintenance: load the player again (their first load may have been refused).
    if (wasDown.current) { wasDown.current = false; if (token) loadProfile(token).catch(() => {}); }
  }, [maintenance.maintenance]);
  const [noticeHidden, setNoticeHidden] = useState("");
  const [adminSignIn, setAdminSignIn] = useState(false);

  // The profile panel (yours or another player's).
  const [profileView, setProfileView] = useState(null);
  useEffect(() => { profileOpeners.add(setProfileView); return () => profileOpeners.delete(setProfileView); }, []);

  // Friends: refresh the badge and invites every 30 seconds while signed in.
  const refreshFriends = useCallback(async (currentToken = token) => {
    if (!currentToken) return;
    try { setFriendState(await request("/friends/summary", {}, currentToken)); } catch { /* the next check will try again */ }
  }, [token]);
  useEffect(() => {
    if (!token || !profile) return;
    refreshFriends();
    const timer = setInterval(() => refreshFriends(), 30000);
    return () => clearInterval(timer);
  }, [token, profile?.username, refreshFriends]);

  // Sends a duel invite to a friend, then waits in the Arena like any friend challenge.
  async function challengeFriend(friend) {
    setError("");
    try {
      // The duel opens by itself when they join (checked every few seconds, wherever the player is).
      const data = await request(`/friends/${friend.id}/challenge`, { method: "POST" }, token);
      setChallenge(data);
      pollChallenge();
    } catch (value) {
      if (value.status === 409) await refreshActive();
      setNoticeToast(value.message || "Could not send the invite.");
    }
  }

  async function answerInvite(invite, accept) {
    if (accept) {
      const ok = await joinChallenge(invite.code);
      if (!ok) await refreshFriends();
    } else {
      await request(`/friends/invites/${invite.challengeId}/decline`, { method: "POST" }, token).catch(() => {});
    }
    await refreshFriends();
  }

  // Leaves the queue and starts a practice duel against the Shade, for when nobody else is searching.
  async function shadeFromSearch(difficulty) {
    clearInterval(pollRef.current);
    lastMatchStatus.current = "idle";
    await request("/matchmaking/queue", { method: "DELETE" }, token).catch(() => {});
    setMatch({ status: "idle", opponent: null });
    window.localStorage.removeItem("runeMatchId");
    await startShade(difficulty);
  }

  async function openCombat(matchId, currentToken = token) {
    setError("");
    try {
      setCombat(await request(`/combat/${matchId}`, {}, currentToken));
      clearInterval(challengePollRef.current);
      setChallenge(null);
      window.localStorage.setItem("runeMatchId", matchId);
      window.localStorage.setItem("runeScreen", screens.combat);
      setScreen(screens.combat);
    } catch (value) {
      showError(value);
    }
  }

  async function submitCombatAction(action) {
    try {
      const data = await request(`/combat/${combat.matchId}/action`, {
        method: "POST",
        body: JSON.stringify(action)
      }, token);
      setCombat(data);
      if (data.status === "finished") {
        window.localStorage.removeItem("runeMatchId");
        await refreshProfile();
      }
      return true;
    } catch (value) {
      showError(value);
      return false;
    }
  }

  async function loadMatchDetail(matchId) {
    try { return await request(`/matches/${matchId}`, {}, token); } catch { return null; }
  }

  async function loadHistory() {
    return await request("/matches?take=20", {}, token);
  }

  async function finishTutorial(finished) {
    try {
      const data = await request("/tutorial/complete", { method: "POST", body: JSON.stringify({ finished }) }, token);
      setProfile(data.player);
      if (data.reward > 0) setNoticeToast(`Tutorial complete! +${data.reward} coins`);
    } catch (value) {
      showError(value);
    }
  }

  async function shapeDice(shapeRequest) {
    try {
      setCombat(await request(`/combat/${combat.matchId}/shape`, { method: "POST", body: JSON.stringify(shapeRequest) }, token));
      return true;
    } catch (value) {
      showError(value);
      return false;
    }
  }

  async function useTalisman() {
    try {
      setCombat(await request(`/combat/${combat.matchId}/talisman`, { method: "POST" }, token));
      return true;
    } catch (value) {
      showError(value);
      return false;
    }
  }

  async function refreshCombat() {
    if (!combat) return;
    try {
      const fresh = await request(`/combat/${combat.matchId}`, {}, token);
      if (fresh.rematch?.matchId && fresh.rematch.mine) { await openCombat(fresh.rematch.matchId); return; }
      setCombat(fresh);
      if (fresh.status === "finished" && combat.status !== "finished") {
        window.localStorage.removeItem("runeMatchId");
        await refreshProfile();
      }
    } catch (value) {
      if (value.status === 404) {
        window.localStorage.removeItem("runeMatchId");
        window.localStorage.setItem("runeScreen", screens.home);
        setCombat(null);
        setScreen(screens.home);
        setError("That combat is no longer available.");
        return;
      }
      showError(value);
    }
  }

  async function giveUpCombat() {
    if (!combat) return;
    setError("");
    try {
      const data = await request(`/combat/${combat.matchId}/give-up`, { method: "POST" }, token);
      setCombat(data);
      window.localStorage.removeItem("runeMatchId");
      await refreshProfile();
    } catch (value) {
      showError(value);
    }
  }

  async function claimWin() {
    if (!combat) return;
    setError("");
    try {
      setCombat(await request(`/combat/${combat.matchId}/claim-win`, { method: "POST" }, token));
      await refreshProfile();
    } catch (value) {
      showError(value);
    }
  }

  // Practice against the computer opponent. Nothing in the league or quests changes.
  async function startShade(difficulty) {
    setError("");
    try {
      const data = await request("/shade/start", { method: "POST", body: JSON.stringify({ difficulty }) }, token);
      await openCombat(data.matchId);
    } catch (value) {
      if (value.status === 409) {
        await refreshActive();
        setNoticeToast(value.message);
        return;
      }
      showError(value);
    }
  }

  async function continueDuel() {
    if (!activeDuel) return;
    await openCombat(activeDuel.matchId);
  }

  async function forfeitDuel() {
    if (!activeDuel) return;
    setError("");
    try {
      await request(`/combat/${activeDuel.matchId}/give-up`, { method: "POST" }, token);
      setActiveDuel(null);
      setCombat(null);
      window.localStorage.removeItem("runeMatchId");
      await refreshProfile();
      setNoticeToast("You forfeited the duel. Your opponent takes the win.");
    } catch (value) {
      showError(value);
      await refreshActive();
    }
  }

  async function loadRealm() {
    try { setRealm(await request("/realm", {}, token)); } catch (value) { showError(value); }
  }

  async function realmAction(path) {
    setRealmBusy(true);
    setError("");
    try {
      const data = await request(path, { method: "POST" }, token);
      setRealm(data);
      if (data.notice) {
        setRealmNotice(data.notice);
        window.setTimeout(() => setRealmNotice((current) => (current === data.notice ? "" : current)), 2600);
      }
      await refreshProfile();
    } catch (value) {
      showError(value);
    } finally {
      setRealmBusy(false);
    }
  }

  async function endFoundMatch(matchId) {
    setError("");
    try {
      await request(`/combat/${matchId}/give-up`, { method: "POST" }, token);
      setMatch({ status: "idle", opponent: null });
      window.localStorage.removeItem("runeMatchId");
      window.localStorage.setItem("runeScreen", screens.home);
      setScreen(screens.home);
      await loadProfile();
    } catch (value) {
      showError(value);
    }
  }

  async function openLeague() {
    setError("");
    try {
      setLeague(await request("/league"));
      setScreen(screens.league);
    } catch (value) {
      showError(value);
    }
  }

  function openRealm(page) {
    if (page === "settings") { setSettingsOpen(true); return; }
    if (page === "arena") { openArena(); return; }
    if (page === "rankings") { openLeague(); return; }
    if (page === "inventory" || page === "build") {
      setScreen(screens.build);
      loadRealm();
      return;
    }
    setRealmPage(page);
    setScreen(screens.realm);
    loadRealm();
  }

  async function recordResult(win) {
    setError("");
    try {
      const data = await request("/test/results", {
        method: "POST",
        body: JSON.stringify({ win })
      }, token);
      setProfile(data);
    } catch (value) {
      showError(value);
    }
  }

  async function turnWheel() {
    setError("");
    try {
      await request("/test/wheel", { method: "POST" }, token);
      await loadProfile();
      await openLeague();
    } catch (value) {
      showError(value);
    }
  }

  return (
    <main className="game-shell">
      <WakeNotice />
      {maintenance.maintenance && !profile?.isAdmin && screen !== screens.combat && !(adminSignIn && !profile) && <MaintenanceScreen state={maintenance} onLogout={profile ? () => { setAdminSignIn(false); logout(); } : null} onAdminSignIn={profile ? null : () => setAdminSignIn(true)} />}
      {maintenance.maintenance && profile?.isAdmin && <div className="maintenance-strip">Maintenance mode is on: players cannot play. <a href="/admin">Open admin</a></div>}
      {!maintenance.maintenance && maintenance.notice && noticeHidden !== maintenance.notice && <div className="maintenance-notice" role="status"><span><b>Notice</b> {maintenance.notice}</span><button className="secondary" onClick={() => setNoticeHidden(maintenance.notice)} aria-label="Hide notice">×</button></div>}
      {profile && screen !== screens.combat && screen !== screens.match && <FriendInviteBanner onAnswer={answerInvite} />}
      {profile && profileView && <ProfilePanel token={token} username={profileView.username} me={profile} onClose={() => setProfileView(null)} onChanged={() => refreshProfile()} onLogout={() => { setProfileView(null); logout(); }} />}
      <header className="game-header">
        <div className="brand-lockup"><div className="rune-crest">ᚱ</div><div><h1>Rune Duel</h1><p className="sub">The arcane arena awaits</p></div></div>
        {content?.season && <div className="season-badge">SEASON {content.season.number} <span>•</span> {content.season.title.toUpperCase()}</div>}
      </header>
      {error && <div className="message" role="alert">{error}</div>}
      {screen === screens.auth && <Auth onSubmit={submitAuth} loading={loading} />}
      {screen === screens.classes && <ClassSelection classes={classes} loading={loading} onChoose={chooseClass} />}
      {screen === screens.home && profile && <HomeScreen profile={profile} duel={activeDuel} onTutorial={() => startShade("tutorial")} onSkipTutorial={() => finishTutorial(false)} onContinue={continueDuel} onForfeit={forfeitDuel} notice={matchNotice} onLogout={logout} onMatch={openArena} onLeague={openLeague} onResult={recordResult} onWheel={turnWheel} onNavigate={openRealm} />}
      {screen === screens.build && profile && <BuildScreen profile={profile} content={content} realm={realm} busy={realmBusy} notice={realmNotice} onAction={realmAction} onBack={() => setScreen(screens.home)} onNavigate={openRealm} onLogout={logout} />}
      {screen === screens.arena && profile && <ArenaScreen profile={profile} content={content} contentError={contentError} onRetryContent={loadContent} duel={activeDuel} onContinue={continueDuel} onForfeit={forfeitDuel} notice={matchNotice} onBack={() => setScreen(screens.home)} onFind={findMatch} onShade={startShade} challenge={challenge} onCreateChallenge={createChallenge} onCancelChallenge={cancelChallenge} onJoinChallenge={joinChallenge} onNavigate={openRealm} onLogout={logout} />}
      {screen === screens.match && <>
        <SearchScreen match={match} tips={content?.tips || []} onCancel={cancelMatch} onShade={shadeFromSearch} profile={profile} onNavigate={openRealm} onLogout={logout} />
        {match.status === "found" && <MatchFoundOverlay match={match} error={error} onAccept={acceptMatch} onDecline={cancelMatch} />}
        {matchNotice && <div className="toast" role="status">{matchNotice}</div>}
      </>}
      {screen === screens.combat && combat && <CombatScreen key={combat.matchId} settings={settings} content={content} combat={combat} onSubmit={submitCombatAction} onShape={shapeDice} onTalisman={useTalisman} onRefresh={refreshCombat} onGiveUp={giveUpCombat} onClaimWin={claimWin} onRematch={startShade} onAskRematch={requestRematch} onFindCasual={() => findMatch("casual")} onLoadRounds={loadMatchDetail} onTutorialEnd={finishTutorial} onHome={() => { setScreen(screens.home); }} onLeague={openLeague} />}
      {screen === screens.league && profile && <RankScreen league={league} content={content} profile={profile} loadHistory={loadHistory} loadRounds={loadMatchDetail} onBack={() => setScreen(screens.home)} onNavigate={openRealm} onLogout={logout} />}
      {screen === screens.realm && profile && <RealmPage page={realmPage} content={content} profile={profile} classes={classes} league={league} realm={realm} busy={realmBusy} notice={realmNotice} onAction={realmAction} onBack={() => setScreen(screens.home)} onNavigate={openRealm} onLogout={logout} onLeague={openLeague} token={token} challenge={challenge} onChallengeFriend={challengeFriend} onCancelChallenge={cancelChallenge} onAnswerInvite={answerInvite} onFriendsChanged={refreshFriends} />}
      {settingsOpen && profile && <SettingsModal profile={profile} settings={settings} defaults={content?.settingsDefaults} onChange={updateSettings} onClose={() => setSettingsOpen(false)} onLogout={() => { setSettingsOpen(false); logout(); }} />}
    </main>
  );
}

function Auth({ onSubmit, loading }) {
  const [registering, setRegistering] = useState(false);
  return (
    <section className="panel auth-panel">
      <div className="tabs">
        <button className={!registering ? "active" : ""} onClick={() => setRegistering(false)}>Log in</button>
        <button className={registering ? "active" : ""} onClick={() => setRegistering(true)}>Create account</button>
      </div>
      {registering ? (
        <form onSubmit={(event) => onSubmit(event, "register")}>
          <label>Username<input name="username" required /></label>
          <label>Email<input name="email" type="email" required /></label>
          <label>Password (8+ characters)<input name="password" type="password" minLength="8" required /></label>
          <button disabled={loading}>{loading ? "Creating…" : "Create account"}</button>
        </form>
      ) : (
        <form onSubmit={(event) => onSubmit(event, "login")}>
          <label>Username or email<input name="identifier" required /></label>
          <label>Password<input name="password" type="password" required /></label>
          <button disabled={loading}>{loading ? "Logging in…" : "Log in"}</button>
        </form>
      )}
    </section>
  );
}

function ClassSelection({ classes, loading, onChoose }) {
  return <section className="panel class-panel"><div className="eyebrow">INITIATION RITE</div><h2>Choose your class</h2><p className="step-help">Your path begins here. Choose the magic that will define your legend.</p><p className="muted">This choice is permanent for the season.</p>{classes.length === 0 ? <p>Loading classes…</p> : <div className="cards">{classes.map((item) => <article className={`card class-card ${item.name.toLowerCase()}`} key={item.name}><div className="class-glyph">{item.name.slice(0, 1)}</div><h3>{item.name}</h3><p className="class-playstyle">{item.playstyle}</p><p><strong>Core rule</strong><br />{item.coreRule}</p><ul>{item.runes.map((rune) => <li key={rune.name}><strong className="rune-symbol">{rune.symbol}</strong> <span>{rune.name} — {rune.meaning}</span></li>)}</ul><button disabled={loading} onClick={() => onChoose(item.name)}>{loading ? "Saving…" : `Choose ${item.name}`}</button></article>)}</div>}</section>;
}

function StatChips({ stats, attributes, compact = false }) {
  const list = (attributes || []).filter((item) => (stats?.[item.key] || 0) > 0);
  if (!list.length) return null;
  return <span className={`stat-chips ${compact ? "is-compact" : ""}`}>{list.map((item) => <span key={item.key} className={`stat-chip stat-${item.key}`} title={`${item.name}: ${item.effect}`}>{item.icon} +{stats[item.key]}{compact ? "" : ` ${item.name}`}</span>)}</span>;
}

function RealmPage({ page, content, profile, classes, league, realm, busy, notice, onAction, onBack, onNavigate, onLogout, onLeague, token, challenge, onChallengeFriend, onCancelChallenge, onAnswerInvite, onFriendsChanged }) {
  const titles = {
    characters: ["Characters", "Study the four paths of rune magic."],
    inventory: ["Your build", "Equip gear to shape your attributes in battle."],
    shop: ["Arcane Shop", "Spend your rewards on cosmetics and relics."],
    messages: ["Messages", "Your realm correspondence."],
    quests: ["Quests", "Season goals that pay out coins and gems."],
    missions: ["Missions", "Daily challenges for active mages."],
    rewards: ["Rewards", "Claim the rewards you have earned."],
    rankings: ["Rankings", "See how you compare with the realm."],
    friends: ["Friends", "Build your circle of rival mages."],
    forge: ["The Forge", "Turn duel spoils into gear you cannot buy."],
    story: ["Story Mode", "Uncover the forgotten history of the runes."]
  };
  const [heading, subheading] = titles[page] || titles.characters;
  const questTabs = ["quests", "missions", "rewards"];
  const loading = !realm && page !== "characters" && page !== "rankings" && page !== "friends" && page !== "forge";
  const repeatLabel = { daily: "Resets daily", season: "This season", once: "One time" };
  const attributes = content?.attributes || [];
  const slotName = Object.fromEntries((content?.slots || []).map((slot) => [slot.key, slot.name]));
  const objectiveCards = (kind) => <div className="cards">{(realm?.objectives || []).filter((item) => item.kind === kind).map((item) => <article className={`card objective is-${item.state}`} key={item.key}>
    <h3>{item.name}</h3>
    <p>{item.task}</p>
    <div className="objective-bar" role="progressbar" aria-valuemin={0} aria-valuemax={item.target} aria-valuenow={item.progress}><i style={{ width: `${Math.min(100, item.progress / item.target * 100)}%` }} /></div>
    <p className="muted">{item.progress} / {item.target} · {repeatLabel[item.repeat] || ""}</p>
    <p className="objective-reward">Reward: {item.reward}</p>
    <button disabled={busy || item.state !== "ready"} className={item.state === "ready" ? "" : "secondary"} onClick={() => onAction(`/realm/objectives/${item.key}/claim`)}>{item.state === "claimed" ? "Claimed" : item.state === "ready" ? "Claim reward" : "In progress"}</button>
  </article>)}</div>;
  return <section className="home-world realm-world menu-world" aria-label={`${heading} page`}>
    <WorldTopbar profile={profile} onNavigate={onNavigate} onLogout={onLogout} title={heading} eyebrow="RUNE REALM" onBack={page === "characters" ? () => onNavigate("inventory") : onBack} />
    <section className="realm-content">
      <div className="toolbar">
      <div><div className="eyebrow">RUNE REALM</div><h2>{heading}</h2><p className="muted">{subheading}</p></div>
      <div className="realm-toolbar-side">
        {realm && (page === "shop" || page === "inventory") && <div className="realm-wallet"><span><Ico name="gem" size={20} /> {realm.wallet.gems.toLocaleString()} gems</span><span><Ico name="coin" size={20} /> {realm.wallet.coins.toLocaleString()} coins</span></div>}
        <button className="secondary" onClick={onBack}>Back to home</button>
      </div>
      </div>
    {questTabs.includes(page) && <nav className="sub-tabs" aria-label="Quest types">
      {questTabs.map((item) => <button key={item} className={page === item ? "active" : ""} aria-current={page === item ? "page" : undefined} onClick={() => onNavigate(item)}>{item[0].toUpperCase() + item.slice(1)}{(realm?.objectives || []).some((o) => o.kind === item.replace(/s$/, "") && o.state === "ready") && <i className="tab-dot" aria-label="ready to claim" />}</button>)}
    </nav>}
    {notice && <div className="message" role="status">{notice}</div>}
    {loading && <p className="muted">Loading from the realm…</p>}
    {page === "characters" && <p className="muted page-note">The four paths of rune magic. Your class is set when you create your mage.</p>}
    {page === "characters" && <div className="cards">{classes.map((item) => <article className="card" key={item.name}><div className="class-glyph">{item.runes?.[0]?.symbol || item.name.slice(0, 1)}</div><h3>{item.name}</h3><p className="class-playstyle">{item.playstyle}</p><p>{item.coreRule}</p><p className="rune-row">{item.runes?.map((rune) => <span key={rune.name} title={`${rune.name} — ${rune.meaning}`}>{rune.symbol}</span>)}</p><span className="muted">{item.name === profile.className ? "YOUR ACTIVE CLASS" : "AVAILABLE LORE"}</span></article>)}</div>}
    {realm && page === "shop" && realm.talismans?.length > 0 && <section className="shop-section">
      <div className="shop-section-head"><h3>Talismans</h3><p className="muted">Each bends one rule of the duel. Wear one at a time; your opponent can see it.</p></div>
      <div className="cards talisman-cards">{realm.talismans.map((t) => {
        const short = realm.wallet.coins < t.priceCoins;
        return <TalismanCard key={t.key} talisman={t}>
          <p className="item-price">{t.priceCoins.toLocaleString()} coins</p>
          <button disabled={busy || t.owned || short} className={t.owned ? "secondary" : ""} onClick={() => onAction(`/realm/talismans/${t.key}/buy`)}>{t.owned ? (t.equipped ? "Equipped" : "Owned") : short ? "Not enough" : "Purchase"}</button>
        </TalismanCard>;
      })}</div>
    </section>}
    {realm && page === "shop" && <div className="shop-section-head"><h3>Gear</h3></div>}
    {realm && page === "shop" && <div className="cards">{realm.shop.map((item) => {
      const price = item.priceGems > 0 ? `${item.priceGems} gems` : `${item.priceCoins.toLocaleString()} coins`;
      const short = item.priceGems > 0 ? realm.wallet.gems < item.priceGems : realm.wallet.coins < item.priceCoins;
      return <article className={`card shop-card rarity-${item.rarity || "common"}`} key={item.key}><ItemArt item={item} className="mode-card-icon shop-item-art" /><h3>{item.name}</h3><p className="item-kind">{slotName[item.kind] || item.kind} · <b className={`rarity-label rarity-${item.rarity || "common"}`}>{item.rarity || "common"}</b></p><StatChips stats={item.stats} attributes={attributes} /><p className="muted">{item.description}</p><p className="item-price">{price}</p><button disabled={busy || item.owned || short} className={item.owned ? "secondary" : ""} onClick={() => onAction(`/realm/shop/${item.key}/buy`)}>{item.owned ? "Owned" : short ? "Not enough" : "Purchase"}</button></article>;
    })}</div>}
    {realm && page === "quests" && objectiveCards("quest")}
    {realm && page === "missions" && objectiveCards("mission")}
    {realm && page === "rewards" && objectiveCards("reward")}
    {realm && page === "messages" && <div className="letters">{realm.messages.length === 0 && <p className="muted">No letters yet.</p>}{realm.messages.map((item) => <article className={`rank-card letter ${item.read ? "is-read" : ""}`} key={item.id}><span className="eyebrow">{item.sender}</span><h3>{item.subject}</h3><p>{item.body}</p><small className="muted">{new Date(item.createdAtUtc.endsWith("Z") ? item.createdAtUtc : `${item.createdAtUtc}Z`).toLocaleString()}</small>{!item.read && <button disabled={busy} onClick={() => onAction(`/realm/messages/${item.id}/read`)}>Mark as read</button>}</article>)}</div>}
    {page === "rankings" && <div className="rank-card"><h3>League rankings</h3>{league?.leaderboard?.slice(0, 10).map((player, index) => <div className="rank-line" key={player.username}><span>#{index + 1} {player.username}</span><strong>League {player.league} · {player.points} pts</strong></div>) || <p className="muted">Open League Hall to load rankings.</p>}<button onClick={onLeague}>Open League Hall</button></div>}
    {page === "forge" && <ForgePanel token={token} attributes={attributes} slotName={slotName} />}
    {page === "friends" && <FriendsPanel token={token} challenge={challenge} onChallenge={onChallengeFriend} onCancelChallenge={onCancelChallenge} onAnswerInvite={onAnswerInvite} onChanged={onFriendsChanged} />}
    {realm && page === "story" && <div className="cards">{realm.story.chapters.map((chapter) => <article className={`card story-card ${chapter.unlocked ? "" : "is-locked"}`} key={chapter.number}><span className="eyebrow">CHAPTER {chapter.number}</span><h3>{chapter.title}</h3><p>{chapter.unlocked ? chapter.body : `Unlocks after ${chapter.unlockMatches} finished duels. You have finished ${realm.story.totalMatches}.`}</p><button disabled={busy || !chapter.unlocked || chapter.started} className={chapter.started ? "secondary" : ""} onClick={() => onAction(`/realm/story/${chapter.number}/begin`)}>{chapter.started ? "Begun" : chapter.unlocked ? "Begin chapter" : "Locked"}</button></article>)}</div>}
    </section>
    <WorldBottomNav active={page} onHome={onBack} onNavigate={onNavigate} />
  </section>;
}

function seenText(friend) {
  if (friend.inDuel) return "In a duel";
  if (friend.online) return "Online";
  if (!friend.lastSeenAtUtc) return "Not seen yet";
  const value = friend.lastSeenAtUtc.endsWith("Z") ? friend.lastSeenAtUtc : `${friend.lastSeenAtUtc}Z`;
  const minutes = (Date.now() - new Date(value).getTime()) / 60000;
  if (minutes < 60) return `Seen ${Math.max(1, Math.round(minutes))} min ago`;
  if (minutes < 60 * 24) return `Seen ${Math.round(minutes / 60)} h ago`;
  return `Seen ${Math.round(minutes / 60 / 24)} d ago`;
}

// The Friends page: find players, answer requests, see who is online and challenge them.
function FriendsPanel({ token, challenge, onChallenge, onCancelChallenge, onAnswerInvite, onChanged }) {
  const waitingFor = challenge?.invitee || null;
  const [left, setLeft] = useState(challenge?.expiresInSeconds || 0);
  useEffect(() => {
    if (!challenge) return;
    const end = Date.now() + (challenge.expiresInSeconds || 0) * 1000;
    const timer = setInterval(() => setLeft(Math.max(0, Math.round((end - Date.now()) / 1000))), 500);
    return () => clearInterval(timer);
  }, [challenge?.code, challenge?.expiresInSeconds]);
  const [data, setData] = useState(null);
  const [error, setError] = useState("");
  const [query, setQuery] = useState("");
  const [results, setResults] = useState(null);
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState("");

  const load = useCallback(async () => {
    try { setData(await request("/friends", {}, token)); setError(""); } catch (value) { setError(value.message); }
  }, [token]);
  useEffect(() => {
    load();
    const timer = setInterval(load, 20000);
    return () => clearInterval(timer);
  }, [load]);

  async function act(path, options, message) {
    setBusy(true);
    setNote("");
    try {
      const result = await request(path, options, token);
      setNote(result?.message || message || "");
      await load();
      onChanged?.();
      if (results) await search(query, true);
    } catch (value) { setNote(value.message); }
    setBusy(false);
  }
  async function search(term = query, quiet = false) {
    if (term.trim().length < 2) { setResults(null); return; }
    try { setResults(await request(`/friends/search?q=${encodeURIComponent(term.trim())}`, {}, token)); } catch (value) { if (!quiet) setNote(value.message); }
  }
  function remove(friend) {
    if (!window.confirm(`Remove ${friend.username} from your friends?`)) return;
    act(`/friends/${friend.id}`, { method: "DELETE" }, `${friend.username} was removed.`);
  }

  const relationButton = (player) => {
    if (player.relation === "friend") return <span className="friend-state">Friends</span>;
    if (player.relation === "sent") return <span className="friend-state">Request sent</span>;
    if (player.relation === "received") return <button disabled={busy} onClick={() => act(`/friends/requests/${player.requestId}/accept`, { method: "POST" }, `You and ${player.username} are now friends.`)}>Accept</button>;
    return <button disabled={busy} onClick={() => act("/friends/requests", { method: "POST", body: JSON.stringify({ username: player.username }) })}>Add friend</button>;
  };

  return <div className="friends">
    <section className="friends-block">
      <h3>Find mages</h3>
      <form className="friends-search" onSubmit={(event) => { event.preventDefault(); search(); }}>
        <input aria-label="Player name" placeholder="Type a player's name" value={query} maxLength={20} onChange={(event) => { setQuery(event.target.value); if (event.target.value.trim().length >= 2) search(event.target.value, true); else setResults(null); }} />
        <button disabled={query.trim().length < 2}>Search</button>
      </form>
      {results && (results.length === 0 ? <p className="muted">No player matches that name.</p> :
        <ul className="friend-list">{results.map((player) => <li key={player.id} className="friend-row">
          <button type="button" className="friend-who" onClick={() => openProfile(player.username)}><Avatar url={player.avatarUrl} name={player.username} className="friend-avatar" />
          <span className="friend-name"><b>{player.username}</b><small>Lv {player.level} · {player.className || "No class yet"} · League {player.league}</small></span></button>
          <span className="friend-row-actions">{relationButton(player)}</span>
        </li>)}</ul>)}
      {note && <p className="friends-note" role="status">{note}</p>}
    </section>

    {error && <p className="error">{error}</p>}
    {!data ? !error && <p className="muted">Loading your friends…</p> : <>
      {data.invites.length > 0 && <section className="friends-block is-invites">
        <h3>Duel invites</h3>
        <ul className="friend-list">{data.invites.map((invite) => <li key={invite.challengeId} className="friend-row">
          <Avatar url={invite.fromAvatar} name={invite.from} className="friend-avatar" />
          <span className="friend-name"><b>{invite.from}</b><small>{invite.fromClass} · challenges you to a duel</small></span>
          <span className="friend-row-actions"><button disabled={busy} onClick={() => onAnswerInvite(invite, true)}>Join duel</button><button className="secondary" disabled={busy} onClick={async () => { await onAnswerInvite(invite, false); load(); }}>Decline</button></span>
        </li>)}</ul>
      </section>}

      {data.incoming.length > 0 && <section className="friends-block">
        <h3>Friend requests <span className="count-pill">{data.incoming.length}</span></h3>
        <ul className="friend-list">{data.incoming.map((item) => <li key={item.requestId} className="friend-row">
          <button type="button" className="friend-who" onClick={() => openProfile(item.player.username)}><Avatar url={item.player.avatarUrl} name={item.player.username} className="friend-avatar" />
          <span className="friend-name"><b>{item.player.username}</b><small>Lv {item.player.level} · {item.player.className || "No class yet"} · League {item.player.league}</small></span></button>
          <span className="friend-row-actions">
            <button disabled={busy} onClick={() => act(`/friends/requests/${item.requestId}/accept`, { method: "POST" }, `You and ${item.player.username} are now friends.`)}>Accept</button>
            <button className="secondary" disabled={busy} onClick={() => act(`/friends/requests/${item.requestId}/decline`, { method: "POST" }, "Request declined.")}>Decline</button>
          </span>
        </li>)}</ul>
      </section>}

      <section className="friends-block">
        <h3>Your friends <span className="count-pill">{data.friends.length}</span></h3>
        {data.friends.length === 0 ? <p className="muted">No friends yet. Search for a player above and send them a request.</p> :
          <ul className="friend-list">{data.friends.map(({ friend, record }) => <li key={friend.id} className={`friend-row ${friend.online || friend.inDuel ? "is-online" : ""}`}>
            <button type="button" className="friend-who" onClick={() => openProfile(friend.username)}><span className="friend-avatar-wrap"><Avatar url={friend.avatarUrl} name={friend.username} className="friend-avatar" /><i className={`presence ${friend.inDuel ? "duel" : friend.online ? "on" : "off"}`} /></span>
            <span className="friend-name"><b>{friend.username}</b><small>Lv {friend.level} · {friend.className || "No class yet"} · League {friend.league} · {seenText(friend)}</small>
              <small className="friend-record">You vs them: {record.wins}W {record.losses}L{record.draws ? ` ${record.draws}D` : ""}</small></span></button>
            {waitingFor === friend.username
              ? <span className="friend-row-actions is-waiting"><span className="friend-wait-text">Invite sent · waiting {formatClock(left)}</span><button className="secondary" onClick={onCancelChallenge}>Cancel</button></span>
              : <span className="friend-row-actions">
                <button disabled={busy || friend.inDuel || !friend.className || Boolean(waitingFor)} title={friend.inDuel ? "They are in a duel right now" : waitingFor ? `You are waiting for ${waitingFor}` : undefined} onClick={() => onChallenge(friend)}>Challenge</button>
                <button className="secondary friend-remove" disabled={busy} aria-label={`Remove ${friend.username}`} onClick={() => remove(friend)}>Remove</button>
              </span>}
          </li>)}</ul>}
      </section>

      {data.outgoing.length > 0 && <section className="friends-block">
        <h3>Requests you sent</h3>
        <ul className="friend-list">{data.outgoing.map((item) => <li key={item.requestId} className="friend-row">
          <Avatar url={item.player.avatarUrl} name={item.player.username} className="friend-avatar" />
          <span className="friend-name"><b>{item.player.username}</b><small>Waiting for them to accept</small></span>
          <span className="friend-row-actions"><button className="secondary" disabled={busy} onClick={() => act(`/friends/requests/${item.requestId}`, { method: "DELETE" }, "Request cancelled.")}>Cancel</button></span>
        </li>)}</ul>
      </section>}
    </>}
  </div>;
}

// The Forge: craft gear and talismans from Rune Dust and Ember Shards, or salvage what you do not use.
function ForgePanel({ token, attributes, slotName, onChanged }) {
  const [data, setData] = useState(null);
  const [error, setError] = useState("");
  const [tab, setTab] = useState("recipes");
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState("");
  const [filter, setFilter] = useState("all");
  const load = useCallback(async () => {
    try { setData(await request("/forge", {}, token)); setError(""); } catch (value) { setError(value.message); }
  }, [token]);
  useEffect(() => { load(); }, [load]);

  async function craft(recipe) {
    setBusy(true); setNote("");
    try {
      setData(await request(`/forge/craft/${recipe.key}`, { method: "POST" }, token));
      setNote(`Forged ${recipe.result.name}! ${recipe.result.type === "talisman" ? "Equip it on the Character screen." : "Wear it from the Character screen."}`);
      onChanged?.();
    } catch (value) { setNote(value.message); }
    setBusy(false);
  }
  async function salvage(entry) {
    const value = [entry.dust ? `${entry.dust} Rune Dust` : null, entry.shards ? `${entry.shards} Ember Shard${entry.shards > 1 ? "s" : ""}` : null].filter(Boolean).join(" and ");
    if (!window.confirm(`Salvage ${entry.name} for ${value}? It will be gone from your collection.`)) return;
    setBusy(true); setNote("");
    try {
      const result = await request("/forge/salvage", { method: "POST", body: JSON.stringify({ type: entry.type, key: entry.key }) }, token);
      setData(result.forge);
      setNote(`Salvaged ${entry.name}: +${value}.`);
      onChanged?.();
    } catch (failure) { setNote(failure.message); }
    setBusy(false);
  }

  if (error) return <p className="error">{error}</p>;
  if (!data) return <p className="muted">Heating the Forge…</p>;
  const m = data.materials;
  const recipes = data.recipes.filter((r) => filter === "all" || (filter === "talisman" ? r.result.type === "talisman" : r.result.type === "item"));
  const art = (result) => ({ key: result.key, icon: result.icon, image: result.imageUrl || "" });

  return <div className="forge">
    <div className="forge-materials">
      <div className="forge-mat is-dust"><span className="forge-mat-icon" aria-hidden="true">✦</span><div><b>{m.dust.toLocaleString()}</b><small>Rune Dust</small></div></div>
      <div className="forge-mat is-shard"><span className="forge-mat-icon" aria-hidden="true">◆</span><div><b>{m.shards.toLocaleString()}</b><small>Ember Shards</small></div></div>
      <p className="forge-sources">Ranked win: +{data.sources.rankedWin.dust} Dust, +{data.sources.rankedWin.shards} Shard · Ranked loss: +{data.sources.rankedLoss.dust} · Casual win: +{data.sources.casualWin.dust} · Hard Shade win: +{data.sources.shadeHardWin.dust} · Each level up: +{data.sources.levelUp.shards} Shard · Friend duels give none.</p>
    </div>

    <div className="forge-tabs" role="tablist">
      <button role="tab" aria-selected={tab === "recipes"} className={tab === "recipes" ? "is-on" : ""} onClick={() => setTab("recipes")}>Recipes</button>
      <button role="tab" aria-selected={tab === "salvage"} className={tab === "salvage" ? "is-on" : ""} onClick={() => setTab("salvage")}>Salvage{data.salvage.length ? ` (${data.salvage.length})` : ""}</button>
      {tab === "recipes" && <select aria-label="Show" value={filter} onChange={(event) => setFilter(event.target.value)}><option value="all">All recipes</option><option value="item">Gear</option><option value="talisman">Talismans</option></select>}
    </div>
    {note && <p className="forge-note" role="status">{note}</p>}

    {tab === "recipes" && <div className="cards forge-cards">{recipes.map((recipe) => {
      const r = recipe.result;
      const locked = data.level < recipe.minLevel;
      return <article key={recipe.key} className={`card shop-card forge-card rarity-${r.rarity || "common"} ${recipe.owned ? "is-owned" : ""} ${locked ? "is-locked" : ""}`}>
        {r.type === "item" ? <ItemArt item={art(r)} className="mode-card-icon shop-item-art" /> : <span className="mode-card-icon forge-talisman-icon">{r.icon}</span>}
        <h3>{r.name}</h3>
        <p className="item-kind">{r.type === "item" ? (slotName[r.kind] || r.kind) : "Talisman"} · <b className={`rarity-label rarity-${r.rarity || "common"}`}>{r.rarity || "common"}</b>{r.type === "item" ? " · Forge only" : ""}</p>
        {r.type === "item" && <StatChips stats={r.stats} attributes={attributes} />}
        <p className="muted">{r.description}{r.type === "talisman" && r.cost ? <> <em className="forge-tal-cost">{r.cost}</em></> : null}</p>
        <ul className="forge-cost">
          <li className={m.dust >= recipe.cost.dust ? "ok" : "short"}>✦ {recipe.cost.dust} Dust</li>
          {recipe.cost.shards > 0 && <li className={m.shards >= recipe.cost.shards ? "ok" : "short"}>◆ {recipe.cost.shards} Shard{recipe.cost.shards > 1 ? "s" : ""}</li>}
          {recipe.cost.coins > 0 && <li className={m.coins >= recipe.cost.coins ? "ok" : "short"}>{recipe.cost.coins} coins</li>}
          {recipe.minLevel > 1 && <li className={locked ? "short" : "ok"}>Level {recipe.minLevel}</li>}
        </ul>
        <button disabled={busy || !recipe.canCraft} className={recipe.canCraft ? "" : "secondary"} title={recipe.missing.length ? `Needs ${recipe.missing.join(", ")}` : undefined} onClick={() => craft(recipe)}>
          {recipe.owned ? "Owned" : locked ? `Reach level ${recipe.minLevel}` : recipe.canCraft ? "Forge" : "Not enough"}
        </button>
      </article>;
    })}</div>}

    {tab === "salvage" && (data.salvage.length === 0
      ? <div className="rank-card"><h3>Nothing to salvage</h3><p className="muted">Items and talismans you own but are not using show here. Starter gear and anything you have equipped cannot be salvaged.</p></div>
      : <div className="cards forge-cards">{data.salvage.map((entry) => <article key={`${entry.type}-${entry.key}`} className={`card shop-card forge-card rarity-${entry.rarity || "common"}`}>
          {entry.type === "item" ? <ItemArt item={{ key: entry.key, icon: entry.icon, image: entry.imageUrl || "" }} className="mode-card-icon shop-item-art" /> : <span className="mode-card-icon forge-talisman-icon">{entry.icon}</span>}
          <h3>{entry.name}</h3>
          <p className="item-kind">{entry.type === "item" ? (slotName[entry.kind] || entry.kind) : "Talisman"} · <b className={`rarity-label rarity-${entry.rarity || "common"}`}>{entry.rarity || "common"}</b></p>
          <ul className="forge-cost"><li className="ok">+{entry.dust} Dust</li>{entry.shards > 0 && <li className="ok">+{entry.shards} Shard{entry.shards > 1 ? "s" : ""}</li>}</ul>
          <button className="secondary" disabled={busy} onClick={() => salvage(entry)}>Salvage</button>
        </article>)}</div>)}
  </div>;
}

// Crops a picture to a centred square and shrinks it to 256 x 256, so uploads stay small whatever the phone camera produced.
async function squareAvatar(file) {
  const url = URL.createObjectURL(file);
  try {
    const image = await new Promise((resolve, reject) => { const img = new Image(); img.onload = () => resolve(img); img.onerror = () => reject(new Error("That file is not a picture we can read.")); img.src = url; });
    const side = Math.min(image.naturalWidth, image.naturalHeight);
    const canvas = document.createElement("canvas");
    canvas.width = 256; canvas.height = 256;
    const context = canvas.getContext("2d");
    context.imageSmoothingQuality = "high";
    context.drawImage(image, (image.naturalWidth - side) / 2, (image.naturalHeight - side) / 2, side, side, 0, 0, 256, 256);
    const toBlob = (type, quality) => new Promise((resolve) => canvas.toBlob(resolve, type, quality));
    // WebP where the browser can make it, otherwise JPEG.
    let blob = await toBlob("image/webp", 0.86);
    if (!blob || blob.type !== "image/webp") blob = await toBlob("image/jpeg", 0.88);
    return blob;
  } finally { URL.revokeObjectURL(url); }
}

function recordText(r) {
  if (!r?.played) return "No duels yet";
  return `${r.wins}W · ${r.losses}L${r.draws ? ` · ${r.draws}D` : ""}`;
}
function sinceText(value) {
  if (!value) return "—";
  return new Date(value.endsWith?.("Z") ? value : `${value}Z`).toLocaleDateString([], { year: "numeric", month: "long", day: "numeric" });
}

// The profile panel: an overview of an account. Your own adds the picture, motto and account settings.
function ProfilePanel({ token, username, me, onClose, onChanged, onLogout }) {
  const own = !username || username.toLowerCase() === me.username.toLowerCase();
  const [data, setData] = useState(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [note, setNote] = useState("");
  const [motto, setMotto] = useState("");
  const [editingMotto, setEditingMotto] = useState(false);
  const [passwords, setPasswords] = useState({ current: "", next: "" });
  const [showPassword, setShowPassword] = useState(false);
  const fileRef = useRef(null);

  useEffect(() => {
    const path = own ? "/me/profile" : `/players/${encodeURIComponent(username)}/profile`;
    request(path, {}, token).then((d) => { setData(d); setMotto(d.motto || ""); }).catch((e) => setError(e.message));
  }, [own, username, token]);
  useEffect(() => {
    const onKey = (event) => { if (event.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  async function run(work, done) {
    setBusy(true); setNote("");
    try { const result = await work(); if (result) setData(result); if (done) setNote(done); onChanged?.(); } catch (e) { setNote(e.message); }
    setBusy(false);
  }
  async function upload(file) {
    if (!file) return;
    await run(async () => {
      const blob = await squareAvatar(file);
      const body = new FormData();
      body.append("file", blob, blob.type === "image/webp" ? "avatar.webp" : "avatar.jpg");
      const response = await fetch("/api/v1/me/avatar", { method: "POST", body, headers: { Authorization: `Bearer ${token}` } });
      const result = await response.json().catch(() => ({}));
      if (!response.ok) throw new Error(result.error?.message || "Could not upload that picture.");
      return result;
    }, "Picture updated.");
  }

  const p = data?.progress;
  const xpShare = p ? (p.maxLevel ? 100 : Math.min(100, (p.xp / Math.max(1, p.xpToNext)) * 100)) : 0;
  const tile = (label, value, sub) => <div className="profile-tile"><span>{label}</span><b>{value}</b>{sub && <small>{sub}</small>}</div>;

  return <div className="profile-overlay" role="dialog" aria-modal="true" aria-label={own ? "Your profile" : `${username}'s profile`} onClick={(event) => { if (event.target === event.currentTarget) onClose(); }}>
    <div className="profile-panel">
      <button type="button" className="profile-close" onClick={onClose} aria-label="Close">×</button>
      {error && <p className="error">{error}</p>}
      {!data ? !error && <p className="muted">Loading profile…</p> : <>
        <header className="profile-head">
          <div className="profile-avatar-wrap">
            <span className="profile-avatar-ring"><Avatar url={data.avatarUrl} name={data.username} size={108} className="profile-avatar" /><b className="level-badge big">{p.level}</b></span>
            {own && <button type="button" className="profile-avatar-edit" disabled={busy} onClick={() => fileRef.current?.click()}>{data.avatarUrl ? "Change" : "Add picture"}</button>}
            {own && <input ref={fileRef} type="file" accept="image/*" style={{ display: "none" }} aria-hidden="true" tabIndex={-1} onChange={(event) => { upload(event.target.files?.[0]); event.target.value = ""; }} />}
          </div>
          <div className="profile-id">
            <h2>{data.username}</h2>
            <p className="profile-sub">{data.className || "No class yet"} · {data.league.name}</p>
            {own && editingMotto
              ? <form className="profile-motto-edit" onSubmit={(event) => { event.preventDefault(); run(() => request("/me/profile", { method: "PUT", body: JSON.stringify({ motto }) }, token), "Motto saved.").then(() => setEditingMotto(false)); }}>
                  <input value={motto} maxLength={60} autoFocus placeholder="A line about you (60 characters)" onChange={(event) => setMotto(event.target.value)} />
                  <button disabled={busy}>Save</button><button type="button" className="secondary" onClick={() => { setMotto(data.motto || ""); setEditingMotto(false); }}>Cancel</button>
                </form>
              : <p className="profile-motto">{data.motto ? `“${data.motto}”` : own ? <span className="muted">No motto yet.</span> : null}
                  {own && <button type="button" className="link-button" onClick={() => setEditingMotto(true)}>{data.motto ? "Edit" : "Write one"}</button>}</p>}
            <div className="profile-level">
              <div className="profile-level-top"><b>Level {p.level}</b><span>{p.maxLevel ? "Highest level" : `${p.xp} / ${p.xpToNext} XP`}</span></div>
              <div className="profile-xp" role="progressbar" aria-valuemin={0} aria-valuemax={p.xpToNext} aria-valuenow={p.xp}><i style={{ width: `${xpShare}%` }} /></div>
              <small>{p.totalXp.toLocaleString()} XP in total. XP comes from every duel and never goes down.</small>
            </div>
          </div>
        </header>
        {note && <p className="profile-note" role="status">{note}</p>}
        {data.versus && <p className="profile-versus">You vs {data.username}: <b>{data.versus.wins}W · {data.versus.losses}L{data.versus.draws ? ` · ${data.versus.draws}D` : ""}</b></p>}

        <section className="profile-section">
          <h3>Standing</h3>
          <div className="profile-tiles">
            {tile("League", data.league.name, `${data.league.points} / ${data.league.pointsPerLeague} points`)}
            {tile("Best league", data.league.best)}
            {tile("Skill rating", data.rating, "Used for matchmaking")}
            {tile("This season", `${data.season.wins}W of ${data.season.matches}`)}
          </div>
        </section>

        <section className="profile-section">
          <h3>Record</h3>
          <div className="profile-tiles">
            {tile("All PvP duels", recordText(data.pvp), data.pvp.played ? `${data.pvp.winRate}% wins` : null)}
            {tile("Ranked", recordText(data.ranked))}
            {tile("Casual", recordText(data.casual))}
            {tile("Friend duels", recordText(data.friendDuels))}
            {tile("Against the Shade", recordText(data.practice))}
            {tile("Win streak", data.streak.current, `Best ${data.streak.best}`)}
          </div>
        </section>

        <section className="profile-section">
          <h3>Collection</h3>
          <div className="profile-tiles">
            {tile("Friends", data.friends)}
            {tile("Items", data.itemsOwned)}
            {tile("Talismans", data.talismansOwned, data.equippedTalisman ? `Using ${data.equippedTalisman}` : "None equipped")}
            {tile("Member since", sinceText(data.memberSince))}
            {own && data.wallet && tile("Wallet", `${data.wallet.coins.toLocaleString()} coins`, `${data.wallet.gems} gems`)}
            {own && data.wallet && tile("Forge materials", `${(data.wallet.dust ?? 0).toLocaleString()} Rune Dust`, `${data.wallet.shards ?? 0} Ember Shards`)}
          </div>
        </section>

        {own && <section className="profile-section">
          <h3>Account</h3>
          <div className="profile-account">
            <div className="profile-row"><span>Email</span><b>{data.email}</b></div>
            {data.avatarUrl && <div className="profile-row"><span>Picture</span><button type="button" className="secondary" disabled={busy} onClick={() => run(() => request("/me/avatar", { method: "DELETE" }, token), "Picture removed.")}>Remove picture</button></div>}
            {!showPassword
              ? <div className="profile-row"><span>Password</span><button type="button" className="secondary" onClick={() => setShowPassword(true)}>Change password</button></div>
              : <form className="profile-password" onSubmit={(event) => { event.preventDefault(); run(async () => { await request("/me/password", { method: "POST", body: JSON.stringify({ currentPassword: passwords.current, newPassword: passwords.next }) }, token); setPasswords({ current: "", next: "" }); setShowPassword(false); return null; }, "Password changed."); }}>
                  <input type="password" autoComplete="current-password" placeholder="Current password" value={passwords.current} onChange={(event) => setPasswords({ ...passwords, current: event.target.value })} />
                  <input type="password" autoComplete="new-password" placeholder="New password (8+ characters)" value={passwords.next} onChange={(event) => setPasswords({ ...passwords, next: event.target.value })} />
                  <div className="actions"><button disabled={busy || !passwords.current || passwords.next.length < 8}>Save password</button><button type="button" className="secondary" onClick={() => setShowPassword(false)}>Cancel</button></div>
                </form>}
            <div className="profile-row"><span>Signed in as {data.username}</span><button type="button" className="secondary" onClick={onLogout}>Log out</button></div>
          </div>
        </section>}
      </>}
    </div>
  </div>;
}

// Shown anywhere in the game (except during a duel) when a friend sends a duel invite.
function FriendInviteBanner({ onAnswer }) {
  const friends = useFriendState();
  const [busy, setBusy] = useState(false);
  const invite = friends.invites[0];
  if (!invite) return null;
  async function answer(accept) {
    setBusy(true);
    await onAnswer(invite, accept);
    setBusy(false);
  }
  return <div className="invite-banner" role="alert">
    <span className="invite-text"><b>{invite.from}</b> challenges you to a duel{friends.invites.length > 1 ? ` (+${friends.invites.length - 1} more)` : ""}</span>
    <span className="invite-actions"><button disabled={busy} onClick={() => answer(true)}>Join</button><button className="secondary" disabled={busy} onClick={() => answer(false)}>Decline</button></span>
  </div>;
}

const ICONS = {
  home: "M3 11l9-8 9 8 M5 10v10h5v-6h4v6h5V10",
  helmet: "M6 20V10a6 6 0 0 1 12 0v10z M6 12h12 M12 12v8 M9 15.5h.01 M15 15.5h.01",
  bag: "M6 8h12l1 12H5L6 8z M9 8V6a3 3 0 0 1 6 0v2 M5 13h14",
  cart: "M3 4h3l2 11h10l2-8H7 M9 20.2h.01 M17 20.2h.01",
  anvil: "M3 7h11a4 4 0 0 0 4 4h3v2h-4l-2 2v2h3v3H7v-3h3v-2l-3-3H5a2 2 0 0 1-2-2V7z",
  gear: "M12 9a3 3 0 1 0 0 6a3 3 0 1 0 0-6 M12 3v3 M12 18v3 M3 12h3 M18 12h3 M5.6 5.6l2.1 2.1 M16.3 16.3l2.1 2.1 M18.4 5.6l-2.1 2.1 M7.7 16.3l-2.1 2.1",
  scroll: "M6 3h12a2 2 0 0 1 2 2v13a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2V3z M6 3a2 2 0 0 0-2 2v1h2 M9.5 9h7 M9.5 13h7 M9.5 17h4",
  trophy: "M7 4h10v5a5 5 0 0 1-10 0V4z M7 6H4v2a3 3 0 0 0 3 3 M17 6h3v2a3 3 0 0 1-3 3 M12 14v4 M8 21h8 M9 18h6",
  friends: "M9 5a3 3 0 1 0 0 6a3 3 0 1 0 0-6 M3 20c0-3.3 2.7-6 6-6s6 2.7 6 6 M17 7a2.5 2.5 0 1 0 0 5a2.5 2.5 0 1 0 0-5 M16.5 14.2c2.9.2 4.5 2.3 4.5 5.8",
  mail: "M3 6h18v12H3z M3 7l9 7 9-7",
  gem: "M7 4h10l4 5-9 11L3 9l4-5z M3 9h18 M9.5 4L12 9l2.5-5 M12 9v11",
  coin: "M12 3a9 9 0 1 0 0 18a9 9 0 1 0 0-18 M12 7.5l1.5 3 3.3.4-2.4 2.3.6 3.3-3-1.6-3 1.6.6-3.3-2.4-2.3 3.3-.4z",
  plus: "M12 6v12 M6 12h12",
  chevron: "M9 5l7 7-7 7",
  logout: "M10 4H5v16h5 M14 8l4 4-4 4 M18 12H9",
  swords: "M4 4l11 11 M13 17l4-4 M15 15l4 4 M20 4L9 15 M7 13l4 4 M9 15l-4 4 M19.6 19.6h.01 M4.4 19.6h.01",
  crown: "M3 18h18 M4 18L3 8l5 4 4-7 4 7 5-4-1 10",
  book: "M12 6c-2-2-5-2.5-8-2v14c3-.5 6 0 8 2 2-2 5-2.5 8-2V4c-3-.5-6 0-8 2z M12 6v14"
};

function Ico({ name, size = 28 }) {
  return <svg className="ico" viewBox="0 0 24 24" width={size} height={size} fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={ICONS[name]} /></svg>;
}

// On the home screen the top bar shows the player card. On menu pages (title given) it becomes
// a slim full-width bar with a back button and the page title; the wallet is on every page.
function WorldTopbar({ profile, onNavigate, onLogout, title, eyebrow, onBack }) {
  const level = profile.progress || { level: 1, xp: 0, xpToNext: 100 };
  const xpShare = level.maxLevel ? 100 : Math.min(100, (level.xp / Math.max(1, level.xpToNext)) * 100);
  return <header className={`world-topbar ${title ? "is-menu" : ""}`}>
    {title
      ? <div className="menu-head">
          {onBack && <button className="world-icon menu-back" onClick={onBack} aria-label="Back"><span aria-hidden="true">‹</span></button>}
          <div className="menu-title">{eyebrow && <span className="eyebrow">{eyebrow}</span>}<strong>{title}</strong></div>
        </div>
      : <button type="button" className="world-player" onClick={() => openProfile()} aria-label="Open your profile">
          <span className="world-avatar-wrap"><Avatar url={profile.avatarUrl} name={profile.username} size={null} className="world-avatar" /><b className="level-badge">{level.level}</b></span>
          <div className="world-player-copy"><strong>{profile.username}</strong><span>{profile.leagueName} · {profile.points}/{profile.pointsPerLeague} pts</span><div className="world-xp"><i style={{ width: `${xpShare}%` }} /></div><small>Level {level.level}&nbsp;·&nbsp;{level.maxLevel ? "max" : `${level.xp} / ${level.xpToNext} XP`}</small></div>
        </button>}
    <div className="world-currencies">
      <div className="pill" role="button" tabIndex={0} onClick={() => onNavigate("shop")} onKeyDown={(event) => { if (event.key === "Enter") onNavigate("shop"); }} aria-label={`${profile.gems ?? 0} gems. Open the shop`}><Ico name="gem" size={26} /><strong>{(profile.gems ?? 0).toLocaleString()}</strong><b><Ico name="plus" size={20} /></b></div>
      <div className="pill" role="button" tabIndex={0} onClick={() => onNavigate("shop")} onKeyDown={(event) => { if (event.key === "Enter") onNavigate("shop"); }} aria-label={`${profile.coins ?? 0} coins. Open the shop`}><Ico name="coin" size={26} /><strong>{(profile.coins ?? 0).toLocaleString()}</strong><b><Ico name="plus" size={20} /></b></div>
      <button className="world-icon" aria-label="Messages" onClick={() => onNavigate("messages")}><Ico name="mail" size={28} /></button>
      <button className="world-icon" aria-label="Settings" onClick={() => onNavigate("settings")}><Ico name="gear" size={28} /></button>
      <button className="world-icon" aria-label="Log out" onClick={onLogout}><Ico name="logout" size={26} /></button>
    </div>
  </header>;
}

function WorldRail({ onNavigate }) {
  const friends = useFriendState();
  const friendBadge = friends.requests + friends.invites.length;
  return <aside className="world-rail" aria-label="Realm activities">
    <button onClick={() => onNavigate("quests")}><span><Ico name="scroll" size={40} /></span>Quests</button>
    <button onClick={() => onNavigate("rankings")}><span><Ico name="trophy" size={40} /></span>Rankings</button>
    <button onClick={() => onNavigate("friends")}><span><Ico name="friends" size={40} />{friendBadge > 0 && <b className="rail-badge">{friendBadge}</b>}</span>Friends</button>
    <button onClick={() => onNavigate("forge")}><span><Ico name="anvil" size={40} /></span>Forge</button>
  </aside>;
}

// The one main navigation. Settings, mail and log out live in the top bar; Missions and Rewards sit
// under Quests; class lore is reached from the Character screen.
const NAV_GROUP = { missions: "quests", rewards: "quests", characters: "inventory" };
function WorldBottomNav({ active, onHome, onNavigate }) {
  const items = [["home", "Home", "home"], ["inventory", "Character", "helmet"], ["arena", "Arena", "swords"], ["shop", "Shop", "cart"], ["quests", "Quests", "scroll"]];
  active = NAV_GROUP[active] || active;
  return <nav className="world-bottom-nav" aria-label="Main navigation">
    {items.map(([key, label, icon]) => <button key={key} className={active === key ? "selected" : ""} aria-current={active === key ? "page" : undefined} onClick={() => key === "home" ? onHome() : onNavigate(key)}><span><Ico name={icon} size={42} /></span>{label}</button>)}
  </nav>;
}

/* ---------- Character screen: the mage in the middle, gear slots around, inventory beside ----------
   Layout is drawn on a 1000 x 760 stage. The mage sits at x 300-700, y 60-620 (its 200 x 280 art at 2x),
   so a body point (bx, by) on the art lands at (300 + 2bx, 60 + 2by) on the stage. */
const DOLL_SLOTS = {
  helmet: { side: "left", y: 120, body: [98, 58], label: "Head" },
  mantle: { side: "left", y: 330, body: [62, 112], label: "Shoulders" },
  armor: { side: "left", y: 540, body: [98, 172], label: "Chest" },
  sigil: { side: "right", y: 120, body: [98, 132], label: "Neck" },
  ring: { side: "right", y: 330, body: [148, 172], label: "Hand" },
  charm: { side: "right", y: 540, body: [82, 206], label: "Waist" },
  frame: { side: "bottom", y: 690, body: null, label: "Dice" }
};
const DOLL_TILE = { w: 210, h: 150 };

function slotBox(key) {
  const slot = DOLL_SLOTS[key];
  if (!slot) return null;
  const cx = slot.side === "left" ? 125 : slot.side === "right" ? 875 : 500;
  return { x: cx - DOLL_TILE.w / 2, y: slot.y - DOLL_TILE.h / 2, cx, cy: slot.y };
}

function bodyPoint(key) {
  const body = DOLL_SLOTS[key]?.body;
  return body ? [300 + body[0] * 2, 60 + body[1] * 2] : null;
}

function BuildScreen({ profile, content, realm, busy, notice, onAction, onBack, onNavigate, onLogout }) {
  const [filter, setFilter] = useState("all");
  const [selectedKey, setSelectedKey] = useState(null);
  const [tab, setTab] = useState("inventory");
  const attributes = content?.attributes || [];
  const slotName = Object.fromEntries((content?.slots || []).map((slot) => [slot.key, slot.name]));
  const build = realm?.build;
  const inventory = realm?.inventory || [];
  const ownedTalismans = (realm?.talismans || []).filter((t) => t.owned);
  const equippedTalisman = ownedTalismans.find((t) => t.equipped);
  const shown = filter === "all" ? inventory : inventory.filter((item) => item.kind === filter);
  const selected = inventory.find((item) => item.key === selectedKey) || null;

  function pickSlot(slot) {
    setFilter(slot.key);
    setSelectedKey(slot.item?.key || null);
    setTab("inventory");
  }

  return <section className="home-world build-world menu-world" aria-label="Character and inventory">
    <WorldTopbar profile={profile} onNavigate={onNavigate} onLogout={onLogout} title="Character" eyebrow="YOUR MAGE" onBack={onBack} />
    <section className="arena-content build-content">
      <div className="arena-head">
        <div><span className="eyebrow">YOUR MAGE</span><h2>Character</h2></div>
        <div className="realm-toolbar-side">
          <button className="secondary" onClick={() => onNavigate("shop")}>Shop</button>
          <button className="secondary" onClick={onBack}>Back to home</button>
        </div>
      </div>
      {notice && <div className="toast build-toast" role="status">{notice}</div>}
      {!realm && <p className="muted">Loading your gear…</p>}
      {realm && !build && <div className="message" role="alert">The game server is out of date, so your build cannot load. Stop the API, run dotnet build, then dotnet run.</div>}
      {build && <div className="build-layout">
        <div className="doll-panel">
          <div className="doll-head">
            <button type="button" className={`doll-talisman ${equippedTalisman ? "is-filled" : ""}`} onClick={() => setTab("talismans")} aria-label={`Talisman: ${equippedTalisman ? equippedTalisman.name : "none"}`}>
              {equippedTalisman ? <TalismanMedal talisman={equippedTalisman} size="sm" /> : <span className="talisman-slip size-sm is-empty" aria-hidden="true"><i>+</i></span>}
              <span><small>TALISMAN</small><b>{equippedTalisman ? equippedTalisman.name : "None"}</b></span>
            </button>
            <button type="button" className="doll-classes" onClick={() => onNavigate("characters")}>{profile.className} · Classes</button>
          </div>
          <div className="doll-stage">
            <svg className="doll-lines" viewBox="0 0 1000 760" preserveAspectRatio="none" aria-hidden="true">
              {build.slots.map((slot) => {
                const box = slotBox(slot.key);
                const point = bodyPoint(slot.key);
                if (!box || !point) return null;
                const startX = DOLL_SLOTS[slot.key].side === "left" ? box.x + DOLL_TILE.w : box.x;
                const elbowX = DOLL_SLOTS[slot.key].side === "left" ? startX + 40 : startX - 40;
                return <g key={slot.key} className={`doll-link ${slot.item ? "is-filled" : ""} ${filter === slot.key ? "is-active" : ""}`}>
                  <path d={`M${startX} ${box.cy} L${elbowX} ${box.cy} L${point[0]} ${point[1]}`} />
                  <circle cx={point[0]} cy={point[1]} r={slot.item ? 9 : 6} />
                </g>;
              })}
            </svg>
            <div className="doll-figure"><MageFigure cls={profile.className} /></div>
            {build.slots.map((slot) => {
              const box = slotBox(slot.key);
              if (!box) return null;
              const style = { left: `${box.x / 10}%`, top: `${box.y / 7.6}%`, width: `${DOLL_TILE.w / 10}%`, height: `${DOLL_TILE.h / 7.6}%` };
              return <button key={slot.key} type="button" style={style} className={`doll-slot side-${DOLL_SLOTS[slot.key].side} ${slot.item ? `is-filled rarity-${slot.item.rarity || "common"}` : "is-empty"} ${filter === slot.key ? "is-active" : ""}`} onClick={() => pickSlot(slot)} aria-label={`${slot.name}: ${slot.item ? slot.item.name : "empty"}`}>
                <span className="doll-slot-name">{slot.name}</span>
                {slot.item
                  ? <><ItemArt item={slot.item} className="doll-slot-icon" /><strong>{slot.item.name}</strong><StatChips stats={slot.item.stats} attributes={attributes} compact /></>
                  : <span className="doll-slot-empty">Empty · {DOLL_SLOTS[slot.key].label}</span>}
              </button>;
            })}
          </div>
          <div className="doll-summary">
            {attributes.map((item) => <span key={item.key} className={`doll-attr stat-${item.key}`} title={`${item.name}: ${item.effect}`}><b>{item.icon}</b>{item.name}<em>{build.active[item.key] || 0}/{item.cap}</em></span>)}
          </div>
        </div>

        <aside className="bag-panel">
          <div className="bag-tabs" role="tablist">
            <button role="tab" aria-selected={tab === "inventory"} className={tab === "inventory" ? "is-on" : ""} onClick={() => setTab("inventory")}>Inventory <small>{inventory.length}</small></button>
            <button role="tab" aria-selected={tab === "talismans"} className={tab === "talismans" ? "is-on" : ""} onClick={() => setTab("talismans")}>Talismans <small>{ownedTalismans.length}</small></button>
            <button role="tab" aria-selected={tab === "attributes"} className={tab === "attributes" ? "is-on" : ""} onClick={() => setTab("attributes")}>Attributes</button>
          </div>

          {tab === "inventory" && <>
            <div className="bag-filters">
              <button className={filter === "all" ? "is-on" : ""} onClick={() => setFilter("all")}>All</button>
              {(content?.slots || []).map((slot) => <button key={slot.key} className={filter === slot.key ? "is-on" : ""} onClick={() => setFilter(slot.key)}>{slot.name}</button>)}
            </div>
            <div className="bag-grid">
              {shown.length === 0 && <p className="muted bag-empty">Nothing for this slot yet. Find gear in the shop.</p>}
              {shown.map((item) => <button key={item.key} type="button" className={`bag-item rarity-${item.rarity || "common"} ${item.equipped ? "is-equipped" : ""} ${selectedKey === item.key ? "is-selected" : ""}`} onClick={() => setSelectedKey(item.key)} title={item.name}>
                <ItemArt item={item} className="bag-item-icon" />
                <span className="bag-item-name">{item.name}</span>
                {item.equipped && <span className="bag-item-worn">Worn</span>}
              </button>)}
            </div>
            <div className="bag-detail">
              {selected ? <>
                <div className="bag-detail-head"><ItemArt item={selected} className="bag-item-icon bag-detail-art" /><div><h3>{selected.name}</h3><span className="item-kind">{slotName[selected.kind] || selected.kind} · <b className={`rarity-label rarity-${selected.rarity || "common"}`}>{selected.rarity || "common"}</b></span></div></div>
                <StatChips stats={selected.stats} attributes={attributes} />
                {!Object.values(selected.stats || {}).some(Boolean) && <p className="muted">Cosmetic. No attribute bonus.</p>}
                <p className="muted">{selected.description}</p>
                {selected.equipped
                  ? <button className="secondary" disabled={busy} onClick={() => onAction(`/realm/inventory/${selected.key}/unequip`)}>Take off</button>
                  : <button disabled={busy} onClick={() => onAction(`/realm/inventory/${selected.key}/equip`)}>Equip</button>}
              </> : <p className="muted">Pick an item to see its stats, or click a slot on your mage.</p>}
            </div>
          </>}

          {tab === "talismans" && <div className="bag-talismans">
            {ownedTalismans.length === 0 && <div className="bag-empty-talisman"><p className="muted">You have no talismans yet. Each one bends one rule of the duel.</p><button onClick={() => onNavigate("shop")}>Visit the shop</button></div>}
            {ownedTalismans.map((t) => <TalismanCard key={t.key} talisman={t}>
              {t.equipped
                ? <button className="secondary" disabled={busy} onClick={() => onAction("/realm/talismans/unequip")}>Take off</button>
                : <button disabled={busy} onClick={() => onAction(`/realm/talismans/${t.key}/equip`)}>Equip</button>}
            </TalismanCard>)}
            {ownedTalismans.length > 0 && <p className="muted build-note">One talisman at a time. It locks in when a duel starts.</p>}
          </div>}

          {tab === "attributes" && <div className="bag-attributes">
            {attributes.map((item) => {
              const total = build.totals[item.key] || 0;
              const active = build.active[item.key] || 0;
              return <div className="attr-row" key={item.key}>
                <span className={`attr-icon stat-${item.key}`}>{item.icon}</span>
                <div className="attr-body">
                  <div className="attr-head"><b>{item.name}</b><span>{active} / {item.cap}</span></div>
                  <div className="attr-bar">{Array.from({ length: item.cap }).map((_, index) => <i key={index} className={index < active ? "on" : ""} />)}</div>
                  <small>{item.effect}{total > item.cap ? ` · ${total - item.cap} over the cap does nothing` : ""}</small>
                </div>
              </div>;
            })}
            <p className="muted build-note">Your bonuses lock in when a duel starts.</p>
          </div>}
        </aside>
      </div>}
    </section>
    <WorldBottomNav active="inventory" onHome={onBack} onNavigate={onNavigate} />
  </section>;
}

function DuelBanner({ duel, home = false, onContinue, onForfeit }) {
  const [confirming, setConfirming] = useState(false);
  if (!duel) return null;
  return <div className={`duel-banner ${home ? "on-home" : ""}`} role="region" aria-label="Duel in progress">
    <div className="duel-banner-copy">
      <span className="eyebrow">{duel.practice ? "PRACTICE DUEL IN PROGRESS" : "DUEL IN PROGRESS"}</span>
      <strong>vs {duel.opponent} · {duel.opponentClass}</strong>
      <small>Turn {duel.turn} · You {duel.myHp} HP · Them {duel.opponentHp} HP{duel.waitingForYou ? " · They are waiting for you" : ""}</small>
    </div>
    <div className="duel-banner-actions">
      {confirming
        ? <><button className="danger" onClick={() => { setConfirming(false); onForfeit(); }}>Yes, end it</button><button className="secondary" onClick={() => setConfirming(false)}>Keep it</button></>
        : <><button onClick={onContinue}>Continue match</button><button className="secondary" onClick={() => setConfirming(true)}>End match</button></>}
    </div>
    {confirming && <p className="duel-banner-warn">{duel.practice ? "This is a practice duel, so ending it changes nothing." : "Ending the match counts as a loss and gives your opponent the win."}</p>}
  </div>;
}

function HomeScreen({ profile, duel, onTutorial, onSkipTutorial, onContinue, onForfeit, notice, onLogout, onMatch, onLeague, onResult, onWheel, onNavigate }) {
  return <section className="home-world" aria-label="Rune Reign home">
    <WorldTopbar profile={profile} onNavigate={onNavigate} onLogout={onLogout} />
    <DuelBanner duel={duel} home onContinue={onContinue} onForfeit={onForfeit} />
    {!duel && profile.tutorialDone === false && <div className="duel-banner on-home tutorial-banner" role="region" aria-label="Tutorial">
      <div className="duel-banner-copy"><span className="eyebrow">NEW TO THE ARENA?</span><strong>Learn to duel</strong><small>A guided practice match against a training Shade. Finish it for 150 coins.</small></div>
      <div className="duel-banner-actions"><button onClick={onTutorial}>Start tutorial</button><button className="secondary" onClick={onSkipTutorial}>Skip</button></div>
    </div>}
    {notice && <div className="toast" role="status">{notice}</div>}
    <WorldRail onNavigate={onNavigate} />
    <div className="world-logo" aria-hidden="true"><span className="world-logo-name">{GAME_TITLE}</span><span className="world-logo-sub">{GAME_SUBTITLE}</span></div>
    <p className="world-tagline">A greater journey awaits…<br />Are you ready?</p>

    <section className="world-actions" aria-label="Choose a game mode">
      <button className="world-mode mode-match" onClick={onMatch}><span className="mode-sigil"><Ico name="swords" size={72} /></span><span><strong>Arena</strong><small>PvP duels · ranked</small></span><b><Ico name="chevron" size={30} /></b></button>
      <button className="world-mode mode-campaign" onClick={onLeague}><span className="mode-sigil"><Ico name="crown" size={52} /></span><span><strong>League Hall</strong><small>{profile.points} points · {profile.seasonMatches} matches</small></span><b><Ico name="chevron" size={26} /></b></button>
      <button className="world-mode mode-story" onClick={() => onNavigate("story")}><span className="mode-sigil"><Ico name="book" size={52} /></span><span><strong>Story Mode</strong><small>Uncover the truth</small></span><b><Ico name="chevron" size={26} /></b></button>
    </section>

    <WorldBottomNav active="home" onHome={() => {}} onNavigate={onNavigate} />
    {process.env.NODE_ENV !== "production" && <details className="dev-tools world-dev"><summary>Development test controls</summary><p className="muted">These controls are disabled by the API in production.</p><div className="actions"><button className="secondary" onClick={() => onResult(true)}>Record win</button><button className="secondary" onClick={() => onResult(false)}>Record loss</button><button className="secondary" onClick={onWheel}>Turn Wheel</button></div></details>}
  </section>;
}

/* ---------- Settings (saved to the player's account on the server) ---------- */
// Used only until the server's copy arrives.
const DEFAULT_SETTINGS = { reduceMotion: false, damageNumbers: true, diceAnimation: true, compactHud: false };

function SettingsModal({ profile, settings, defaults, onChange, onClose, onLogout }) {
  const closeRef = useRef(null);
  useEffect(() => {
    closeRef.current?.focus();
    const onKey = (event) => { if (event.key === "Escape") onClose(); };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);
  const rows = [
    ["reduceMotion", "Reduce motion", "Calms menu animations and skips dice tumbling."],
    ["diceAnimation", "Dice roll animation", "Show the dice tumbling before they land."],
    ["damageNumbers", "Floating damage numbers", "Show −/+ numbers when a spell lands."],
    ["compactHud", "Compact battle HUD", "Smaller health plates to give the arena more room."]
  ];
  return <div className="modal-scrim" onMouseDown={(event) => { if (event.target === event.currentTarget) onClose(); }}>
    <section className="settings-modal" role="dialog" aria-modal="true" aria-labelledby="settings-title">
      <header className="modal-head">
        <div><span className="eyebrow">RUNE DUEL</span><h2 id="settings-title">Settings</h2></div>
        <button ref={closeRef} className="modal-close" onClick={onClose} aria-label="Close settings">✕</button>
      </header>
      <div className="modal-body">
        <section className="settings-group">
          <h3>Account</h3>
          <dl className="settings-account">
            <div><dt>Mage name</dt><dd>{profile.username}</dd></div>
            <div><dt>Email</dt><dd>{profile.email}</dd></div>
            <div><dt>Class</dt><dd>{profile.className}</dd></div>
            <div><dt>Rank</dt><dd>{profile.leagueName} · {profile.points} / {profile.pointsPerLeague} pts</dd></div>
            <div><dt>Wallet</dt><dd>{(profile.coins ?? 0).toLocaleString()} coins · {(profile.gems ?? 0).toLocaleString()} gems</dd></div>
          </dl>
        </section>
        <section className="settings-group">
          <h3>Gameplay &amp; display</h3>
          {rows.map(([key, title, help]) => <label className="switch-row" key={key}>
            <span><strong>{title}</strong><small>{help}</small></span>
            <input type="checkbox" role="switch" checked={Boolean(settings[key])} onChange={(event) => onChange({ ...settings, [key]: event.target.checked })} />
            <i className="switch" aria-hidden="true" />
          </label>)}
        </section>
        <section className="settings-group">
          <h3>Session</h3>
          <div className="settings-actions">
            <button className="secondary" onClick={() => onChange({ ...(defaults || DEFAULT_SETTINGS) })}>Reset preferences</button>
            {profile.isAdmin && <a className="secondary admin-link" href="/admin">Open admin page</a>}
            <button onClick={onLogout}>Log out</button>
          </div>
          <p className="settings-foot">Preferences are saved to your account, so they follow you to any device.</p>
        </section>
      </div>
    </section>
  </div>;
}

/* ---------- Arena: the PvP menu, the search and the match-found popup ---------- */
function FriendChallengeActions({ challenge, busy, onCreate, onCancel, onJoin }) {
  const [code, setCode] = useState("");
  const [left, setLeft] = useState(challenge?.expiresInSeconds || 0);
  const [copied, setCopied] = useState(false);
  const [joining, setJoining] = useState(false);
  useEffect(() => {
    if (!challenge) return undefined;
    const end = Date.now() + challenge.expiresInSeconds * 1000;
    setLeft(challenge.expiresInSeconds);
    const timer = setInterval(() => setLeft(Math.max(0, Math.round((end - Date.now()) / 1000))), 1000);
    return () => clearInterval(timer);
  }, [challenge?.code, challenge?.expiresInSeconds]);
  async function copy() {
    try { await navigator.clipboard.writeText(challenge.code); setCopied(true); setTimeout(() => setCopied(false), 1500); } catch { /* clipboard blocked: the code is on screen */ }
  }
  async function join(event) {
    event.preventDefault();
    if (code.trim().length < 6) return;
    setJoining(true);
    const ok = await onJoin(code.trim());
    setJoining(false);
    if (ok) setCode("");
  }
  if (challenge) return <div className="friend-actions is-hosting">
    <span className="friend-label">Your code</span>
    <button type="button" className="friend-code" onClick={copy} title="Copy the code">{challenge.code}</button>
    <span className="friend-wait">{copied ? "Copied!" : `Waiting for ${challenge.invitee || "your friend"} · ${formatClock(left)}`}</span>
    <button type="button" className="secondary" onClick={onCancel}>Cancel code</button>
  </div>;
  return <div className="friend-actions">
    <button type="button" onClick={onCreate} disabled={busy}>Create code</button>
    <form className="friend-join" onSubmit={join}>
      <input aria-label="Friend's code" placeholder="Enter code" maxLength={6} value={code} onChange={(event) => setCode(event.target.value.toUpperCase().replace(/[^A-Z0-9]/g, ""))} disabled={busy} />
      <button type="submit" className="secondary" disabled={busy || joining || code.length < 6}>Join</button>
    </form>
  </div>;
}

function ArenaScreen({ profile, content, contentError, onRetryContent, onShade, duel, onContinue, onForfeit, notice, onBack, onFind, challenge, onCreateChallenge, onCancelChallenge, onJoinChallenge, onNavigate, onLogout }) {
  const progress = Math.min(100, profile.points / profile.pointsPerLeague * 100);
  const modes = content?.arenaModes || [];
  const protectedMatches = content?.rules?.protectedBracketMatches ?? 20;
  return <section className="home-world arena-world menu-world" aria-label="The Arena">
    <WorldTopbar profile={profile} onNavigate={onNavigate} onLogout={onLogout} title="Arena" eyebrow="CHOOSE YOUR DUEL" onBack={onBack} />
    <section className="arena-content">
      <div className="arena-head">
        <div><span className="eyebrow">THE ARENA</span><h2>Choose your duel</h2></div>
        <button className="secondary" onClick={onBack}>Back to home</button>
      </div>
      {notice && <div className="message arena-notice" role="status">{notice}</div>}
      <DuelBanner duel={duel} onContinue={onContinue} onForfeit={onForfeit} />
      {!content && <div className="message arena-notice" role="alert">{contentError || "Loading duels from the server…"} {contentError && <button className="secondary" onClick={onRetryContent}>Try again</button>}</div>}
      <div className="arena-grid">
        <div className="arena-modes">
          {modes.map((mode) => <article className={`arena-mode ${mode.live ? "is-live" : "is-locked"}`} key={mode.key}>
            <span className="arena-mode-icon"><Ico name={mode.icon} size={mode.live ? 54 : 38} /></span>
            <div className="arena-mode-copy"><h3>{mode.title}</h3><span className="arena-tag">{mode.tag}</span><p>{mode.description}</p></div>
            {!mode.live
              ? <button className="secondary" disabled>Locked</button>
              : mode.key === "shade"
                ? <div className="arena-shade-actions"><button className="secondary" onClick={() => onShade("tutorial")} disabled={Boolean(duel)} title="A guided first duel">Tutorial</button><button className="secondary" onClick={() => onShade("easy")} disabled={Boolean(duel)}>Easy</button><button onClick={() => onShade("hard")} disabled={Boolean(duel)}>Hard</button></div>
                : mode.key === "friend"
                  ? <FriendChallengeActions challenge={challenge} busy={Boolean(duel)} onCreate={onCreateChallenge} onCancel={onCancelChallenge} onJoin={onJoinChallenge} />
                  : <button className={mode.key === "casual" ? "arena-find is-casual secondary" : "arena-find"} onClick={() => onFind(mode.key === "casual" ? "casual" : "ranked")} disabled={Boolean(duel)} title={duel ? "Finish or end your current duel first" : undefined}>{mode.key === "casual" ? "Find casual" : "Find opponent"}</button>}
          </article>)}
        </div>
        <aside className="arena-standing" aria-label="Your standing">
          <span className="eyebrow">YOUR STANDING</span>
          <strong className="standing-league">{profile.leagueName}</strong>
          <span className="standing-sub">League {profile.league} · {profile.className}</span>
          <div className="standing-bar" role="progressbar" aria-valuemin={0} aria-valuemax={profile.pointsPerLeague} aria-valuenow={profile.points}><i style={{ width: `${progress}%` }} /></div>
          <span className="standing-points">{profile.points} / {profile.pointsPerLeague} points to promotion</span>
          <dl className="standing-stats">
            <div><dt>Season matches</dt><dd>{profile.seasonMatches}</dd></div>
            <div><dt>Season wins</dt><dd>{profile.seasonWins ?? "—"}</dd></div>
          </dl>
          {profile.seasonMatches < protectedMatches && <p className="standing-note">New mage protection: your first {protectedMatches} matches are matched against other new players where possible.</p>}
        </aside>
      </div>
    </section>
    <WorldBottomNav active="arena" onHome={onBack} onNavigate={onNavigate} />
  </section>;
}

function formatClock(seconds) {
  const m = Math.floor(seconds / 60);
  return `${m}:${String(seconds % 60).padStart(2, "0")}`;
}

function SearchScreen({ match, tips, onCancel, onShade, profile, onNavigate, onLogout }) {
  const [elapsed, setElapsed] = useState(0);
  const [tip, setTip] = useState(0);
  useEffect(() => {
    const started = Date.now();
    const timer = setInterval(() => setElapsed(Math.floor((Date.now() - started) / 1000)), 500);
    return () => clearInterval(timer);
  }, []);
  useEffect(() => {
    const timer = setInterval(() => setTip((value) => value + 1), 5000);
    return () => clearInterval(timer);
  }, []);
  const found = match.status === "found";
  const casual = match.mode === "casual";
  const offerShade = casual && !found && elapsed >= 20 && Boolean(onShade);
  return <section className="search-screen menu-world" aria-live="polite">
    {profile && <WorldTopbar profile={profile} onNavigate={onNavigate} onLogout={onLogout} title="Finding a duel" eyebrow={casual ? "CASUAL" : "RANKED"} onBack={onCancel} />}
    <div className="search-card">
      <span className="eyebrow">{casual ? "CASUAL DUEL · NO POINTS" : "RANKED DUEL"}</span>
      <h2>{found ? "Opponent found" : "Searching for a worthy opponent"}</h2>
      <div className="search-sigil" aria-hidden="true">
        <i className="ring ring-a" /><i className="ring ring-b" /><i className="ring ring-c" />
        {["ᚲ", "ᛁ", "ᛈ", "ᛇ"].map((glyph, index) => <b key={glyph} style={{ "--i": index }}>{glyph}</b>)}
        <span>ᚱ</span>
      </div>
      <div className="search-clock">{formatClock(elapsed)}</div>
      <p className="search-sub">{elapsed < 15 ? "Looking for mages of similar rank…" : elapsed < 45 ? "Widening the search for a fair match…" : "Still searching. Quiet realm tonight, but the search continues."}</p>
      {tips.length > 0 && !offerShade && <p className="search-tip"><b>Tip</b> {tips[tip % tips.length]}</p>}
      {offerShade && <div className="search-shade">
        <p><b>No one else is searching right now.</b> Duel the Shade, a computer opponent, instead. Practice only: no coins or points.</p>
        <div className="actions"><button onClick={() => onShade("easy")}>Fight the Shade</button><button className="secondary" onClick={() => onShade("hard")}>Hard Shade</button></div>
      </div>}
      <button className="secondary" onClick={onCancel}>Cancel search</button>
    </div>
  </section>;
}

function MatchFoundOverlay({ match, error, onAccept, onDecline }) {
  const windowSeconds = match.acceptWindowSeconds || 10;
  const deadline = useRef(Date.now() + (match.secondsLeft ?? windowSeconds) * 1000);
  const [left, setLeft] = useState(match.secondsLeft ?? windowSeconds);
  const [pending, setPending] = useState(false);
  useEffect(() => { deadline.current = Date.now() + (match.secondsLeft ?? windowSeconds) * 1000; }, [match.secondsLeft, windowSeconds]);
  useEffect(() => {
    const timer = setInterval(() => setLeft(Math.max(0, Math.ceil((deadline.current - Date.now()) / 1000))), 200);
    return () => clearInterval(timer);
  }, []);
  const accepted = Boolean(match.accepted);
  async function press() {
    if (accepted || pending || left <= 0) return;
    setPending(true);
    await onAccept();
    setPending(false);
  }
  const fraction = Math.min(1, left / windowSeconds);
  return <div className="modal-scrim found-scrim" role="alertdialog" aria-modal="true" aria-labelledby="found-title">
    <section className={`found-card ${accepted ? "is-accepted" : ""} ${left <= 2 && !accepted ? "is-urgent" : ""}`}>
      <span className="eyebrow">THE ARENA CALLS</span>
      <h2 id="found-title">Match found!</h2>
      <div className="found-timer" aria-label={`${left} seconds left`}>
        <svg viewBox="0 0 120 120" aria-hidden="true"><circle className="found-track" cx="60" cy="60" r="52" /><circle className="found-progress" cx="60" cy="60" r="52" style={{ strokeDasharray: 326.7, strokeDashoffset: 326.7 * (1 - fraction) }} /></svg>
        <strong>{accepted ? "✓" : left}</strong>
      </div>
      {accepted
        ? <p className="found-wait">You are ready. Waiting for your opponent<span className="dots" aria-hidden="true"><i /><i /><i /></span></p>
        : <button className="found-enter" onClick={press} disabled={left <= 0 || pending}>{left <= 0 ? "Too late…" : "Enter match"}</button>}
      <p className="found-sub">{accepted ? "The duel begins as soon as both mages are in." : "Both mages must enter before the timer ends."}</p>
      {error && <p className="found-error" role="alert">{error}</p>}
      <button className="secondary found-cancel" onClick={onDecline}>{accepted ? "Cancel and leave" : "Decline match"}</button>
    </section>
  </div>;
}

/* ---------- Ranks ---------- */
function RankScreen({ league, content, profile, loadHistory, loadRounds, onBack, onNavigate, onLogout }) {
  const [tab, setTab] = useState("ladder");
  const leaders = league?.leaderboard || [];
  const myIndex = leaders.findIndex((item) => item.username === profile.username);
  const now = new Date();
  const wheelEnds = Date.UTC(now.getUTCFullYear(), now.getUTCMonth() + 1, 1);
  const daysLeft = Math.max(1, Math.ceil((wheelEnds - now.getTime()) / 864e5));
  const progress = Math.min(100, profile.points / profile.pointsPerLeague * 100);
  const tiers = (league?.leagues || []).slice().sort((a, b) => a.number - b.number);
  const tierClass = (n) => (n <= 3 ? "tier-gold" : n <= 6 ? "tier-silver" : "tier-bronze");
  const countIn = (n) => leaders.filter((item) => item.league === n).length;
  return <section className="home-world rank-world menu-world" aria-label="Ranks">
    <WorldTopbar profile={profile} onNavigate={onNavigate} onLogout={onLogout} title="Ranks" eyebrow={content?.season ? `SEASON ${content.season.number} · ${content.season.title.toUpperCase()}` : "SEASON"} onBack={onBack} />
    <section className="rank-content">
      <div className="arena-head">
        <div><span className="eyebrow">{content?.season ? `SEASON ${content.season.number} · ${content.season.title.toUpperCase()}` : "SEASON"}</span><h2>Ranks</h2></div>
        <button className="secondary" onClick={onBack}>Back to home</button>
      </div>
      <div className="rank-summary">
        <div className={`rank-emblem ${tierClass(profile.league)}`}><span>{profile.league}</span></div>
        <div className="rank-summary-copy">
          <strong>{profile.leagueName}</strong>
          <span>League {profile.league} of {tiers.length || 10} · {myIndex >= 0 ? `Server rank #${myIndex + 1}` : "Unranked on the board yet"}</span>
          <div className="standing-bar"><i style={{ width: `${progress}%` }} /></div>
          <small>{profile.points} / {profile.pointsPerLeague} points · {profile.league > 1 ? `${profile.pointsPerLeague - profile.points} to ${tiers.find((t) => t.number === profile.league - 1)?.name || "promotion"}` : "You stand at the top"}</small>
        </div>
        <div className="rank-wheel"><span className="eyebrow">THE WHEEL TURNS IN</span><strong>{daysLeft}<small> day{daysLeft === 1 ? "" : "s"}</small></strong><span>{content?.rules?.wheelSummary}</span></div>
      </div>
      <div className="rank-tabs" role="tablist">
        {[["ladder", "Ladder"], ["board", "Leaderboard"], ["history", "My duels"], ["fame", "Hall of Fame"]].map(([key, label]) => <button key={key} role="tab" aria-selected={tab === key} className={tab === key ? "is-on" : ""} onClick={() => setTab(key)}>{label}</button>)}
      </div>
      {!league && <p className="muted">Loading ranks…</p>}
      {league && tab === "ladder" && <ol className="ladder">
        {tiers.map((tier) => <li key={tier.number} className={`ladder-row ${tierClass(tier.number)} ${tier.number === profile.league ? "is-me" : ""}`}>
          <span className="ladder-num">{tier.number}</span>
          <strong>{tier.name}</strong>
          <span className="ladder-count">{countIn(tier.number)} mage{countIn(tier.number) === 1 ? "" : "s"}</span>
          {tier.number === profile.league && <span className="ladder-you">YOU ARE HERE</span>}
        </li>)}
      </ol>}
      {league && tab === "board" && <div className="leaderboard">
        {leaders.length === 0 && <p className="muted">No ranked mages yet.</p>}
        {leaders.map((item, index) => <div className={`leader-row ${index < 3 ? "top-rank" : ""} ${item.username === profile.username ? "is-me" : ""}`} key={item.username}><span className={`rank-number rank-${index + 1}`}>{index + 1}</span><span className="leader-avatar">{item.username.slice(0, 1).toUpperCase()}</span><div className="leader-player"><strong>{item.username}</strong><span>{item.className || "Unbound mage"}</span></div><div className="leader-league"><strong>{item.leagueName}</strong><span>League {item.league}</span></div><strong className="leader-points">{item.points}<small> pts</small></strong></div>)}
      </div>}
      {tab === "history" && <HistoryList loadHistory={loadHistory} loadRounds={loadRounds} />}
      {league && tab === "fame" && <div className="hall-card"><div className="hall-icon">✦</div><div><div className="eyebrow">HALL OF FAME</div><h3>Past champions</h3>{league.hallOfFame.length ? <ul>{league.hallOfFame.map((item) => <li key={`${item.seasonName}-${item.username}`}><strong>{item.username}</strong><span>{item.seasonName}</span></li>)}</ul> : <p className="muted">The first crown is waiting. Finish a season at #1 with at least {content?.rules?.minimumChampionMatches ?? 30} matches to claim it.</p>}</div></div>}
    </section>
    <WorldBottomNav active="" onHome={onBack} onNavigate={onNavigate} />
  </section>;
}

/* ---------- Mage figures ---------- */
// Uses the illustrated character at /characters/<class>.png when that file exists,
// and falls back to the drawn mage below when it does not.
// The name shown as the logo on the home screen.
const GAME_TITLE = "Rune Duel";
const GAME_SUBTITLE = "The Arcane Arena";

const missingArt = new Set();
// Item pictures that failed to load, so the symbol is shown without asking the server again.
const missingItemArt = new Set();

// The item's picture, or its symbol until a picture exists at item.image (default /items/<key>.png).
function ItemArt({ item, className }) {
  const src = item?.image || (item?.key ? `/items/${item.key}.png` : "");
  const [failed, setFailed] = useState(!src || missingItemArt.has(src));
  useEffect(() => { setFailed(!src || missingItemArt.has(src)); }, [src]);
  return <span className={`${className} ${failed ? "" : "has-art"}`}>
    {failed ? item?.icon : <img src={src} alt="" draggable={false} loading="lazy" onError={() => { missingItemArt.add(src); setFailed(true); }} />}
  </span>;
}
function MageFigure({ cls, flip, pose, art: forced }) {
  const key = (cls || "Flamecaller").toLowerCase();
  // Class art first, then the shared hero art, then the drawn mage.
  // A forced art name (the Shade) is tried before the class art.
  const pick = () => [forced, key, "hero"].filter(Boolean).find((name) => !missingArt.has(name)) || null;
  const [art, setArt] = useState(pick);
  useEffect(() => { setArt(pick()); }, [key, forced]);
  if (art) {
    return <div className={`mage mage-${key} has-art art-${art} ${flip ? "is-flipped" : ""}`} data-pose={pose || "idle"} aria-hidden="true">
      <i className="mage-shadow" />
      <img className="mage-svg mage-art" src={`/characters/${art}.png`} alt="" draggable={false} onError={() => { missingArt.add(art); setArt(pick()); }} />
    </div>;
  }
  return <div className={`mage mage-${key} ${flip ? "is-flipped" : ""}`} data-pose={pose || "idle"} aria-hidden="true">
    <i className="mage-shadow" />
    <svg className="mage-svg" viewBox="0 0 200 280">
      <defs>
        <linearGradient id={`mg-${key}`} x1="0" y1="0" x2="0" y2="1"><stop offset="0" style={{ stopColor: "var(--m2)" }} /><stop offset="1" style={{ stopColor: "var(--m1)" }} /></linearGradient>
        <radialGradient id={`mo-${key}`}><stop offset="0" stopColor="#fff" stopOpacity=".95" /><stop offset=".4" style={{ stopColor: "var(--orb)" }} /><stop offset="1" style={{ stopColor: "var(--orb)" }} stopOpacity="0" /></radialGradient>
      </defs>
      <g className="mage-figure">
        <path className="m-line" d="M64 112 C50 168 36 220 28 258 L164 258 C156 220 142 168 128 112 Z" fill={`url(#mg-${key})`} />
        <path className="m-front" d="M80 122 C74 172 68 216 64 258 L132 258 C128 216 122 172 116 122 Z" />
        <path className="m-trim-line" d="M30 252 L162 252" />
        {key === "gravebound" && <path className="m-line" d="M28 258 L40 242 L52 258 L66 240 L80 258 L96 243 L110 258 L126 241 L140 258 L152 244 L164 258 Z" style={{ fill: "var(--m1)" }} />}
        <path className="m-line m-mantle" d="M60 110 C72 94 124 94 136 110 C142 130 126 142 98 142 C70 142 54 130 60 110 Z" />
        {key === "frostwarden" && <><ellipse className="m-pauldron" cx="64" cy="112" rx="17" ry="11" /><ellipse className="m-pauldron" cx="132" cy="112" rx="17" ry="11" /></>}
        <path className="m-trim-line" d="M72 178 Q98 190 124 178" />
        <path className="m-emblem" d="M98 176 L106 186 L98 196 L90 186 Z" />
        <path className="m-arm" d="M68 126 C54 146 50 168 58 188" />
        <path className="m-line m-hood" d="M98 38 C70 38 60 68 64 98 C68 124 84 136 98 136 C112 136 128 124 132 98 C136 68 126 38 98 38 Z" />
        <path className="m-hood-rim" d="M76 62 C84 48 112 48 120 62" />
        <ellipse className="m-face" cx="100" cy="88" rx="19" ry="23" />
        <ellipse className="m-eye" cx="94" cy="86" rx="3.4" ry="2.4" /><ellipse className="m-eye" cx="108" cy="86" rx="3.4" ry="2.4" />
      </g>
      <g className="mage-staff">
        <path className="m-arm" d="M120 128 C138 130 148 146 148 170" />
        <path className="m-staff" d="M152 70 L146 262" />
        <circle className="m-hand" cx="148" cy="172" r="8" />
        {key === "flamecaller" && <path className="m-flame" d="M152 24 C140 38 142 50 152 56 C162 50 164 38 152 24 Z" />}
        {key === "frostwarden" && <path className="m-crystal" d="M152 26 L164 50 L152 74 L140 50 Z M140 50 L164 50" />}
        {key === "fateweaver" && <g className="m-ring"><ellipse cx="152" cy="52" rx="26" ry="9" /><circle cx="126" cy="52" r="3" /><circle cx="178" cy="52" r="3" /></g>}
        {key === "gravebound" && <g className="m-skull"><path d="M152 30 C142 30 138 40 140 48 C141 52 144 54 144 58 L160 58 C160 54 163 52 164 48 C166 40 162 30 152 30 Z" /><circle cx="147" cy="45" r="3" /><circle cx="157" cy="45" r="3" /></g>}
        <circle className="m-orb" cx="152" cy="54" r="24" fill={`url(#mo-${key})`} />
        {key !== "frostwarden" && key !== "gravebound" && <circle className="m-orb-core" cx="152" cy="54" r="7" />}
      </g>
    </svg>
  </div>;
}

/* ---------- Combat: 3D dice, the three-circle ring and the duel screen ----------
   The server still decides every roll. These pieces only animate and display what it returns. */


const vSub = (a, b) => [a[0] - b[0], a[1] - b[1], a[2] - b[2]];
const vDot = (a, b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
const vScale = (a, s) => [a[0] * s, a[1] * s, a[2] * s];
const vCross = (a, b) => [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]];
const vNorm = (a) => { const l = Math.hypot(a[0], a[1], a[2]) || 1; return [a[0] / l, a[1] / l, a[2] / l]; };

const qMul = (a, b) => [
  a[3] * b[0] + a[0] * b[3] + a[1] * b[2] - a[2] * b[1],
  a[3] * b[1] - a[0] * b[2] + a[1] * b[3] + a[2] * b[0],
  a[3] * b[2] + a[0] * b[1] - a[1] * b[0] + a[2] * b[3],
  a[3] * b[3] - a[0] * b[0] - a[1] * b[1] - a[2] * b[2]
];
const qNorm = (q) => { const l = Math.hypot(q[0], q[1], q[2], q[3]) || 1; return [q[0] / l, q[1] / l, q[2] / l, q[3] / l]; };
const qAxis = (axis, angle) => { const s = Math.sin(angle / 2); return [axis[0] * s, axis[1] * s, axis[2] * s, Math.cos(angle / 2)]; };
const qConj = (q) => [-q[0], -q[1], -q[2], q[3]];
const qToMat = ([x, y, z, w]) => [
  [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]
];
function qFromMat(m) {
  const t = m[0][0] + m[1][1] + m[2][2];
  let x, y, z, w;
  if (t > 0) { const s = Math.sqrt(t + 1) * 2; w = s / 4; x = (m[2][1] - m[1][2]) / s; y = (m[0][2] - m[2][0]) / s; z = (m[1][0] - m[0][1]) / s; }
  else if (m[0][0] > m[1][1] && m[0][0] > m[2][2]) { const s = Math.sqrt(1 + m[0][0] - m[1][1] - m[2][2]) * 2; w = (m[2][1] - m[1][2]) / s; x = s / 4; y = (m[0][1] + m[1][0]) / s; z = (m[0][2] + m[2][0]) / s; }
  else if (m[1][1] > m[2][2]) { const s = Math.sqrt(1 + m[1][1] - m[0][0] - m[2][2]) * 2; w = (m[0][2] - m[2][0]) / s; x = (m[0][1] + m[1][0]) / s; y = s / 4; z = (m[1][2] + m[2][1]) / s; }
  else { const s = Math.sqrt(1 + m[2][2] - m[0][0] - m[1][1]) * 2; w = (m[1][0] - m[0][1]) / s; x = (m[0][2] + m[2][0]) / s; y = (m[1][2] + m[2][1]) / s; z = s / 4; }
  return qNorm([x, y, z, w]);
}

/* A slight fixed camera tilt so the landed face is readable but the die still looks solid. */
const DIE_TILT = qNorm(qMul(qAxis([1, 0, 0], -0.34), qAxis([0, 1, 0], 0.46)));
const DIE_REST = qNorm(qMul(qAxis([1, 0, 0], -0.62), qAxis([0, 1, 0], 0.72)));
const LIGHT = vNorm([-0.35, -0.55, 0.76]);

function buildDieFaces(kind, size) {
  const raw = kind === "d6"
    ? [[0, 0, 1], [0, 0, -1], [1, 0, 0], [-1, 0, 0], [0, -1, 0], [0, 1, 0]]
    : [[1, 1, 1], [1, 1, -1], [-1, 1, -1], [-1, 1, 1], [1, -1, 1], [1, -1, -1], [-1, -1, -1], [-1, -1, 1]].map(vNorm);
  const dist = kind === "d6" ? size / 2 : size / Math.sqrt(6);
  return raw.map((n) => {
    // The triangle's apex must point at the octahedron's vertex on the y axis, otherwise the faces do not meet.
    const ref = kind === "d8" ? [0, n[1] > 0 ? 1 : -1, 0] : Math.abs(n[1]) > 0.99 ? [0, 0, -1] : [0, -1, 0];
    const tangentUp = vNorm(vSub(ref, vScale(n, vDot(ref, n))));
    const v = vScale(tangentUp, -1);
    const u = vCross(v, n);
    const c = vScale(n, dist);
    const css = `matrix3d(${u[0]},${u[1]},${u[2]},0,${v[0]},${v[1]},${v[2]},0,${n[0]},${n[1]},${n[2]},0,${c[0]},${c[1]},${c[2]},1)`;
    return { n, u, v, css, target: qNorm(qMul(DIE_TILT, qFromMat([u, v, n]))) };
  });
}

/* kind: "d6" | "d8"; mode: "rest" | "rolling" | "landing"; face: index of the face the server rolled (or -1) */
function Die3D({ kind, labels, face, mode, size = 84, tone, instant = false }) {
  const faces = useMemo(() => buildDieFaces(kind, size), [kind, size]);
  const dieRef = useRef(null);
  const shadowRef = useRef(null);
  const faceRefs = useRef([]);
  const engine = useRef({ q: DIE_REST, mode: "rest", raf: 0, axis: [0, 1, 0], speed: 0, t0: 0, from: DIE_REST, axisS: [0, 1, 0], angle: 0, dur: 1200, y: 0 });
  const triH = size * 0.866;

  const paint = useCallback(() => {
    const s = engine.current;
    const el = dieRef.current;
    if (!el) return;
    const m = qToMat(s.q);
    el.style.transform = `translate3d(0,${s.y.toFixed(2)}px,0) matrix3d(${m[0][0]},${m[1][0]},${m[2][0]},0,${m[0][1]},${m[1][1]},${m[2][1]},0,${m[0][2]},${m[1][2]},${m[2][2]},0,0,0,0,1)`;
    faces.forEach((f, i) => {
      const node = faceRefs.current[i];
      if (!node) return;
      const nx = m[0][0] * f.n[0] + m[0][1] * f.n[1] + m[0][2] * f.n[2];
      const ny = m[1][0] * f.n[0] + m[1][1] * f.n[1] + m[1][2] * f.n[2];
      const nz = m[2][0] * f.n[0] + m[2][1] * f.n[1] + m[2][2] * f.n[2];
      const b = Math.max(0, Math.min(1, 0.5 + 0.5 * vDot([nx, ny, nz], LIGHT)));
      node.style.setProperty("--shade", ((1 - b) * 0.62).toFixed(3));
    });
    if (shadowRef.current) {
      const lift = Math.min(1, Math.abs(s.y) / (size * 0.6));
      shadowRef.current.style.transform = `translateX(-50%) scale(${(1 - lift * 0.45).toFixed(3)})`;
      shadowRef.current.style.opacity = (0.55 - lift * 0.3).toFixed(3);
    }
  }, [faces, size]);

  const loop = useCallback((now) => {
    const s = engine.current;
    const last = s.last || now;
    const dt = Math.min(0.05, (now - last) / 1000);
    s.last = now;
    const t = (now - s.t0) / 1000;
    if (s.mode === "tumble") {
      s.q = qNorm(qMul(qAxis(s.axis, s.speed * dt), s.q));
      s.speed += (9 - s.speed) * dt * 1.2;
      s.axis = vNorm([s.axis[0] + (Math.random() - 0.5) * dt * 6, s.axis[1] + (Math.random() - 0.5) * dt * 6, s.axis[2] + (Math.random() - 0.5) * dt * 6]);
      s.y = -Math.abs(Math.sin(t * 6.5)) * size * 0.42;
    } else if (s.mode === "settle") {
      const p = Math.min(1, (now - s.t0) / s.dur);
      const e = 1 - Math.pow(1 - p, 3.4);
      s.q = qNorm(qMul(qAxis(s.axisS, s.angle * e), s.from));
      s.y = -Math.abs(Math.sin(p * Math.PI * 2.6)) * (1 - p) * size * 0.5;
      if (p >= 1) { s.mode = "rest"; s.y = 0; s.q = s.to; }
    }
    paint();
    if (s.mode !== "rest") s.raf = requestAnimationFrame(loop);
    else { s.raf = 0; s.last = 0; }
  }, [paint, size]);

  const kick = useCallback(() => {
    const s = engine.current;
    if (!s.raf) { s.last = 0; s.raf = requestAnimationFrame(loop); }
  }, [loop]);

  useEffect(() => {
    const s = engine.current;
    const reduce = instant || (typeof window !== "undefined" && window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches);
    const target = face >= 0 && faces[face] ? faces[face].target : null;
    if (mode === "rolling") {
      if (reduce) return;
      if (s.mode !== "tumble") {
        s.mode = "tumble";
        s.t0 = performance.now();
        s.speed = 15 + Math.random() * 4;
        s.axis = vNorm([Math.random() - 0.5, Math.random() - 0.5, Math.random() - 0.5]);
        kick();
      }
    } else if (mode === "landing" && target) {
      s.to = target;
      if (reduce) { s.q = target; s.y = 0; s.mode = "rest"; paint(); return; }
      const rel = qNorm(qMul(target, qConj(s.q)));
      const theta = 2 * Math.acos(Math.max(-1, Math.min(1, rel[3])));
      const sin = Math.sqrt(Math.max(0, 1 - rel[3] * rel[3]));
      s.axisS = sin < 1e-4 ? [0, 1, 0] : [rel[0] / sin, rel[1] / sin, rel[2] / sin];
      s.angle = theta + Math.PI * 2;
      s.from = s.q;
      s.dur = 1050 + Math.random() * 200;
      s.t0 = performance.now();
      s.mode = "settle";
      kick();
    } else if (mode === "rest" && s.mode === "rest") {
      s.q = target || s.q;
      paint();
    }
  }, [mode, face, faces, kick, paint, instant]);

  useEffect(() => {
    const s = engine.current;
    if (s.mode === "rest") { s.q = face >= 0 && faces[face] ? faces[face].target : DIE_REST; paint(); }
    return () => { if (s.raf) cancelAnimationFrame(s.raf); s.raf = 0; s.mode = "rest"; };
  }, [faces]);

  return <div className={`die3d die3d-${kind} ${tone || ""}`} style={{ width: size * 1.7, height: size * 1.7 }} role="img" aria-label={`${kind.toUpperCase()} ${face >= 0 ? labels[face] : "ready"}`}>
    <i className="die3d-shadow" ref={shadowRef} style={{ width: size * 1.1 }} />
    <div className="die3d-body" ref={dieRef}>
      {faces.map((f, i) => kind === "d6"
        ? <div key={i} ref={(node) => { faceRefs.current[i] = node; }} className="die3d-face d6-face3d" style={{ width: size, height: size, left: -size / 2, top: -size / 2, transform: f.css, fontSize: size * 0.56 }}><span>{labels[i]}</span></div>
        : <div key={i} ref={(node) => { faceRefs.current[i] = node; }} className="die3d-face d8-face3d" style={{ width: size, height: triH, left: -size / 2, top: -triH * 2 / 3, transformOrigin: `${size / 2}px ${triH * 2 / 3}px`, transform: f.css, fontSize: size * 0.135 }}><span>{labels[i]}</span></div>)}
    </div>
  </div>;
}

/* ---------- the ring of three circles ---------- */
// Icons are artwork, so they stay in the client. What each state beats comes from the server.
const STATE_ICONS = {
  Break: "M13 2L4 14h6l-1 8 9-12h-6z",
  Flow: "M2 12c3-6 6-6 10 0s7 6 10 0 M2 17c3-5 6-5 10 0s7 5 10 0",
  Bind: "M9 6a5 5 0 1 0 0 10 5 5 0 0 0 0-10z M15 8a5 5 0 1 1 0 10 5 5 0 0 1 0-10z"
};
const RING_ORDER = ["Break", "Flow", "Bind"];
// Break beats Flow, Flow beats Bind, Bind beats Break.
const beats = (a, b) => (a === "Break" && b === "Flow") || (a === "Flow" && b === "Bind") || (a === "Bind" && b === "Break");
const ringArrows = [-30, 90, 210].map((deg) => {
  const r = deg * Math.PI / 180;
  return { x: 50 + 30 * Math.cos(r), y: 50 + 30 * Math.sin(r), rot: deg + 90 };
});

function StateRing({ states, info = [], value, locked, onPick, onReopen, ready, bonus, pending }) {
  const merged = Boolean(value);
  return <div className={`state-ring ${merged ? "is-merged" : "is-open"} ${ready ? "is-ready" : ""}`} data-mode={merged ? "merged" : "open"}>
    <svg className="ring-links" viewBox="0 0 100 100" aria-hidden="true">
      <circle className="ring-track" cx="50" cy="50" r="30" />
      {ringArrows.map((a, i) => <path key={i} className="ring-arrow" d="M-2.2 -2.6L2.8 0L-2.2 2.6Z" transform={`translate(${a.x} ${a.y}) rotate(${a.rot})`} />)}
    </svg>
    <span className="ring-seal" aria-hidden="true" />
    {pending && <svg className="ring-timer" viewBox="0 0 100 100" aria-hidden="true"><circle cx="50" cy="50" r="46" style={{ animationDuration: `${LOCK_DELAY_MS}ms` }} /></svg>}
    {RING_ORDER.filter((name) => states.includes(name)).map((name) => {
      const rule = info.find((item) => item.name === name);
      const meta = { hint: rule ? `beats ${rule.beats}` : "", lose: rule ? `loses to ${rule.losesTo}` : "", icon: STATE_ICONS[name] };
      const chosen = value === name;
      return <button type="button" key={name} data-state={name} data-chosen={chosen ? "true" : "false"} className="ring-node"
        aria-pressed={chosen} disabled={locked && !chosen}
        aria-label={`${name}: ${meta.hint}, ${meta.lose}${bonus === name ? ". Your form's state, +2 if you win the clash" : ""}${chosen && !locked ? ". Press again to take it back" : ""}`}
        onClick={() => (merged ? (!locked && chosen ? onReopen() : undefined) : onPick(name))}>
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true"><path d={meta.icon} /></svg>
        <b>{name}</b>
        {bonus === name && !locked && <em className="ring-bonus" aria-hidden="true">+2</em>}
      </button>;
    })}
  </div>;
}

function normalizeRoll(roll) {
  if (!roll) return null;
  return { rune: roll.rune ?? roll.Rune ?? "", symbol: roll.symbol ?? roll.Symbol ?? "", form: roll.form ?? roll.Form ?? "", state: roll.state ?? roll.State ?? "", picked: Boolean(roll.picked ?? roll.Picked) };
}
const rollKey = (combat) => {
  const a = normalizeRoll(combat.lastRoll);
  const b = normalizeRoll(combat.opponentRoll);
  return `${combat.turn}|${a?.symbol || ""}${a?.form || ""}|${b?.symbol || ""}${b?.form || ""}`;
};
const faceOf = (list, value) => {
  if (!value) return -1;
  const wanted = String(value).toLowerCase();
  return list.findIndex((item) => String(item).toLowerCase() === wanted);
};

function useDieSize() {
  const [size, setSize] = useState(84);
  useEffect(() => {
    const update = () => {
      const w = window.innerWidth;
      const h = window.innerHeight;
      setSize(w <= 760 ? 50 : h <= 760 ? 72 : w >= 1500 && h >= 880 ? 104 : 84);
    };
    update();
    window.addEventListener("resize", update);
    return () => window.removeEventListener("resize", update);
  }, []);
  return size;
}

const MAX_HP = 30;
const MAX_SHIELD = 8;
function ResourceHud({ side, name, hp, shield, mana, manaCap = 5 }) {
  const hpPct = Math.max(0, Math.min(100, hp / MAX_HP * 100));
  return <div className={`combat-health-hud hud-plate ${side}-hud ${hp <= MAX_HP / 3 ? "is-low" : ""}`}>
    <strong className="hud-name">{name}</strong>
    <div className="hp-bar" role="meter" aria-label="Health" aria-valuemin={0} aria-valuemax={MAX_HP} aria-valuenow={hp}>
      <i className="hp-fill" style={{ width: `${hpPct}%` }} />
      <span className="hp-text">{hp} <small>/ {MAX_HP}</small></span>
    </div>
    <div className="hud-pools">
      <div className="mana-pool" role="meter" aria-label="Mana" aria-valuemin={0} aria-valuemax={manaCap} aria-valuenow={mana}>
        <span className="pool-label">Mana</span>
        {Array.from({ length: manaCap }, (_, i) => <i key={i} className={`mana-gem ${i < mana ? "is-full" : ""}`} />)}
        <b className="pool-count">{mana}/{manaCap}</b>
      </div>
      <div className={`shield-badge ${shield > 0 ? "is-up" : ""}`} role="meter" aria-label="Shield" aria-valuemin={0} aria-valuemax={MAX_SHIELD} aria-valuenow={shield} title={`Shield ${shield} of ${MAX_SHIELD}: blocks damage before health`}>
        <svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 2l8 3v6c0 5-3.4 9.4-8 11-4.6-1.6-8-6-8-11V5z" /></svg>
        <b>{shield}</b>
        <span className="pool-label">Shield</span>
      </div>
    </div>
  </div>;
}

/* ---------- Results, history and the tutorial ---------- */
const signed = (n) => (n > 0 ? `+${n}` : `${n}`);

function RoundsList({ rounds }) {
  if (!rounds) return <p className="muted">Loading rounds…</p>;
  if (!rounds.length) return <p className="muted">No rounds were recorded for this duel.</p>;
  const side = (s) => <span className="round-side"><b className="round-rune">{s.symbol}</b>{s.rune} · {s.form} <em className={`state-tag type-${s.state.toLowerCase()}`}>{s.state}</em>{s.committed && <i className="round-commit" title="Form matched state">★</i>}</span>;
  return <ol className="rounds-list">
    {rounds.map((round) => <li key={round.turn} className={`round-row clash-${round.clash}`}>
      <span className="round-turn">T{round.turn}</span>
      {side(round.me)}
      <span className="round-clash">{round.clash === "won" ? "Won clash" : round.clash === "lost" ? "Lost clash" : "Even"}</span>
      {side(round.them)}
      <span className="round-hp">{round.myHp} – {round.opponentHp}</span>
    </li>)}
  </ol>;
}

function ResultSummary({ combat, onLoadRounds }) {
  const [rounds, setRounds] = useState(null);
  const [open, setOpen] = useState(false);
  const result = combat.result;
  async function toggle() {
    const next = !open;
    setOpen(next);
    if (next && !rounds) {
      const detail = await onLoadRounds?.(combat.matchId);
      setRounds(detail?.rounds || []);
    }
  }
  return <div className="result-summary">
    {result && !combat.practice && <dl className="result-rows">
      {(!combat.mode || combat.mode === "ranked") && <>
        <div><dt>League points</dt><dd className={result.pointsDelta >= 0 ? "is-up" : "is-down"}>{signed(result.pointsDelta)}</dd></div>
        <div><dt>Skill rating</dt><dd className={result.ratingDelta >= 0 ? "is-up" : "is-down"}>{signed(result.ratingDelta)}</dd></div>
      </>}
      {combat.mode !== "friend" && <div><dt>Coins</dt><dd className="is-up">+{result.coins}</dd></div>}
      <div><dt>Experience</dt><dd className="is-xp">+{result.xp ?? 0} XP</dd></div>
      {(result.dust > 0 || result.shards > 0) && <div><dt>Forge materials</dt><dd className="is-forge">{[result.dust ? `+${result.dust} Rune Dust` : null, result.shards ? `+${result.shards} Ember Shard${result.shards > 1 ? "s" : ""}` : null].filter(Boolean).join(" · ")}</dd></div>}
      <div><dt>Turns</dt><dd>{result.turns}</dd></div>
    </dl>}
    {result && combat.practice && result.xp > 0 && <dl className="result-rows"><div><dt>Experience</dt><dd className="is-xp">+{result.xp} XP</dd></div>{result.dust > 0 && <div><dt>Forge materials</dt><dd className="is-forge">+{result.dust} Rune Dust</dd></div>}</dl>}
    {result?.levelUp && <p className="result-levelup"><b>Level {result.levelUp.level}!</b> +{result.levelUp.coins} coins{result.levelUp.gems ? ` · +${result.levelUp.gems} gems` : ""}</p>}
    {result?.leagueChange > 0 && <p className="result-promo">Promoted! You climbed {result.leagueChange} league{result.leagueChange > 1 ? "s" : ""}.</p>}
    {result?.leagueChange < 0 && <p className="result-demo">You dropped a league. Win your way back.</p>}
    <button type="button" className="secondary result-rounds-toggle" onClick={toggle} aria-expanded={open}>{open ? "Hide rounds" : "Round by round"}</button>
    {open && <RoundsList rounds={rounds} />}
  </div>;
}

function HistoryList({ loadHistory, loadRounds }) {
  const [items, setItems] = useState(null);
  const [openId, setOpenId] = useState(null);
  const [rounds, setRounds] = useState({});
  useEffect(() => { loadHistory().then((list) => setItems(list || [])).catch(() => setItems([])); }, []);
  async function toggle(id) {
    setOpenId(openId === id ? null : id);
    if (!rounds[id]) {
      const detail = await loadRounds(id);
      setRounds((current) => ({ ...current, [id]: detail?.rounds || [] }));
    }
  }
  if (!items) return <p className="muted">Loading your duels…</p>;
  if (!items.length) return <p className="muted">No finished duels yet. Your matches will show up here.</p>;
  return <div className="history-list">
    {items.map((item) => <article key={item.matchId} className={`history-row is-${item.result}`}>
      <button type="button" className="history-head" onClick={() => toggle(item.matchId)} aria-expanded={openId === item.matchId}>
        <span className={`history-result is-${item.result}`}>{item.result === "win" ? "Victory" : item.result === "loss" ? "Defeat" : "Draw"}</span>
        <span className="history-vs"><strong>vs {item.opponent}</strong><small>{item.myClass} vs {item.opponentClass}{item.practice ? " · practice" : item.mode && item.mode !== "ranked" ? ` · ${item.mode}` : ""} · {item.turns} turns</small></span>
        {item.practice || item.mode === "friend" ? <span className="history-delta muted">{item.xp ? <b className="is-xp">+{item.xp} XP</b> : null}<small>No points</small></span> : item.mode === "casual" ? <span className="history-delta"><b className="is-up">+{item.coins} coins</b><small>Casual · no points · +{item.xp ?? 0} XP</small></span> : <span className="history-delta"><b className={item.pointsDelta >= 0 ? "is-up" : "is-down"}>{signed(item.pointsDelta)} pts</b><small>{signed(item.ratingDelta)} rating · +{item.coins} coins · +{item.xp ?? 0} XP</small></span>}
        <time className="history-time">{new Date(String(item.completedAtUtc).endsWith("Z") ? item.completedAtUtc : `${item.completedAtUtc}Z`).toLocaleDateString()}</time>
      </button>
      {openId === item.matchId && <RoundsList rounds={rounds[item.matchId]} />}
    </article>)}
  </div>;
}

const TUTORIAL_STEPS = [
  { target: null, title: "Your first duel", text: "Every turn: read the dice, shape them if you want, then secretly declare Break, Flow or Bind. Let's walk through one turn against a training Shade." },
  { target: ".rune-card.mine", title: "Your rune", text: "The rune die (D6) sets how strong your spell is. The form die (D8) sets what it does. The coloured tag shows which state your form wants." },
  { target: ".rune-card.theirs", title: "The Shade's rune", text: "You can see your opponent's rune too. Their tag hints at what they want to declare, so use it to guess their move." },
  { target: ".shape-tray", title: "Shape your dice", text: "Spend mana to reroll your rune die or pick the face of your form die. You gain 1 mana every turn, so it is fine to save it for a better moment." },
  { target: ".state-ring", title: "Declare a state", text: "Break beats Flow, Flow beats Bind, Bind beats Break. The +2 badge marks the state your form wants. Tap a state on the ring to declare it; tap it again within a moment to take it back.", waitFor: "submit" },
  { target: ".ring-slot", title: "The round resolves", text: "After each round the ring shows who won the clash and how much damage landed. Win the clash while declaring your form's state and you get +2. Now finish the Shade!", waitFor: "round", action: "Let's go" }
];

function Coach({ step, total, target, title, text, action, waiting, onNext, onSkip }) {
  const [rect, setRect] = useState(null);
  const [cardHeight, setCardHeight] = useState(220);
  const cardRef = useRef(null);
  useEffect(() => {
    const update = () => {
      if (cardRef.current) setCardHeight(cardRef.current.offsetHeight || 220);
      const el = target ? document.querySelector(target) : null;
      const r = el?.getBoundingClientRect();
      setRect(r && r.width > 0 ? { left: r.left, top: r.top, width: r.width, height: r.height } : null);
    };
    update();
    const id = setInterval(update, 250);
    window.addEventListener("resize", update);
    return () => { clearInterval(id); window.removeEventListener("resize", update); };
  }, [target]);
  const pad = 8;
  const hole = rect && { left: rect.left - pad, top: rect.top - pad, width: rect.width + pad * 2, height: rect.height + pad * 2 };
  const vh = typeof window !== "undefined" ? window.innerHeight : 800;
  const vw = typeof window !== "undefined" ? window.innerWidth : 1200;
  const cardWidth = Math.min(360, vw - 24);
  let cardStyle = { left: "50%", top: "50%", transform: "translate(-50%, -50%)", width: cardWidth };
  if (hole) {
    const below = hole.top + hole.height + 12;
    const left = Math.min(Math.max(12, hole.left + hole.width / 2 - cardWidth / 2), vw - cardWidth - 12);
    cardStyle = below + cardHeight + 12 < vh ? { left, top: below, width: cardWidth } : { left, top: Math.max(12, hole.top - 12 - cardHeight), width: cardWidth };
  }
  return <div className="coach" role="dialog" aria-live="polite" aria-label="Tutorial">
    {hole ? <div className="coach-hole" style={hole} /> : <div className="coach-dim" />}
    <div className="coach-card" ref={cardRef} style={cardStyle}>
      <span className="eyebrow">TUTORIAL · {step} / {total}</span>
      <h3>{title}</h3>
      <p>{text}</p>
      <div className="coach-actions">
        {!waiting && <button type="button" onClick={onNext}>{action || "Next"}</button>}
        {waiting && <span className="coach-waiting">{waiting}</span>}
        <button type="button" className="secondary" onClick={onSkip}>Skip tutorial</button>
      </div>
    </div>
  </div>;
}

function diceView(dice) {
  if (!dice) return null;
  return { rune: dice.rune || "", symbol: dice.symbol || "", form: dice.form || "", formState: dice.formState || "", rerolled: Boolean(dice.rerolled), picked: Boolean(dice.picked), shifted: Boolean(dice.shifted) };
}

// Roll first, then decide: both players' dice are rolled in the open at the start of every turn.
// You may shape your own dice with mana, then you secretly declare Break, Flow or Bind.
// How long a tapped state waits before it is sent, so a mis-tap can be taken back.
const LOCK_DELAY_MS = 1800;

// A talisman drawn as a curled paper slip: a brushed-ink rune down the middle and a red seal at the foot.
// Each talisman has its own rune; the shape is the same for all.
const TALISMAN_RUNES = { "lucky-knot": "ᛃ", "mirror-shard": "ᛞ", "ember-coal": "ᚲ", "iron-seal": "ᛉ", "gamblers-coin": "ᛈ", "patient-stone": "ᛁ", "second-wind": "ᛒ", "twin-mark": "ᛗ" };
function TalismanMedal({ talisman, size = "md" }) {
  const rune = TALISMAN_RUNES[talisman?.key] || talisman?.icon || "ᚱ";
  const id = `slip-${talisman?.key || "x"}-${size}`;
  return <span className={`talisman-slip size-${size} ${talisman ? "" : "is-empty"}`} aria-hidden="true">
    <svg viewBox="0 0 64 112">
      <defs>
        <linearGradient id={`${id}-paper`} x1="0" y1="0" x2="1" y2="1">
          <stop offset="0" stopColor="#fbf0d6" /><stop offset=".55" stopColor="#f1dcb2" /><stop offset="1" stopColor="#dcbb85" />
        </linearGradient>
        <linearGradient id={`${id}-shade`} x1="0" y1="0" x2="1" y2="0">
          <stop offset="0" stopColor="#8a5a2a" stopOpacity=".28" /><stop offset=".35" stopColor="#8a5a2a" stopOpacity="0" /><stop offset=".8" stopColor="#8a5a2a" stopOpacity="0" /><stop offset="1" stopColor="#8a5a2a" stopOpacity=".3" />
        </linearGradient>
      </defs>
      <path className="slip-paper" d="M18 4 C30 2 44 3 56 2 C52 22 58 40 54 58 C50 76 58 92 50 108 C38 110 22 109 10 110 C14 92 6 76 10 58 C14 40 8 22 18 4 Z" fill={`url(#${id}-paper)`} />
      <path d="M18 4 C30 2 44 3 56 2 C52 22 58 40 54 58 C50 76 58 92 50 108 C38 110 22 109 10 110 C14 92 6 76 10 58 C14 40 8 22 18 4 Z" fill={`url(#${id}-shade)`} />
      <path className="slip-ink" d="M22 12 C30 9 40 10 48 9 M24 14 C22 34 26 50 22 66 C19 80 24 92 19 102 M46 12 C44 32 48 50 44 66 C41 80 46 92 42 102" fill="none" />
      <text className="slip-rune" x="34" y="50" textAnchor="middle">{rune}</text>
      <path className="slip-ink thin" d="M27 62 C31 60 37 60 41 62 M28 68 C32 66 36 66 40 68" fill="none" />
      <rect className="slip-seal" x="26" y="80" width="16" height="16" rx="2" transform="rotate(-6 34 88)" />
      <text className="slip-seal-mark" x="34" y="92" textAnchor="middle" transform="rotate(-6 34 88)">{rune}</text>
    </svg>
  </span>;
}

function TalismanCard({ talisman, children }) {
  return <article className={`card talisman-card ${talisman.equipped ? "is-equipped" : ""}`}>
    <div className="talisman-card-head"><TalismanMedal talisman={talisman} size="lg" /><h3>{talisman.name}</h3></div>
    <span className="item-kind">{talisman.active ? "Use in duel" : "Triggers itself"}{talisman.charges ? ` · ${talisman.charges}× per duel` : " · always on"}</span>
    <p className="talisman-effect">{talisman.effect}</p>
    {talisman.drawback ? <p className="talisman-drawback"><b>Cost:</b> {talisman.drawback}</p> : <p className="talisman-drawback is-none">No drawback</p>}
    {children}
  </article>;
}

// The talisman row on a rune card in a duel. Mine can be used; theirs is shown so you can play around it.
function DuelTalisman({ talisman, mine, disabled, myMana, myHp, onUse }) {
  if (!talisman) return <div className="duel-talisman is-empty"><span>{mine ? "No talisman" : "No talisman"}</span></div>;
  const pips = Array.from({ length: talisman.charges || 0 }, (_, i) => <i key={i} className={i < talisman.chargesLeft ? "is-full" : ""} />);
  const cost = [talisman.manaCost ? `${talisman.manaCost}◈` : "", talisman.hpCost ? `${talisman.hpCost} HP` : ""].filter(Boolean).join(" ");
  const affordable = (myMana ?? 0) >= (talisman.manaCost || 0) && (myHp ?? 0) > (talisman.hpCost || 0);
  const canUse = mine && talisman.active && talisman.chargesLeft > 0 && !talisman.usedThisTurn && affordable && !disabled;
  const status = talisman.twinActive ? "Active this turn" : talisman.stillThisTurn ? "Dice held this turn" : talisman.freeReroll ? "Free reroll ready" : !talisman.active ? (talisman.chargesLeft > 0 || !talisman.charges ? "Triggers itself" : "Spent") : talisman.chargesLeft > 0 ? "" : "Spent";
  return <div className={`duel-talisman ${talisman.twinActive || talisman.stillThisTurn ? "is-lit" : ""} ${talisman.chargesLeft === 0 && talisman.charges ? "is-spent" : ""}`} title={`${talisman.name}: ${talisman.effect}${talisman.drawback ? ` Cost: ${talisman.drawback}` : ""}`}>
    <TalismanMedal talisman={talisman} size="sm" />
    <span className="duel-talisman-copy"><b>{talisman.name}</b>{status && <small>{status}</small>}</span>
    {pips.length > 0 && <span className="talisman-pips" aria-label={`${talisman.chargesLeft} of ${talisman.charges} charges left`}>{pips}</span>}
    {mine && talisman.active && talisman.chargesLeft > 0 && !talisman.usedThisTurn && <button type="button" className="talisman-use" disabled={!canUse} onClick={onUse} title={talisman.effect}>Use<span className="talisman-use-name"> {talisman.name}</span>{cost && <b>{cost}</b>}</button>}
  </div>;
}

function RematchActions({ combat, onAskRematch, onFindCasual }) {
  const r = combat.rematch;
  const name = combat.opponent;
  if (r?.matchId) return <button disabled>Starting rematch…</button>;
  if (!r?.available) return combat.mode === "casual" ? <button className="secondary" onClick={onFindCasual}>Find another</button> : null;
  if (r.mine) return <button className="secondary" disabled>Waiting for {name}…</button>;
  if (r.theirs) return <button className="rematch-ready" onClick={onAskRematch}>Accept rematch</button>;
  return <button className="secondary" onClick={onAskRematch}>Rematch</button>;
}

function CombatScreen({ settings = DEFAULT_SETTINGS, content, combat, onSubmit, onShape, onTalisman, onRefresh, onGiveUp, onClaimWin, onHome, onLeague, onRematch, onAskRematch, onFindCasual, onLoadRounds, onTutorialEnd }) {
  const tutorial = combat.practice === "tutorial";
  const [coachStep, setCoachStep] = useState(tutorial ? 0 : -1);
  const tutorialEnded = useRef(false);
  const [state, setState] = useState("");
  const [submitted, setSubmitted] = useState(Boolean(combat.waitingForOpponent));
  const [shown, setShown] = useState(combat);
  const [view, setView] = useState(() => ({ mine: diceView(combat.myDice), theirs: diceView(combat.opponentDice) }));
  const [modes, setModes] = useState({ myRune: "rest", myForm: "rest", oppRune: "rest", oppForm: "rest" });
  const [effects, setEffects] = useState([]);
  const [hit, setHit] = useState("");
  const [paused, setPaused] = useState(false);
  const [formOpen, setFormOpen] = useState(false);
  const [shaping, setShaping] = useState(false);
  const [anim, setAnim] = useState({ me: "idle", opp: "idle" });
  const [bolts, setBolts] = useState([]);
  const [ringReady, setRingReady] = useState(false);
  const [confirmLeave, setConfirmLeave] = useState(false);
  const [pending, setPending] = useState("");
  // The short result shown over the ring after each round.
  const [banner, setBanner] = useState(null);
  const [shake, setShake] = useState("");
  const [infoOpen, setInfoOpen] = useState("");
  const pendingTimer = useRef(null);
  const playing = useRef(false);
  const timers = useRef([]);
  const lastKey = useRef(rollKey(combat));
  const shownRef = useRef(combat);
  const viewRef = useRef(view);

  const facesFor = (name) => {
    const runes = (content?.classes || []).find((item) => item.name === name)?.runes || [];
    return runes.length ? runes.map((rune) => rune.symbol) : Array(6).fill("ᛟ");
  };
  const myFaces = facesFor(combat.myClass);
  const theirFaces = facesFor(combat.opponentClass);
  const formList = content?.forms?.length ? content.forms : (combat.forms || []).map((name, face) => ({ name, face, state: "", blurb: "" }));
  const formNames = formList.map((item) => item.name);
  const formInfo = (name) => formList.find((item) => item.name === name);
  const awayClaimSeconds = content?.rules?.awayClaimSeconds ?? 60;
  const dieSize = useDieSize();
  // Both dice share the rune card, so each is a little smaller than a lone die.
  const cardDie = Math.min(dieSize >= 100 ? 84 : 66, Math.round(dieSize * 0.8));
  const states = combat.states || RING_ORDER;
  const manaCap = combat.manaCap || 5;
  const pickCost = combat.formPickCost ?? 2;
  const rerollCost = combat.rerollManaCost ?? 1;
  const instant = settings.reduceMotion || settings.diceAnimation === false;

  const later = (fn, ms) => { const id = setTimeout(fn, ms); timers.current.push(id); return id; };
  const setViewBoth = (next) => { viewRef.current = next; setView(next); };
  useEffect(() => () => { timers.current.forEach(clearTimeout); clearTimeout(pendingTimer.current); }, []);
  useEffect(() => { const id = setTimeout(() => setRingReady(true), 80); return () => clearTimeout(id); }, []);

  // A round has resolved: show the dice that were played, apply the damage, then roll next turn's dice.
  useEffect(() => {
    const key = rollKey(combat);
    if (key === lastKey.current) return;
    lastKey.current = key;
    timers.current.forEach(clearTimeout);
    timers.current = [];
    playing.current = true;
    const next = combat;
    const playedMine = normalizeRoll(next.lastRoll);
    const playedTheirs = normalizeRoll(next.opponentRoll);
    setViewBoth({ mine: { ...viewRef.current.mine, ...playedMine }, theirs: { ...viewRef.current.theirs, ...playedTheirs } });
    setModes({ myRune: "landing", myForm: "landing", oppRune: "landing", oppForm: "landing" });
    later(() => {
      const before = shownRef.current;
      const list = [];
      if (next.opponentHp < before.opponentHp) list.push({ id: `${next.turn}-o`, side: "opp", type: "damage", text: `−${before.opponentHp - next.opponentHp}` });
      if (next.myHp < before.myHp) list.push({ id: `${next.turn}-m`, side: "me", type: "damage", text: `−${before.myHp - next.myHp}` });
      if (next.myHp > before.myHp) list.push({ id: `${next.turn}-h`, side: "me", type: "heal", text: `+${next.myHp - before.myHp}` });
      if (next.myShield > before.myShield) list.push({ id: `${next.turn}-s`, side: "me", type: "shield", text: `+${next.myShield - before.myShield} ward` });
      setEffects(settings.damageNumbers === false ? [] : list);
      const iHit = next.opponentHp < before.opponentHp;
      const theyHit = next.myHp < before.myHp;
      const dealt = Math.max(0, before.opponentHp - next.opponentHp);
      const taken = Math.max(0, before.myHp - next.myHp);
      const mineState = playedMine?.state || "";
      const theirState = playedTheirs?.state || "";
      const clash = !mineState || !theirState || mineState === theirState ? "even" : beats(mineState, theirState) ? "won" : "lost";
      const parts = [];
      if (dealt) parts.push(`You deal ${dealt}`);
      if (taken) parts.push(`You take ${taken}`);
      if (next.myHp > before.myHp) parts.push(`You heal ${next.myHp - before.myHp}`);
      if (next.myShield > before.myShield) parts.push(`+${next.myShield - before.myShield} shield`);
      const fired = [next.myTalisman?.firedLastRound && `Your ${next.myTalisman.name}`, next.opponentTalisman?.firedLastRound && `${next.opponent}'s ${next.opponentTalisman.name}`].filter(Boolean);
      setBanner({ id: next.turn, clash, mineState, theirState, detail: parts.join(" · ") || "No damage", fired });
      if (!settings.reduceMotion && Math.max(dealt, taken) >= 6) setShake(dealt >= taken ? "opp" : "me");
      setHit(iHit ? "opp" : theyHit ? "me" : "");
      setAnim({ me: theyHit ? "hit" : iHit || next.myShield > before.myShield ? "cast" : "idle", opp: iHit ? "hit" : theyHit ? "cast" : "idle" });
      const flying = [];
      if (iHit) flying.push({ id: `${next.turn}-b1`, from: "me", cls: next.myClass });
      if (theyHit) flying.push({ id: `${next.turn}-b2`, from: "opp", cls: next.opponentClass });
      setBolts(flying);
      shownRef.current = next;
      setShown(next);
    }, 450);
    later(() => { setEffects([]); setHit(""); setBolts([]); setShake(""); setAnim({ me: "idle", opp: "idle" }); }, 1900);
    later(() => setBanner(null), next.status === "finished" ? 1900 : 2700);
    if (next.status === "finished") {
      later(() => { playing.current = false; setSubmitted(false); setState(""); }, 1900);
      return;
    }
    // Next turn: the dice tumble and land on the new roll.
    later(() => setModes({ myRune: "rolling", myForm: "rolling", oppRune: "rolling", oppForm: "rolling" }), 2100);
    later(() => {
      setViewBoth({ mine: diceView(next.myDice), theirs: diceView(next.opponentDice) });
      setModes({ myRune: "landing", myForm: "landing", oppRune: "landing", oppForm: "landing" });
    }, 2800);
    later(() => {
      setModes({ myRune: "rest", myForm: "rest", oppRune: "rest", oppForm: "rest" });
      playing.current = false;
      setState("");
      setPending("");
      setSubmitted(false);
      setFormOpen(false);
    }, 4000);
  }, [combat]);

  // Shaping by either player during the turn: animate only the die that changed.
  useEffect(() => {
    if (playing.current) return;
    shownRef.current = combat;
    setShown(combat);
    setSubmitted(Boolean(combat.waitingForOpponent));
    const now = viewRef.current;
    const mine = diceView(combat.myDice);
    const theirs = diceView(combat.opponentDice);
    const changes = {};
    if (mine && now.mine && mine.symbol !== now.mine.symbol) changes.myRune = true;
    if (mine && now.mine && mine.form !== now.mine.form) changes.myForm = true;
    if (theirs && now.theirs && theirs.symbol !== now.theirs.symbol) changes.oppRune = true;
    if (theirs && now.theirs && theirs.form !== now.theirs.form) changes.oppForm = true;
    const keys = Object.keys(changes);
    if (!keys.length || !now.mine) { setViewBoth({ mine, theirs }); return; }
    setModes((current) => ({ ...current, ...Object.fromEntries(keys.map((k) => [k, "rolling"])) }));
    later(() => {
      setViewBoth({ mine, theirs });
      setModes((current) => ({ ...current, ...Object.fromEntries(keys.map((k) => [k, "landing"])) }));
    }, 550);
    later(() => setModes((current) => ({ ...current, ...Object.fromEntries(keys.map((k) => [k, "rest"])) })), 1700);
  }, [combat]);

  useEffect(() => {
    if (combat.winner || combat.status === "finished") return undefined;
    const timer = setInterval(() => { onRefresh(); }, 2000);
    return () => clearInterval(timer);
  }, [combat.winner, combat.status, combat.matchId]);

  // After a casual or friend duel, keep watching for the opponent's rematch answer while the window is open.
  const rematchOpen = combat.status === "finished" && combat.rematch?.available;
  useEffect(() => {
    if (!rematchOpen) return undefined;
    const timer = setInterval(() => { onRefresh(); }, 1500);
    return () => clearInterval(timer);
  }, [rematchOpen, combat.matchId]);

  async function shape(kind, extra = {}) {
    if (shaping) return;
    setShaping(true);
    setFormOpen(false);
    await onShape({ kind, ...extra });
    setShaping(false);
  }

  async function talismanUse() {
    if (shaping) return;
    setShaping(true);
    setFormOpen(false);
    await onTalisman?.();
    setShaping(false);
  }

  function endTutorial(finishedDuel) {
    if (tutorialEnded.current) return;
    tutorialEnded.current = true;
    setCoachStep(-1);
    onTutorialEnd?.(finishedDuel);
  }

  // Tapping a state declares it after a short pause; tapping it again in that pause takes it back.
  async function lockIn(name) {
    setPending("");
    setSubmitted(true);
    setFormOpen(false);
    const ok = await onSubmit({ state: name });
    if (ok === false) { setSubmitted(false); setState(""); }
  }
  function choose(name) {
    if (submitted || playing.current) return;
    clearTimeout(pendingTimer.current);
    if (pending === name) { setPending(""); setState(""); return; }
    setPending(name);
    setState(name);
    setFormOpen(false);
    pendingTimer.current = setTimeout(() => lockIn(name), LOCK_DELAY_MS);
  }

  const settling = Object.values(modes).some((mode) => mode !== "rest");
  // Tutorial steps that wait for the player move on by themselves.
  const coach = coachStep >= 0 && coachStep < TUTORIAL_STEPS.length ? TUTORIAL_STEPS[coachStep] : null;
  useEffect(() => {
    if (!coach) return;
    if (coach.waitFor === "submit" && submitted) setCoachStep((n) => n + 1);
  }, [coach, state, submitted]);
  const roundDone = coach?.waitFor === "round" && shown.turn >= 2 && !playing.current && !submitted;
  useEffect(() => {
    if (tutorial && shown.status === "finished" && !playing.current) endTutorial(true);
  }, [tutorial, shown.status, playing.current]);
  const locked = submitted || playing.current;
  const finished = shown.status === "finished" && !playing.current;
  const mine = view.mine;
  const theirs = view.theirs;
  const myMana = shown.myMana ?? 0;
  const isFlamecaller = combat.myClass === "Flamecaller";
  const held = Boolean(shown.myTalisman?.stillThisTurn);
  const freeReroll = Boolean(shown.myTalisman?.freeReroll);
  const canReroll = !locked && !held && mine && !mine.rerolled && (freeReroll || (isFlamecaller ? shown.myHp > 1 : myMana >= rerollCost));
  const canPick = !locked && !held && mine && !mine.picked && myMana >= pickCost;
  const canShift = !locked && !held && mine && combat.myClass === "Fateweaver" && !mine.shifted && !mine.picked;
  const formIndex = mine ? formNames.indexOf(mine.form) : -1;
  const myCommit = mine?.formState || formInfo(mine?.form)?.state || "";
  const theirCommit = theirs?.formState || formInfo(theirs?.form)?.state || "";
  const tags = (dice) => [dice?.rerolled && "↻", dice?.picked && "★", dice?.shifted && "⇅"].filter(Boolean).join(" ");
  const caption = (dice) => dice?.rune ? `${dice.rune} · ${dice.form}${tags(dice) ? ` ${tags(dice)}` : ""}` : "Rolling…";
  const stateList = RING_ORDER.filter((name) => states.includes(name));
  // One line that sums up the table, replacing the old reading panel.
  const hint = finished ? ""
    : playing.current ? "The round resolves…"
    : pending ? `Declaring ${pending}… tap it again to take it back.`
    : !state && !submitted && mine?.form && shown.turn === 1 && !tutorial ? <>Pick a state on the ring. Your {mine.form} wants <em className={`state-tag type-${myCommit.toLowerCase()}`}>{myCommit}</em> (+2) · {shown.opponent} shows <em className={`state-tag type-${theirCommit.toLowerCase()}`}>{theirCommit}</em></>
    : submitted ? `${state || "State"} sealed. Waiting for ${shown.opponent}${shown.opponentLocked ? "" : " to declare"}…`
    : !mine?.form ? "Rolling…"
    : <>Your {mine.form} wants <em className={`state-tag type-${myCommit.toLowerCase()}`}>{myCommit}</em> for +2 · {shown.opponent} shows <em className={`state-tag type-${theirCommit.toLowerCase()}`}>{theirCommit}</em></>;

  // Keys 1, 2 and 3 declare Break, Flow and Bind.
  useEffect(() => {
    const onKey = (event) => {
      if (event.target instanceof HTMLInputElement || paused || formOpen) return;
      const index = ["1", "2", "3"].indexOf(event.key);
      if (index >= 0 && stateList[index] && !finished) choose(stateList[index]);
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  });

  return <section className={`duel ${hit ? `hit-${hit}` : ""} ${shake ? `shake shake-${shake}` : ""}`}>
    <header className="duel-top">
      <div className="duel-bar">
        <button className="hud-icon pause-button" onClick={() => setPaused(true)} aria-label="Pause combat">Ⅱ</button>
        <div className="duel-title"><span className="eyebrow">{combat.practice ? `PRACTICE · ${combat.practice.toUpperCase()} SHADE` : "RUNE DUEL"}</span><strong>{shown.status === "finished" ? "Duel finished" : `Turn ${shown.turn}`}</strong></div>
        <div className="duel-actions"><span className="combat-class-badge">{combat.myClass}</span><button className="hud-icon" onClick={onLeague} aria-label="View league">♜</button></div>
      </div>
      <div className="duel-plates">
        <ResourceHud side="player" name={`${shown.playerName} · ${shown.myClass}`} hp={shown.myHp} shield={shown.myShield} mana={shown.myMana} manaCap={manaCap} />
        <ResourceHud side="opponent" name={`${shown.opponent} · ${shown.opponentClass}`} hp={shown.opponentHp} shield={shown.opponentShield} mana={shown.opponentMana} manaCap={manaCap} />
      </div>
    </header>

    <div className="duel-stage">
      <div className={`duelist duelist-me ${hit === "me" ? "is-hit" : ""}`}>
        <MageFigure cls={combat.myClass} pose={finished && shown.winner === "opponent" ? "down" : finished && shown.winner === "me" ? "win" : anim.me} />
        <button type="button" className="duelist-tap" aria-label="Your class rule and gear" aria-expanded={infoOpen === "me"} onClick={() => setInfoOpen((v) => (v === "me" ? "" : "me"))} />
        {infoOpen === "me" && <div className="duelist-info is-pop" role="dialog"><b>{combat.myClass}</b><span>{combat.myCoreRule}</span><StatChips stats={combat.myGear} attributes={content?.attributes} compact /></div>}
        {effects.filter((e) => e.side === "me").map((effect) => <span key={effect.id} className={`duel-float ${effect.type}`}>{effect.text}</span>)}
        {bolts.filter((b) => b.from === "opp").map((b) => <i key={`burst-${b.id}`} className={`spell-burst burst-${String(b.cls).toLowerCase()}`} aria-hidden="true"><i /><i /><i /><i /><i /><i /></i>)}
      </div>
      <div className="rune-field">
        <div className="rune-card mine">
          <span className="rune-card-who">You</span>
          <div className="rune-card-dice">
            <Die3D kind="d6" labels={myFaces} face={faceOf(myFaces, mine?.symbol)} mode={modes.myRune} size={cardDie} tone="die-rune" instant={instant} />
            <Die3D kind="d8" labels={formNames} face={faceOf(formNames, mine?.form)} mode={modes.myForm} size={Math.round(cardDie * 1.05)} tone="die-form" instant={instant} />
          </div>
          <strong className="rune-card-rune">{mine?.rune || "…"}{tags(mine) && <i className="rune-card-marks" title="↻ rerolled · ★ form picked · ⇅ shifted">{tags(mine)}</i>}</strong>
          <span className="rune-card-form" key={`m-${mine?.form}`}>{mine?.form || "Rolling…"}{myCommit && !finished && <em className={`state-tag type-${myCommit.toLowerCase()}`}>{myCommit}</em>}</span>
          {formInfo(mine?.form)?.blurb && <small className="rune-card-blurb">{formInfo(mine?.form).blurb}</small>}
          {!finished && shown.myTalisman && <DuelTalisman talisman={shown.myTalisman} mine disabled={locked || shaping || settling} myMana={myMana} myHp={shown.myHp} onUse={talismanUse} />}
          {!locked && !finished && <div className="shape-tray" role="group" aria-label="Shape your rune">
            <button type="button" disabled={!canReroll || shaping || settling} onClick={() => shape("reroll")} title={isFlamecaller ? "Flamecallers pay 1 HP to reroll their rune" : `Reroll your rune for ${rerollCost} mana`}>{mine?.rerolled ? "Rerolled" : <>↻ Reroll <b>{freeReroll ? "Free" : isFlamecaller ? "1 HP" : `${rerollCost}◈`}</b></>}</button>
            <button type="button" disabled={!canPick || shaping || settling} onClick={() => setFormOpen((value) => !value)} aria-expanded={formOpen} title={`Choose your form for ${pickCost} mana`}>{mine?.picked ? "Face chosen" : <>★ Pick D8 <b>{pickCost}◈</b></>}</button>
            {combat.myClass === "Fateweaver" && <span className="shift-pair">
              <button type="button" disabled={!canShift || formIndex <= 0 || shaping || settling} onClick={() => shape("shift", { direction: -1 })} aria-label="Shift your form down one face" title="Shift your form down one face">−1</button>
              <button type="button" disabled={!canShift || formIndex >= formNames.length - 1 || shaping || settling} onClick={() => shape("shift", { direction: 1 })} aria-label="Shift your form up one face" title="Shift your form up one face">+1</button>
            </span>}
          </div>}
        </div>
        <div className="ring-slot">
          {banner && <div key={banner.id} className={`round-banner clash-${banner.clash}`} role="status">
            <span className="round-banner-states">
              <em className={`state-tag type-${banner.mineState.toLowerCase()}`}>{banner.mineState || "?"}</em>
              <i>{banner.clash === "won" ? "beats" : banner.clash === "lost" ? "loses to" : "meets"}</i>
              <em className={`state-tag type-${banner.theirState.toLowerCase()}`}>{banner.theirState || "?"}</em>
            </span>
            <strong>{banner.clash === "won" ? "Clash won" : banner.clash === "lost" ? "Clash lost" : "Even clash"}</strong>
            <small>{banner.detail}</small>
            {banner.fired?.length > 0 && <span className="round-banner-talisman">{banner.fired.map((name) => <em key={name}>✦ {name} took effect</em>)}</span>}
          </div>}
          {finished ? <div className="dice-vs">VS</div> : <StateRing states={states} info={content?.states || []} value={state} locked={locked} ready={ringReady}
            bonus={myCommit} pending={Boolean(pending)} onPick={choose} onReopen={() => choose(state)} />}
        </div>
        <div className="rune-card theirs">
          <span className="rune-card-who">{shown.opponent}{shown.opponentLocked && !finished && <b className="locked-tag">Declared ✓</b>}</span>
          <div className="rune-card-dice">
            <Die3D kind="d6" labels={theirFaces} face={faceOf(theirFaces, theirs?.symbol)} mode={modes.oppRune} size={cardDie} tone="die-rune" instant={instant} />
            <Die3D kind="d8" labels={formNames} face={faceOf(formNames, theirs?.form)} mode={modes.oppForm} size={Math.round(cardDie * 1.05)} tone="die-form" instant={instant} />
          </div>
          <strong className="rune-card-rune">{theirs?.rune || "…"}{tags(theirs) && <i className="rune-card-marks">{tags(theirs)}</i>}</strong>
          <span className="rune-card-form" key={`t-${theirs?.form}`}>{theirs?.form || "Rolling…"}{theirCommit && !finished && <em className={`state-tag type-${theirCommit.toLowerCase()}`}>{theirCommit}</em>}</span>
          {formInfo(theirs?.form)?.blurb && <small className="rune-card-blurb">{formInfo(theirs?.form).blurb}</small>}
          {!finished && shown.opponentTalisman && <DuelTalisman talisman={shown.opponentTalisman} />}
          {!finished && <div className="shape-tray opp-tray" aria-label="Their recent states">
            <span className="tray-label">Last played</span>
            {(shown.opponentHistory || []).slice(-3).map((st, i) => <em key={i} className={`state-tag type-${String(st).toLowerCase()}`}>{st}</em>)}
            {!(shown.opponentHistory || []).length && <span className="tray-none">nothing yet</span>}
          </div>}
        </div>
        {!finished && <p className="duel-tip" aria-live="polite"><span className="duel-tip-label">{pending || submitted ? "STATE" : "READ"}</span><span>{hint}</span></p>}
        {bolts.map((bolt) => <i key={bolt.id} className={`bolt bolt-from-${bolt.from} bolt-${String(bolt.cls).toLowerCase()}`} aria-hidden="true" />)}
      </div>
      <div className={`duelist duelist-opp ${hit === "opp" ? "is-hit" : ""}`}>
        <MageFigure cls={combat.opponentClass} art={combat.practice ? "shade" : undefined} flip pose={finished && shown.winner === "me" ? "down" : finished && shown.winner === "opponent" ? "win" : anim.opp} />
        <button type="button" className="duelist-tap" aria-label={`${combat.opponent}'s class rule and gear`} aria-expanded={infoOpen === "opp"} onClick={() => setInfoOpen((v) => (v === "opp" ? "" : "opp"))} />
        {infoOpen === "opp" && <div className="duelist-info is-pop" role="dialog"><b>{combat.opponentClass}</b><span>{combat.opponentCoreRule}</span><StatChips stats={combat.opponentGear} attributes={content?.attributes} compact /></div>}
        {effects.filter((e) => e.side === "opp").map((effect) => <span key={effect.id} className={`duel-float ${effect.type}`}>{effect.text}</span>)}
        {bolts.filter((b) => b.from === "me").map((b) => <i key={`burst-${b.id}`} className={`spell-burst burst-${String(b.cls).toLowerCase()}`} aria-hidden="true"><i /><i /><i /><i /><i /><i /></i>)}
      </div>
    </div>

    {!finished && combat.status !== "finished" && combat.opponentAway
      ? <div className="duel-log away-log" role="status">
          <span><strong>{combat.opponent} has left the duel.</strong> {combat.claimWinInSeconds > 0 ? `You can claim the win in ${combat.claimWinInSeconds}s if they do not return.` : "Claim the win, or keep waiting."}</span>
          {combat.claimWinInSeconds <= 0 && <button type="button" onClick={onClaimWin}>Claim victory</button>}
        </div>
      : null}

    <footer className="duel-controls">
      {finished ? <div className="duel-finished">
        <h3>{shown.draw ? "Draw" : shown.winner === "me" ? "Victory!" : "Defeat"}</h3>
        <p>{tutorial ? "Tutorial complete. You are ready for the arena." : combat.practice ? "Practice duel against the Shade. Nothing in your league or quests has changed." : combat.mode === "friend" ? "Friend duel. Nothing in your league, rating or coins has changed." : combat.mode === "casual" ? "Casual duel. No league points or rating change." : shown.draw ? "Both mages fell together. No league points change." : "The duel is over. Here is what it changed."}</p>
        <ResultSummary combat={combat} onLoadRounds={onLoadRounds} />
        <div className="actions"><button onClick={onHome}>Return home</button>{combat.practice ? <button className="secondary" onClick={() => onRematch?.(combat.practice === "tutorial" ? "easy" : combat.practice)}>{combat.practice === "tutorial" ? "Practice more" : "Duel again"}</button> : combat.mode && combat.mode !== "ranked" ? <RematchActions combat={combat} onAskRematch={onAskRematch} onFindCasual={onFindCasual} /> : <button className="secondary" onClick={onLeague}>View ranks</button>}</div>
        {!combat.practice && combat.rematch?.available && combat.rematch.theirs && !combat.rematch.mine && <p className="rematch-note">{combat.opponent} wants a rematch.</p>}
      </div> : null}
    </footer>
    {formOpen && !locked && <div className="form-sheet" role="dialog" aria-label="Choose your D8 face">
      <div className="form-sheet-card">
        <header><div><span className="eyebrow">MANA · {shown.myMana}/{manaCap}</span><h3>Choose your form</h3><p>Costs {pickCost} mana. Your D8 turns to this face.</p></div><button className="modal-close" type="button" onClick={() => setFormOpen(false)} aria-label="Close">✕</button></header>
        <div className="form-grid">
          {formList.map(({ name: form, state: formState, blurb }) => <button type="button" key={form} className={`form-chip type-${String(formState).toLowerCase()} ${mine?.form === form ? "is-chosen" : ""}`} onClick={() => shape("pick", { form })}><b>{form}</b><small>{formState}{blurb ? ` · ${blurb}` : ""}</small></button>)}
        </div>
      </div>
    </div>}
    {coach && !finished && (coach.waitFor !== "round" || roundDone || coachStep === TUTORIAL_STEPS.length - 1) && <Coach
      step={coachStep + 1} total={TUTORIAL_STEPS.length} target={coach.target} title={coach.title} text={coach.text} action={coach.action}
      waiting={coach.waitFor === "submit" ? "Tap a state on the ring" : coach.waitFor === "round" && !roundDone ? "Waiting for the round…" : ""}
      onNext={() => setCoachStep((n) => (n + 1 >= TUTORIAL_STEPS.length ? -1 : n + 1))}
      onSkip={() => endTutorial(false)} />}
    {paused && <div className="pause-overlay" role="dialog" aria-modal="true" aria-label="Pause menu"><div className="pause-menu"><span className="eyebrow">RUNE DUEL</span><h2>Paused</h2>
      {confirmLeave
        ? <><p className="muted">{combat.practice ? "This is only practice. Ending it changes nothing in your league." : `Forfeiting counts as a loss and gives ${combat.opponent} the win.`}</p><button className="danger" onClick={() => { setConfirmLeave(false); setPaused(false); onGiveUp(); }}>Yes, forfeit</button><button className="secondary" onClick={() => setConfirmLeave(false)}>Back</button></>
        : <><p className="muted">{combat.practice ? "You can step away and continue this practice duel from the home screen." : `You can step away and continue this duel from the home screen. If you are gone for more than ${awayClaimSeconds} seconds, your opponent may claim the win.`}</p><button onClick={() => setPaused(false)}>Resume battle</button><button className="secondary" onClick={onHome}>Step away</button><button className="secondary" onClick={() => setConfirmLeave(true)}>Forfeit match</button></>}
    </div></div>}
  </section>;
}
