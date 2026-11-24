// Consequences: browser client. Plain JavaScript, no build step.
"use strict";

// ============================================================ utilities
const $ = (sel, el = document) => el.querySelector(sel);
const $$ = (sel, el = document) => [...el.querySelectorAll(sel)];
const esc = s => String(s ?? "").replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const store = {
  get(k, d) { try { const v = localStorage.getItem("conseq." + k); return v == null ? d : JSON.parse(v); } catch { return d; } },
  set(k, v) { try { localStorage.setItem("conseq." + k, JSON.stringify(v)); } catch { /* private mode */ } },
  del(k) { try { localStorage.removeItem("conseq." + k); } catch { } },
};
const html = (strings, ...vals) => strings.reduce((a, s, i) => a + s + (i < vals.length ? vals[i] : ""), "");
function el(markup) { const t = document.createElement("template"); t.innerHTML = markup.trim(); return t.content.firstElementChild; }

function toast(msg, kind = "") {
  const t = el(`<div class="toast ${kind}">${esc(msg)}</div>`);
  $("#toasts").appendChild(t);
  setTimeout(() => t.remove(), kind === "err" ? 4500 : 3000);
}

function modal(inner, { onClose } = {}) {
  const back = el(`<div class="modal-back"><div class="modal" role="dialog" aria-modal="true">${inner}</div></div>`);
  const close = () => { back.remove(); onClose && onClose(); };
  back.addEventListener("click", e => { if (e.target === back || e.target.closest("[data-close]")) close(); });
  document.body.appendChild(back);
  return { root: back.firstElementChild, close };
}

function ask(title, { placeholder = "", type = "text", ok = "OK" } = {}) {
  return new Promise(resolve => {
    let done = false;
    const m = modal(`<h2>${esc(title)}</h2><form><input type="${type}" placeholder="${esc(placeholder)}" autocomplete="off"><div class="row" style="margin-top:12px;justify-content:flex-end"><button type="button" class="btn ghost" data-close>Cancel</button><button class="btn primary">${esc(ok)}</button></div></form>`,
      { onClose: () => { if (!done) resolve(null); } });
    const input = $("input", m.root);
    setTimeout(() => input.focus(), 50);
    $("form", m.root).addEventListener("submit", e => { e.preventDefault(); done = true; resolve(input.value); m.close(); });
  });
}

function confirmBox(title, text, ok = "Yes") {
  return new Promise(resolve => {
    let done = false;
    const m = modal(`<h2>${esc(title)}</h2><p>${esc(text)}</p><div class="row" style="justify-content:flex-end"><button class="btn ghost" data-close>Cancel</button><button class="btn primary" data-ok>${esc(ok)}</button></div>`,
      { onClose: () => { if (!done) resolve(false); } });
    $("[data-ok]", m.root).addEventListener("click", () => { done = true; resolve(true); m.close(); });
  });
}

// ============================================================ app state
let config = null;              // /api/config
let state = null;               // latest room state from the server
let session = store.get("session", null); // {code, token, playerId}
let profile = store.get("profile", { name: "", avatar: "", color: "" });
let route = location.hash.replace(/^#\/?/, "") || "";
let clockOffset = 0;            // server time - local time
let current = { key: null, screen: null };
let soundOn = store.get("sound", true);
let speakOn = store.get("speak", true);
let wakeLock = null;

const F = key => !!(state ? state.features[key] : config?.features[key]);
const meId = () => state?.you?.id;
const isHost = () => !!state?.you?.isHost;
const playerById = id => state?.players.find(p => p.id === id);

// ============================================================ connection
const conn = new signalR.HubConnectionBuilder()
  .withUrl("hub")
  .withAutomaticReconnect([0, 1000, 2000, 4000, 8000, 10000, 15000, 20000, 30000, 30000])
  .configureLogging(signalR.LogLevel.Warning)
  .build();
conn.serverTimeoutInMilliseconds = 45000;

conn.on("state", s => onState(s));
conn.on("reaction", r => floatEmoji(r));
conn.on("sound", name => sfx(name));
conn.on("kicked", () => { leaveLocal(); toast("You were removed from the room.", "err"); });
conn.on("roomClosed", reason => { leaveLocal(); toast(reason || "The room closed.", "err"); });
conn.onreconnecting(() => { $("#conn-banner").hidden = false; });
conn.onreconnected(async () => { $("#conn-banner").hidden = true; await rejoin(true); });
conn.onclose(() => { $("#conn-banner").hidden = false; setTimeout(startConnection, 2500); });

async function startConnection() {
  try {
    await conn.start();
    $("#conn-banner").hidden = true;
    await rejoin(true);
  } catch {
    $("#conn-banner").hidden = false;
    setTimeout(startConnection, 3000);
  }
}

async function call(method, ...args) {
  try { return await conn.invoke(method, ...args); }
  catch (e) {
    const msg = (e?.message || "Something went wrong").replace(/^.*HubException: /, "");
    if (msg !== "PASSWORD_REQUIRED") toast(msg, "err");
    throw new Error(msg);
  }
}

async function rejoin(silent) {
  if (!session?.code || conn.state !== signalR.HubConnectionState.Connected) return;
  try {
    const res = await conn.invoke("JoinRoom", { code: session.code, token: session.token, name: profile.name, avatar: profile.avatar, color: profile.color });
    saveSession(res);
  } catch (e) {
    if (!silent) toast(String(e.message).replace(/^.*HubException: /, ""), "err");
    leaveLocal();
  }
}

function saveSession(res) {
  session = { code: res.code, token: res.token, playerId: res.playerId };
  store.set("session", session);
  const url = new URL(location.href);
  url.searchParams.set("room", res.code);
  history.replaceState(null, "", url.pathname + url.search + location.hash);
}

function leaveLocal() {
  session = null; state = null;
  store.del("session");
  const url = new URL(location.href);
  url.searchParams.delete("room");
  history.replaceState(null, "", url.pathname + url.search);
  releaseWakeLock();
  render();
}

function onState(s) {
  const prevPhase = state?.phase, prevRound = state?.game?.round, hadTask = !!state?.task;
  clockOffset = s.serverNow - Date.now();
  state = s;
  if (route) { route = ""; location.hash = ""; }
  // your turn to write: a little ding + buzz
  if (s.task && (!hadTask || prevRound !== s.game.round)) { sfx(prevPhase === "lobby" ? "start" : "ding"); navigator.vibrate?.(80); }
  if (s.phase !== "lobby") requestWakeLock(); else releaseWakeLock();
  render();
}

// ============================================================ sound & speech
let audio = null;
function sfx(name) {
  if (!soundOn || !F("sounds")) return;
  try {
    audio = audio || new (window.AudioContext || window.webkitAudioContext)();
    const t = audio.currentTime;
    const tone = (freq, start, dur, type = "sine", vol = .15) => {
      const o = audio.createOscillator(), g = audio.createGain();
      o.type = type; o.frequency.value = freq;
      g.gain.setValueAtTime(vol, t + start); g.gain.exponentialRampToValueAtTime(.001, t + start + dur);
      o.connect(g).connect(audio.destination); o.start(t + start); o.stop(t + start + dur);
    };
    const rustle = (dur, from, to) => {
      const len = audio.sampleRate * dur, buf = audio.createBuffer(1, len, audio.sampleRate), d = buf.getChannelData(0);
      for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * (1 - i / len);
      const src = audio.createBufferSource(), f = audio.createBiquadFilter(), g = audio.createGain();
      src.buffer = buf; f.type = "bandpass"; f.Q.value = 1.2;
      f.frequency.setValueAtTime(from, t); f.frequency.exponentialRampToValueAtTime(to, t + dur);
      g.gain.value = .35; src.connect(f).connect(g).connect(audio.destination); src.start(t);
    };
    switch (name) {
      case "fold": rustle(.35, 3000, 600); break;
      case "unfold": rustle(.4, 700, 3500); break;
      case "ding": tone(880, 0, .25); tone(1320, .12, .35); break;
      case "tick": tone(1200, 0, .05, "square", .05); break;
      case "start": [523, 659, 784].forEach((f, i) => tone(f, i * .1, .3, "triangle")); break;
      case "fanfare": [523, 659, 784, 1047, 784, 1047].forEach((f, i) => tone(f, i * .12, .35, "triangle", .12)); break;
      case "pop": tone(600, 0, .08, "sine", .08); break;
    }
  } catch { /* audio not available */ }
}

function speak(text) {
  if (!speakOn || !F("readAloud") || !("speechSynthesis" in window) || !text) return;
  const u = new SpeechSynthesisUtterance(text);
  const voices = speechSynthesis.getVoices();
  u.voice = voices.find(v => v.lang === "en-GB") || voices.find(v => v.lang?.startsWith("en")) || null;
  u.rate = 0.95;
  speechSynthesis.speak(u);
}

async function requestWakeLock() {
  if (wakeLock || !("wakeLock" in navigator) || document.visibilityState !== "visible") return;
  try { wakeLock = await navigator.wakeLock.request("screen"); wakeLock.addEventListener("release", () => { wakeLock = null; }); } catch { }
}
function releaseWakeLock() { try { wakeLock?.release(); } catch { } wakeLock = null; }
document.addEventListener("visibilitychange", () => { if (document.visibilityState === "visible" && state && state.phase !== "lobby") requestWakeLock(); });

// ============================================================ rendering
function render() {
  let screen;
  if (route === "halloffame") screen = HallOfFameScreen;
  else if (route === "admin") screen = AdminScreen;
  else if (!state) screen = HomeScreen;
  else if (state.phase === "lobby") screen = LobbyScreen;
  else if (state.phase === "writing") screen = state.task ? WriteScreen : WaitScreen;
  else if (state.phase === "reveal") screen = RevealScreen;
  else screen = ResultsScreen;

  const key = screen.key(state);
  const view = $("#view");
  if (key !== current.key) {
    current.screen?.unmount?.();
    view.innerHTML = "";
    current = { key, screen };
    screen.mount(view, state);
    window.scrollTo({ top: 0 });
  }
  screen.update?.(view, state);

  const chip = $("#room-chip");
  chip.hidden = !state;
  if (state) chip.textContent = "Room " + state.code;
  updateChat();
}

// ------------------------------------------------------------ shared bits
function avatarHtml(p, cls = "av") {
  return `<div class="${cls}" style="border-color:${esc(p.color)}">${esc(p.avatar)}</div>`;
}

function shareUrl(code) {
  const base = (config.publicUrl || (location.origin + location.pathname)).replace(/\/$/, "").replace(/\/index\.html$/, "");
  return `${base}/?room=${code}`;
}

function qrSvg(text) {
  try { const q = qrcode(0, "M"); q.addData(text); q.make(); return q.createSvgTag({ cellSize: 4, margin: 2, scalable: true }); }
  catch { return ""; }
}

function fmtTime(ms) {
  const s = Math.max(0, Math.ceil(ms / 1000));
  return s >= 60 ? `${Math.floor(s / 60)}:${String(s % 60).padStart(2, "0")}` : String(s);
}

// global ticking clock: timers + auto-submit when time runs out
let lastTickSecond = null;
setInterval(() => {
  if (!state?.deadline) return;
  const remaining = state.deadline - (Date.now() + clockOffset);
  const total = state.task?.canDraw || state.task?.kind === "drawing" ? state.settings.drawSeconds * 1000 : state.settings.textSeconds * 1000;
  $$(".timer").forEach(t => {
    t.style.setProperty("--p", Math.max(0, Math.min(100, remaining / total * 100)) + "%");
    t.querySelector("span").textContent = fmtTime(remaining);
    t.classList.toggle("urgent", remaining < 10000);
  });
  const sec = Math.ceil(remaining / 1000);
  if (state.task && sec <= 5 && sec > 0 && sec !== lastTickSecond) { lastTickSecond = sec; sfx("tick"); }
  if (state.task && remaining <= 300 && current.screen === WriteScreen) WriteScreen.autoSubmit();
}, 250);

// ============================================================ HOME
const HomeScreen = {
  key: () => "home",
  mount(view) {
    const params = new URLSearchParams(location.search);
    const roomFromUrl = (params.get("room") || "").toUpperCase();
    if (!profile.avatar) profile.avatar = config.avatars[Math.floor(Math.random() * config.avatars.length)];
    if (!profile.color) profile.color = config.colors[Math.floor(Math.random() * config.colors.length)];

    view.innerHTML = html`
      <section class="hero">
        <h1>${config.appName === "Consequences" ? "Conse<span>q</span>uences" : esc(config.appName)}</h1>
        <p>The folded-paper game: write a bit, fold it over, pass it on… then unfold the chaos.</p>
      </section>
      <div class="grid-2" style="margin-top:18px">
        <div class="stack">
          <div class="card tape">
            <h2>Who are you?</h2>
            <label class="field"><span>Your name</span><input type="text" id="name" maxlength="20" placeholder="e.g. Auntie Pam" value="${esc(profile.name)}" autocomplete="nickname"></label>
            <div class="field"><span style="font-weight:600;font-size:.9rem">Pick a face</span><div class="picker" id="avatars" style="margin-top:6px">${config.avatars.map(a => `<button type="button" data-v="${a}" class="${a === profile.avatar ? "sel" : ""}">${a}</button>`).join("")}</div></div>
            <div class="field"><span style="font-weight:600;font-size:.9rem">Pick a colour</span><div class="picker colors" id="colors" style="margin-top:6px">${config.colors.map(c => `<button type="button" data-v="${c}" style="background:${c}" class="${c === profile.color ? "sel" : ""}" aria-label="${c}"></button>`).join("")}</div></div>
          </div>
        </div>
        <div class="stack">
          <div class="card">
            <h2>Join a room</h2>
            <form id="join-form" class="row" autocomplete="off">
              <input type="text" id="code" class="code-input" maxlength="5" placeholder="CODE" value="${esc(roomFromUrl)}" style="flex:1;min-width:140px" aria-label="Room code">
              <button class="btn primary big">Join</button>
            </form>
            ${F("spectators") ? `<label class="row small muted" style="margin-top:8px;cursor:pointer"><input type="checkbox" id="spectate"> Just watch (spectate)</label>` : ""}
          </div>
          <div class="card">
            <h2>Start a new game</h2>
            <p class="muted small" style="margin-top:-4px">Create a room, then share the code or QR with everyone playing.</p>
            <button class="btn teal big block" id="create">✨ Create a room</button>
          </div>
          ${F("publicRooms") ? `<div class="card"><div class="row between"><h2 style="margin:0">Open rooms</h2><span class="small muted" id="rooms-count"></span></div><div class="room-list" id="rooms" style="margin-top:10px"><p class="muted small">Looking for rooms…</p></div></div>` : ""}
          <div class="row">
            ${F("hallOfFame") ? `<a class="btn gold" href="#/halloffame">🏆 Hall of Fame</a>` : ""}
            ${config.adminAvailable ? `<a class="btn ghost" href="#/admin">⚙️ Settings</a>` : ""}
          </div>
        </div>
      </div>
      <p class="center small muted" style="margin-top:28px">v${esc(config.version)}</p>`;

    const nameInput = $("#name", view);
    nameInput.addEventListener("input", () => { profile.name = nameInput.value; store.set("profile", profile); });
    $("#avatars", view).addEventListener("click", e => {
      const b = e.target.closest("button"); if (!b) return;
      profile.avatar = b.dataset.v; store.set("profile", profile);
      $$("#avatars button", view).forEach(x => x.classList.toggle("sel", x === b)); sfx("pop");
    });
    $("#colors", view).addEventListener("click", e => {
      const b = e.target.closest("button"); if (!b) return;
      profile.color = b.dataset.v; store.set("profile", profile);
      $$("#colors button", view).forEach(x => x.classList.toggle("sel", x === b));
    });

    const needName = () => {
      if (profile.name.trim()) return true;
      toast("Pop your name in first! ✍️", "err"); nameInput.focus(); return false;
    };

    $("#join-form", view).addEventListener("submit", async e => {
      e.preventDefault();
      const code = $("#code", view).value.trim().toUpperCase();
      if (!code) { toast("Type the room code", "err"); return; }
      if (!needName()) return;
      await joinRoom(code, $("#spectate", view)?.checked);
    });

    $("#create", view).addEventListener("click", async () => {
      if (!needName()) return;
      let pin = "";
      if (config.createPinRequired) {
        pin = await ask("Room-creation PIN", { type: "password", placeholder: "Ask the server owner", ok: "Create" });
        if (pin == null) return;
      }
      try {
        const res = await call("CreateRoom", { name: profile.name, avatar: profile.avatar, color: profile.color, pin });
        saveSession(res);
      } catch { }
    });

    if (roomFromUrl && profile.name.trim() && !session) setTimeout(() => $("#code", view)?.focus(), 100);
    if (F("publicRooms")) { this.loadRooms(view); this.timer = setInterval(() => this.loadRooms(view), 5000); }
  },
  unmount() { clearInterval(this.timer); },
  async loadRooms(view) {
    try {
      const rooms = await (await fetch("api/rooms")).json();
      const list = $("#rooms", view); if (!list) return;
      $("#rooms-count", view).textContent = rooms.length ? `${rooms.length} open` : "";
      list.innerHTML = rooms.length ? rooms.map(r => html`
        <button class="room-item" data-code="${esc(r.code)}">
          <span class="code">${esc(r.code)}</span>
          <span class="grow"><b>${esc(r.name || (r.host ? r.host + "'s room" : "Room"))}</b><br><span class="small muted">${esc(r.mode)} · ${r.players}/${r.max} players</span></span>
          ${r.locked ? "🔒" : ""}
          <span class="tag ${r.phase === "lobby" ? "green" : "orange"}">${r.phase === "lobby" ? "Waiting" : "Playing"}</span>
        </button>`).join("") : `<p class="muted small">No open rooms right now. Create one and tick “Show in open rooms”.</p>`;
      $$(".room-item", list).forEach(b => b.addEventListener("click", () => { $("#code", view).value = b.dataset.code; $("#join-form", view).requestSubmit(); }));
    } catch { }
  },
};

async function joinRoom(code, spectate, password) {
  try {
    const res = await call("JoinRoom", {
      code, name: profile.name, avatar: profile.avatar, color: profile.color, spectate: !!spectate, password,
      token: session?.code === code ? session.token : null,
    });
    saveSession(res);
  } catch (e) {
    if (e.message === "PASSWORD_REQUIRED") {
      const pw = await ask("This room is locked 🔒", { type: "password", placeholder: "Password", ok: "Join" });
      if (pw != null) await joinRoom(code, spectate, pw);
    }
  }
}

// ============================================================ LOBBY
const LobbyScreen = {
  key: s => "lobby:" + s.code + ":" + (s.you?.isHost ? "host" : "guest"),
  mount(view, s) {
    const url = shareUrl(s.code);
    view.innerHTML = html`
      <div class="grid-2">
        <div class="stack">
          <div class="card tape">
            <div class="row" style="align-items:flex-start;flex-wrap:nowrap;gap:14px">
              <div style="flex:1;min-width:0">
                <div class="muted small" style="font-weight:700">ROOM CODE</div>
                <div class="room-code-big" id="code-big" title="Tap to copy the invite link">${esc(s.code)}</div>
                <div class="small muted" id="room-name" style="margin:4px 0 10px"></div>
                <div class="row">
                  <button class="btn sm" id="copy">🔗 Copy invite link</button>
                  ${navigator.share ? `<button class="btn sm teal" id="share">📤 Share</button>` : ""}
                </div>
                ${config.viaIngress && !config.publicUrl ? `<p class="small muted" style="margin-top:8px">⚠️ You're inside Home Assistant. Set <b>public_url</b> in the app options so the invite link works for people outside your house.</p>` : ""}
              </div>
              <div class="qr" id="qr" ${F("qrCode") ? "" : "hidden"}>${F("qrCode") ? qrSvg(url) : ""}</div>
            </div>
          </div>
          <div class="card">
            <div class="row between"><h2 style="margin:0">Players <span class="muted small" id="pcount"></span></h2>
              <div class="row">
                <button class="btn sm ghost" id="spectate-btn" hidden></button>
                <button class="btn sm ghost" id="leave">🚪 Leave</button>
              </div>
            </div>
            <div class="players" id="players" style="margin-top:12px"></div>
            <div class="row" style="margin-top:10px" id="bot-row" hidden><button class="btn sm ghost" id="add-bot">🤖 Add a computer player</button></div>
          </div>
        </div>
        <div class="stack">
          <div class="card" id="settings-card"></div>
        </div>
      </div>
      <div class="start-bar" id="start-bar"></div>`;

    const copy = async () => {
      try { await navigator.clipboard.writeText(url); toast("Invite link copied! 📋", "ok"); }
      catch { ask("Copy this link", { placeholder: url }); }
    };
    $("#copy", view).addEventListener("click", copy);
    $("#code-big", view).addEventListener("click", copy);
    $("#share", view)?.addEventListener("click", () => navigator.share({ title: "Play Consequences with me!", text: `Join my Consequences room: ${s.code}`, url }).catch(() => { }));
    $("#leave", view).addEventListener("click", async () => { if (await confirmBox("Leave the room?", "You can rejoin with the code.", "Leave")) { await call("Leave").catch(() => { }); leaveLocal(); } });
    $("#spectate-btn", view).addEventListener("click", () => call("ToggleSpectate").catch(() => { }));
    $("#add-bot", view).addEventListener("click", () => call("AddBot").catch(() => { }));

    if (s.you?.isHost) SettingsPanel.mount($("#settings-card", view));
    this.qrUrl = url;
  },
  update(view, s) {
    $("#room-name", view).textContent = s.settings.roomName ? "“" + s.settings.roomName + "”" : (s.settings.hasPassword ? "🔒 Password protected" : "");
    const qr = $("#qr", view);
    qr.hidden = !F("qrCode");
    if (F("qrCode") && !qr.innerHTML) qr.innerHTML = qrSvg(this.qrUrl);

    const active = s.players.filter(p => !p.isSpectator);
    $("#pcount", view).textContent = `(${active.length}/${s.settings.maxPlayers})`;
    $("#players", view).innerHTML = s.players.map(p => html`
      <div class="player ${p.connected ? "" : "off"}">
        ${avatarHtml(p)}
        <div class="nm">${esc(p.name)} ${p.id === meId() ? '<span class="tag">you</span>' : ""}</div>
        ${p.score ? `<span class="tag">⭐ ${p.score}</span>` : ""}
        ${p.isHost ? '<span class="status" title="Host">👑</span>' : ""}
        ${p.isBot ? '<span class="status" title="Computer player">🤖</span>' : ""}
        ${p.isSpectator ? '<span class="status" title="Watching">🍿</span>' : ""}
        ${!p.connected ? '<span class="status" title="Disconnected">📵</span>' : ""}
        ${isHost() && p.id !== meId() ? `<button class="menu-btn" data-id="${p.id}" aria-label="Player options">⋮</button>` : ""}
      </div>`).join("");
    $$(".menu-btn", view).forEach(b => b.addEventListener("click", () => playerMenu(playerById(b.dataset.id))));

    const sb = $("#spectate-btn", view);
    sb.hidden = !F("spectators");
    sb.textContent = s.you?.isSpectator ? "🙋 Join in" : "🍿 Just watch";
    $("#bot-row", view).hidden = !(isHost() && F("bots"));

    if (isHost()) SettingsPanel.update(s);
    else $("#settings-card", view).innerHTML = settingsSummary(s);

    const canStart = active.length >= 2;
    $("#start-bar", view).innerHTML = isHost()
      ? `<button class="btn primary big block" id="start" ${canStart ? "" : "disabled"}>${canStart ? "▶️ Start the game!" : "Waiting for at least 2 players…"}</button>`
      : `<div class="card center" style="padding:12px"><b>Waiting for ${esc(playerById(s.hostId)?.name || "the host")} to start…</b> <span class="muted small">${s.you?.isSpectator ? "(you're watching)" : ""}</span></div>`;
    $("#start", view)?.addEventListener("click", () => call("StartGame").catch(() => { }));
  },
};

function playerMenu(p) {
  if (!p) return;
  const m = modal(html`<h2>${esc(p.avatar)} ${esc(p.name)}</h2>
    <div class="stack">
      ${!p.isBot ? `<button class="btn block teal" data-a="host">👑 Make host</button>` : ""}
      <button class="btn block primary" data-a="kick">${p.isBot ? "🤖 Remove bot" : "👢 Kick out"}</button>
      <button class="btn block ghost" data-close>Cancel</button>
    </div>`);
  m.root.addEventListener("click", async e => {
    const a = e.target.closest("[data-a]")?.dataset.a; if (!a) return;
    m.close();
    if (a === "host") call("MakeHost", p.id).catch(() => { });
    if (a === "kick" && (p.isBot || await confirmBox("Kick " + p.name + "?", "They won't be able to rejoin with their old seat.", "Kick"))) call("Kick", p.id).catch(() => { });
  });
}

function templateName(s) {
  if (s.settings.templateId === "custom") return "Custom prompts";
  return config.templates.find(t => t.id === s.settings.templateId)?.name || s.settings.templateId;
}

function settingsSummary(s) {
  const t = config.templates.find(t => t.id === s.settings.templateId);
  const on = config.catalog.filter(f => f.scope === "room" && s.features[f.key]);
  return html`<h2>This game</h2>
    <p><b style="font-family:var(--display);font-size:1.4rem">${esc(templateName(s))}</b><br><span class="muted small">${esc(t?.description || (s.settings.customSteps.length + " custom folds"))}</span></p>
    ${s.features.timer ? `<p class="small">⏱️ ${s.settings.textSeconds}s to write · ${s.settings.drawSeconds}s to draw</p>` : ""}
    <h3>Switched on</h3>
    <div class="row small">${on.map(f => `<span class="tag" title="${esc(f.description)}">${f.icon} ${esc(f.label)}</span>`).join("")}</div>
    <p class="small muted" style="margin-top:14px">Only the host can change these.</p>`;
}

// ------------------------------------------------------------ host settings
const SettingsPanel = {
  mount(card) {
    this.card = card;
    card.innerHTML = html`
      <h2>Game settings</h2>
      <label class="field"><span>Room name (optional)</span><input type="text" id="s-name" maxlength="30" placeholder="e.g. Christmas at Nan's"></label>
      <div class="field"><span style="font-weight:600;font-size:.9rem">Game mode</span><div class="mode-cards" id="s-modes" style="margin-top:6px"></div></div>
      <div id="s-custom" hidden>
        <h3>Your prompts</h3>
        <p class="small muted">Each row is one fold. “Connective” is what's read out before the answer (e.g. “met”, “at”, “He said”).</p>
        <div id="s-steps"></div>
        <div class="row"><button class="btn sm ghost" id="s-add">➕ Add a fold</button><button class="btn sm ghost" id="s-copy">📋 Start from Classic</button><button class="btn sm teal" id="s-save">💾 Save prompts</button></div>
      </div>
      <div id="s-timers" class="row" style="margin-top:6px">
        <label class="field" style="flex:1;min-width:130px"><span>⏱️ Seconds to write</span><input type="number" id="s-text" min="10" max="900" step="5"></label>
        <label class="field" style="flex:1;min-width:130px"><span>🎨 Seconds to draw</span><input type="number" id="s-draw" min="10" max="1800" step="10"></label>
      </div>
      <div class="row">
        <label class="field" id="s-peek-wrap" style="flex:1;min-width:130px"><span>👀 Words that peek over the fold</span><input type="number" id="s-peek" min="1" max="8"></label>
        <label class="field" style="flex:1;min-width:130px"><span>👥 Max players</span><input type="number" id="s-max" min="2"></label>
      </div>
      <label class="switch" id="s-public-wrap"><input type="checkbox" id="s-public"><span class="knob"></span><span class="txt"><b>🚪 Show in open rooms</b><small>Anyone on the home screen can see and join.</small></span></label>
      <label class="field" id="s-pass-wrap" style="margin-top:8px"><span>🔒 Room password (optional)</span><input type="text" id="s-pass" maxlength="30" placeholder="Leave empty for no password" autocomplete="off"></label>
      <details class="settings" open><summary>Features in this room</summary><div class="feature-grid" id="s-features"></div></details>`;

    const send = patch => call("UpdateSettings", patch).catch(() => { });
    const debounce = (fn, ms = 500) => { let t; return (...a) => { clearTimeout(t); t = setTimeout(() => fn(...a), ms); }; };
    const sendName = debounce(v => send({ roomName: v }));
    $("#s-name", card).addEventListener("input", e => sendName(e.target.value));
    const sendPass = debounce(v => send({ password: v }), 700);
    $("#s-pass", card).addEventListener("input", e => sendPass(e.target.value));
    $("#s-text", card).addEventListener("change", e => send({ textSeconds: +e.target.value }));
    $("#s-draw", card).addEventListener("change", e => send({ drawSeconds: +e.target.value }));
    $("#s-peek", card).addEventListener("change", e => send({ peekWords: +e.target.value }));
    $("#s-max", card).addEventListener("change", e => send({ maxPlayers: +e.target.value }));
    $("#s-public", card).addEventListener("change", e => send({ isPublic: e.target.checked }));
    $("#s-modes", card).addEventListener("click", e => { const b = e.target.closest("button"); if (b && !b.disabled) send({ templateId: b.dataset.id }); });
    $("#s-features", card).addEventListener("change", e => { const i = e.target.closest("input[data-key]"); if (i) send({ features: { [i.dataset.key]: i.checked } }); });

    const stepsEl = $("#s-steps", card);
    const addRow = (st = {}) => stepsEl.appendChild(el(html`<div class="custom-step">
        <input type="text" class="p" maxlength="120" placeholder="Prompt, e.g. Name a famous villain" value="${esc(st.prompt || "")}">
        <input type="text" class="c" maxlength="40" placeholder="Connective" value="${esc(st.prefix || "")}">
        <label class="small" title="Players can draw on this fold"><input type="checkbox" class="d" ${st.drawable ? "checked" : ""}> 🎨</label>
        <button class="btn sm ghost rm" type="button" aria-label="Remove fold">✕</button></div>`));
    stepsEl.addEventListener("click", e => { if (e.target.closest(".rm")) e.target.closest(".custom-step").remove(); });
    $("#s-add", card).addEventListener("click", () => addRow());
    $("#s-copy", card).addEventListener("click", () => { stepsEl.innerHTML = ""; config.templates.find(t => t.id === "classic").steps.forEach(addRow); });
    $("#s-save", card).addEventListener("click", () => {
      const steps = $$(".custom-step", stepsEl).map(r => ({ prompt: $(".p", r).value, prefix: $(".c", r).value, drawable: $(".d", r).checked, suffix: "" })).filter(x => x.prompt.trim());
      if (steps.length < 2) { toast("Add at least 2 folds", "err"); return; }
      send({ customSteps: steps, templateId: "custom" }).then(() => toast("Prompts saved ✔️", "ok"));
    });
    this.addRow = addRow;
    this.stepsLoaded = false;
  },
  update(s) {
    const card = this.card, st = s.settings, sf = s.serverFeatures;
    const setVal = (id, v) => { const i = $(id, card); if (i && document.activeElement !== i) { if (i.type === "checkbox") i.checked = v; else i.value = v; } };
    setVal("#s-name", st.roomName);
    setVal("#s-text", st.textSeconds); setVal("#s-draw", st.drawSeconds);
    setVal("#s-peek", st.peekWords);
    setVal("#s-max", st.maxPlayers); $("#s-max", card).max = st.serverMaxPlayers;
    setVal("#s-public", st.isPublic); setVal("#s-pass", st.password || "");
    $("#s-timers", card).hidden = !s.features.timer;
    $("#s-peek-wrap", card).hidden = !s.features.peek;
    $("#s-public-wrap", card).hidden = !sf.publicRooms;
    $("#s-pass-wrap", card).hidden = !sf.roomPasswords;

    const modes = config.templates.map(t => ({ ...t, disabled: t.pictureMode && !(s.features.pictureMode && s.features.drawing) }));
    if (s.features.customTemplates) modes.push({ id: "custom", name: "Custom", description: "Write your own folds.", folds: st.customSteps.length });
    $("#s-modes", card).innerHTML = modes.map(t => html`<button type="button" class="mode-card ${st.templateId === t.id ? "sel" : ""}" data-id="${t.id}" ${t.disabled ? "disabled" : ""}><b>${esc(t.name)}</b><small>${esc(t.description)}</small><small style="margin-top:4px"><i>${t.folds} folds</i></small></button>`).join("");

    const custom = st.templateId === "custom" && s.features.customTemplates;
    $("#s-custom", card).hidden = !custom;
    if (custom && !this.stepsLoaded) {
      $("#s-steps", card).innerHTML = "";
      (st.customSteps.length ? st.customSteps : [{}, {}, {}]).forEach(this.addRow);
      this.stepsLoaded = true;
    }

    const featEl = $("#s-features", card);
    if (!featEl.contains(document.activeElement)) {
      featEl.innerHTML = config.catalog.filter(f => f.scope === "room").map(f => {
        const locked = !sf[f.key];
        return html`<label class="switch ${locked ? "locked" : ""}" title="${locked ? "Switched off by the server admin" : ""}">
          <input type="checkbox" data-key="${f.key}" ${!locked && st.roomFeatures[f.key] ? "checked" : ""} ${locked ? "disabled" : ""}><span class="knob"></span>
          <span class="txt"><b>${f.icon} ${esc(f.label)}</b><small>${locked ? "Switched off by the server admin" : esc(f.description)}</small></span></label>`;
      }).join("");
    }
  },
};

// ============================================================ WRITING
function gameHead(s) {
  const pct = (s.game.round / s.game.totalRounds) * 100;
  return html`<div class="game-head">
    <div style="flex:1"><div class="row between small" style="font-weight:700;margin-bottom:4px"><span>Fold ${s.game.round + 1} of ${s.game.totalRounds}</span><span class="muted">${esc(s.game.templateName)}</span></div>
    <div class="progress"><div style="width:${pct}%"></div></div></div>
    ${s.deadline ? `<div class="timer"><span>…</span></div>` : ""}
  </div>`;
}

const WriteScreen = {
  key: s => `write:${s.code}:${s.game.round}`,
  mount(view, s) {
    const t = s.task;
    this.submitting = false;
    this.sketch = null;
    const isDrawing = t.kind === "drawing";
    const strips = Math.min(t.foldsBelow, 10);
    view.innerHTML = html`
      ${gameHead(s)}
      <div class="folds-stack">${Array.from({ length: strips }, () => `<div class="fold-strip"></div>`).join("")}</div>
      <div class="paper-wrap"><div class="paper" id="paper">
        ${t.foldsBelow > 0 ? `<div class="fold-edge"><span class="lbl">${t.foldsBelow} fold${t.foldsBelow > 1 ? "s" : ""} hidden above · paper ${t.paperNumber}, started by ${esc(t.starterName || "?")}</span>${t.peekText ? `<span>${esc(t.peekText)}</span>` : ""}</div>` : `<div class="fold-edge"><span class="lbl">A fresh sheet of paper · paper ${t.paperNumber}</span></div>`}
        <div class="prompt">${esc(t.prompt)}</div>
        ${t.prefix && !isDrawing ? `<div class="connective">…${esc(t.prefix.replace(/[“"]\s*$/, ""))}</div>` : ""}
        ${!isDrawing ? `<textarea id="answer" rows="2" maxlength="200" placeholder="${esc(t.placeholder || "Write here…")}" aria-label="Your answer" enterkeyhint="send"></textarea><div class="charcount" id="cc"></div>` : ""}
        <div id="sketch-host"></div>
        <div class="paper-actions">
          ${!isDrawing && F("inspire") ? `<button class="btn sm ghost" id="inspire" type="button">🎲 Inspire me</button>` : ""}
          ${!isDrawing && t.canDraw ? `<button class="btn sm ghost" id="draw-toggle" type="button">🎨 ${esc(t.drawHint || "Add a drawing")}</button>` : ""}
        </div>
        <button class="btn primary big block" id="fold" style="margin-top:14px">Fold & pass it on ➜</button>
      </div></div>`;

    const answer = $("#answer", view);
    if (answer) {
      setTimeout(() => { if (!("ontouchstart" in window)) answer.focus(); }, 80);
      answer.addEventListener("input", () => { $("#cc", view).textContent = answer.value.length > 150 ? `${answer.value.length}/200` : ""; });
      answer.addEventListener("keydown", e => { if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); this.submit(); } });
    }
    $("#inspire", view)?.addEventListener("click", async () => {
      try { const idea = await call("Inspire"); answer.value = idea; answer.focus(); sfx("pop"); } catch { }
    });
    const openSketch = () => {
      const picture = s.game.pictureMode && isDrawing;
      this.sketch = new Sketch($("#sketch-host", view), {
        width: 800, height: picture ? 400 : 600,
        guideImage: t.peekImage, foldGuide: picture && s.game.round < s.game.totalRounds - 2,
      });
    };
    if (isDrawing) openSketch();
    $("#draw-toggle", view)?.addEventListener("click", e => {
      if (this.sketch) { this.sketch = null; $("#sketch-host", view).innerHTML = ""; e.target.textContent = "🎨 " + (t.drawHint || "Add a drawing"); }
      else { openSketch(); e.target.textContent = "✕ Remove drawing"; }
    });
    $("#fold", view).addEventListener("click", () => this.submit());
  },
  hasContent() {
    return !!($("#answer")?.value.trim() || this.sketch?.dirty);
  },
  autoSubmit() {
    if (this.submitting || this.autoTried === current.key) return;
    this.autoTried = current.key;
    if (this.hasContent()) this.submit(true);
  },
  async submit(auto = false) {
    if (this.submitting) return;
    const text = $("#answer")?.value.trim() || "";
    const image = this.sketch?.dirty ? this.sketch.toDataUrl() : null;
    const kind = state.task?.kind;
    if (!auto && kind === "drawing" && !image) { toast("Draw something first! 🎨", "err"); return; }
    if (!auto && kind === "text" && !text && !image) { toast("Write something first! ✍️", "err"); $("#answer")?.focus(); return; }
    this.submitting = true;
    const paper = $("#paper");
    $("#fold").disabled = true;
    sfx("fold");
    paper.classList.add("folding");
    try { await call("Submit", text, image); }
    catch { paper.classList.remove("folding"); $("#fold").disabled = false; this.submitting = false; }
  },
};

const WaitScreen = {
  key: s => `wait:${s.code}:${s.game.round}:${s.you?.isSpectator ? "spec" : "p"}`,
  mount(view, s) {
    const watching = !s.players.find(p => p.id === meId())?.seated;
    view.innerHTML = html`
      ${gameHead(s)}
      <div class="card center" style="max-width:720px;margin:0 auto">
        <div class="big-emoji">${watching ? "🍿" : "📨"}</div>
        <h2>${watching ? "You're watching this one" : "Folded and passed!"}</h2>
        <p class="muted">${watching ? (s.you?.lateJoiner ? "You'll be dealt in when the next game starts." : "Sit back and enjoy the show.") : "Waiting for everyone else to finish their fold…"}</p>
        <div class="waiting-grid" id="wg"></div>
        <div id="host-tools" style="margin-top:18px"></div>
      </div>`;
  },
  update(view, s) {
    const seated = s.players.filter(p => p.seated);
    $("#wg", view).innerHTML = seated.map(p => html`<div class="w ${p.submitted ? "done" : "writing"}">${avatarHtml(p)}<span>${esc(p.name)}${!p.connected && !p.isBot ? " 📵" : ""}</span></div>`).join("");
    const waitingOn = seated.filter(p => !p.submitted).length;
    $("#host-tools", view).innerHTML = isHost() && waitingOn > 0
      ? `<button class="btn ghost" id="skip">⏭️ Skip the slowcoaches (${waitingOn})</button><p class="small muted">${F("autoFill") ? "Their folds get a random silly answer." : "Their folds are left blank."}</p>` : "";
    $("#skip", view)?.addEventListener("click", async () => { if (await confirmBox("Skip waiting?", "Anyone still writing gets auto-filled.", "Skip")) call("SkipWaiting").catch(() => { }); });
  },
};

// ============================================================ REVEAL
function entryHtml(e, { picture, canStar }) {
  const text = e.text != null ? e.text + (e.suffix || "") : null;
  const prefix = (e.prefix || "").replace(/[“"]\s*$/, "").trim();
  const openQuote = /[“"]\s*$/.test(e.prefix || "") && text ? "“" : "";
  const starBtn = F("stars") && e.id && !e.autoFilled
    ? `<button class="star-btn ${e.starredByMe ? "on" : ""}" data-star="${e.id}" ${!canStar || e.mine ? "disabled" : ""} title="${e.mine ? "That's yours!" : "Star this"}">⭐ <span>${e.stars || ""}</span></button>` : "";
  const meta = (e.author || starBtn) ? `<div class="meta">${e.author ? "✍️ " + esc(e.author) : ""}${starBtn}</div>` : "";
  if (picture && e.kind === "drawing") {
    return `<div class="entry" data-id="${e.id || ""}">${e.image ? `<img src="${esc(e.image)}" alt="Drawing by ${esc(e.author || "someone")}">` : `<div class="txt auto">(this bit was scribbled out)</div>`}${meta}</div>`;
  }
  let body;
  if (text == null && !e.image) body = `<span class="txt auto">${e.kind === "drawing" ? "(scribbled out)" : "(left blank)"}</span>`;
  else body = text != null ? `<span class="txt ${e.autoFilled ? "auto" : ""}">${openQuote}${esc(text)}</span>` : "";
  return `<div class="entry" data-id="${e.id || ""}">${prefix ? `<span class="connective">${esc(prefix)}</span> ` : ""}${body}${e.image ? `<img src="${esc(e.image)}" alt="Drawing by ${esc(e.author || "someone")}" loading="lazy">` : ""}${meta}</div>`;
}

function spokenText(e) {
  return [(e.prefix || "").replace(/[“"]/g, ""), e.text || (e.image ? "a drawing" : "")].join(" ").trim();
}

function bindStars(root) {
  root.addEventListener("click", e => {
    const b = e.target.closest("[data-star]");
    if (b && !b.disabled) { call("Star", b.dataset.star).catch(() => { }); sfx("pop"); }
  });
}

function updateStars(root, entries) {
  for (const e of entries) {
    if (!e.id) continue;
    const b = root.querySelector(`[data-star="${e.id}"]`);
    if (!b) continue;
    b.classList.toggle("on", e.starredByMe);
    b.querySelector("span").textContent = e.stars || "";
  }
}

function paperTools(p, s) {
  return html`<div class="paper-tools">
    ${F("hallOfFame") ? `<button class="btn sm gold" data-hof="${p.index}" ${p.saved ? "disabled" : ""}>${p.saved ? "🏆 In the Hall of Fame" : "🏆 Save to Hall of Fame"}</button>` : ""}
    ${F("exportImage") ? `<button class="btn sm ghost" data-export="${p.index}">🖼️ Save as image</button>` : ""}
  </div>`;
}

function bindPaperTools(root, getPaper) {
  root.addEventListener("click", e => {
    const h = e.target.closest("[data-hof]");
    if (h && !h.disabled) { h.disabled = true; call("SaveToHallOfFame", +h.dataset.hof).then(() => toast("Saved to the Hall of Fame! 🏆", "ok")).catch(() => { h.disabled = false; }); }
    const x = e.target.closest("[data-export]");
    if (x) exportPaper(getPaper(+x.dataset.export), state.game.pictureMode);
  });
}

const RevealScreen = {
  key: s => `reveal:${s.code}:${s.reveal.paperIndex}`,
  mount(view, s) {
    const r = s.reveal;
    this.rendered = 0;
    view.innerHTML = html`
      <div style="max-width:720px;margin:0 auto">
        <div class="row between" style="margin-bottom:8px"><h2 style="font-family:var(--display);font-weight:400;margin:0;font-size:1.8rem">Paper ${r.paperIndex + 1} of ${r.paperCount}</h2>
          <button class="icon-btn" id="speak-btn" title="Read aloud on this device" ${F("readAloud") ? "" : "hidden"}>${speakOn ? "🗣️" : "🤐"}</button></div>
        <div class="reader-banner" id="reader"></div>
        <div class="paper reveal-paper ${s.game.pictureMode ? "picture" : ""}" id="rpaper"><div id="entries"></div><div class="folded-rest" id="rest"></div></div>
        <div id="tools"></div>
        <div class="reactions" id="reactions" ${F("reactions") ? "" : "hidden"}>${config.reactions.map(e => `<button data-r="${e}" aria-label="React ${e}">${e}</button>`).join("")}</div>
      </div>
      <div class="reveal-controls" id="controls"></div>`;
    bindStars($("#rpaper", view));
    bindPaperTools($("#tools", view), () => state.reveal.paper);
    $("#reactions", view).addEventListener("click", e => { const b = e.target.closest("[data-r]"); if (b) call("React", b.dataset.r).catch(() => { }); });
    $("#speak-btn", view).addEventListener("click", e => { speakOn = !speakOn; store.set("speak", speakOn); e.target.textContent = speakOn ? "🗣️" : "🤐"; if (!speakOn) speechSynthesis?.cancel(); });
    this.first = true;
  },
  update(view, s) {
    const r = s.reveal, p = r.paper;
    $("#reader", view).innerHTML = r.readerId === meId() ? "🎙️ <b>You're reading this one!</b> Read each fold out loud as you unfold it." : `🎙️ <b>${esc(r.readerName)}</b> is unfolding this paper…`;
    const entriesEl = $("#entries", view);
    for (let i = this.rendered; i < p.entries.length; i++) {
      const e = p.entries[i];
      entriesEl.appendChild(el(entryHtml(e, { picture: s.game.pictureMode, canStar: true })));
      if (!this.first) speak(spokenText(e));
    }
    this.rendered = p.entries.length;
    this.first = false;
    updateStars(entriesEl, p.entries);

    const left = p.total - p.unfolded;
    $("#rest", view).innerHTML = left > 0 ? `<div class="small muted" style="font-family:var(--ui);line-height:1.6">${left} fold${left > 1 ? "s" : ""} still hidden…</div>` + Array.from({ length: Math.min(left, 12) }, () => `<div class="fold-strip"></div>`).join("") : "";
    $("#tools", view).innerHTML = left === 0 ? paperTools(p, s) : "";

    const last = r.paperIndex === r.paperCount - 1;
    const c = $("#controls", view);
    if (r.canControl) {
      c.innerHTML = html`
        ${r.paperIndex > 0 ? `<button class="btn ghost" data-c="prev">◀</button>` : ""}
        ${left > 0 ? `<button class="btn primary big" data-c="unfold">📜 Unfold</button><button class="btn ghost" data-c="all">Unfold all</button>` : ""}
        ${left === 0 && !last ? `<button class="btn teal big" data-c="next">Next paper ▶</button>` : ""}
        ${left === 0 && last ? `<button class="btn gold big" data-c="finish">🎉 See the results</button>` : ""}`;
      c.onclick = e => {
        const a = e.target.closest("[data-c]")?.dataset.c;
        if (a === "unfold") call("Unfold", false).catch(() => { });
        if (a === "all") call("Unfold", true).catch(() => { });
        if (a === "next") call("NextPaper").catch(() => { });
        if (a === "prev") call("PrevPaper").catch(() => { });
        if (a === "finish") call("FinishReveal").catch(() => { });
      };
    } else c.innerHTML = `<span class="muted small" style="padding:8px">Waiting for ${esc(r.readerName)} to ${left > 0 ? "unfold" : "move on"}…</span>`;
  },
};

// ============================================================ RESULTS
const ResultsScreen = {
  key: s => `results:${s.code}`,
  mount(view, s) {
    view.innerHTML = html`
      <div class="center"><div class="big-emoji">🎉</div><h1 style="font-family:var(--display);font-weight:400;font-size:2.6rem;margin:0">That's a wrap!</h1></div>
      <div id="awards" class="awards" style="margin:18px 0"></div>
      <div class="grid-2">
        <div class="paper-list" id="papers"></div>
        <div class="card" id="board"></div>
      </div>
      <div class="start-bar" id="again"></div>`;
    const papers = $("#papers", view);
    papers.innerHTML = s.results.papers.map(p => html`<div><div class="small muted" style="font-weight:700;margin-bottom:6px">PAPER ${p.index + 1} · started by ${esc(p.starter || "?")} ${p.stars ? "· ⭐ " + p.stars : ""}</div>
      <div class="paper reveal-paper ${s.game.pictureMode ? "picture" : ""}" data-paper="${p.index}">${p.entries.map(e => entryHtml(e, { picture: s.game.pictureMode, canStar: true })).join("")}</div>
      <div data-tools="${p.index}">${paperTools(p, s)}</div></div>`).join("");
    bindStars(papers);
    bindPaperTools(papers, i => state.results.papers[i]);
  },
  update(view, s) {
    const a = s.results.awards;
    const cards = [];
    if (a.bestPaper) cards.push(`<div class="award" style="--r:-2deg"><div class="medal">🏆</div><h4>Best story</h4><div>Paper ${a.bestPaper.index + 1} (started by ${esc(a.bestPaper.starter)})</div><div class="small muted">⭐ ${a.bestPaper.stars}</div></div>`);
    if (a.bestLine) cards.push(`<div class="award" style="--r:1.5deg"><div class="medal">😂</div><h4>Funniest line</h4><div class="quote">${esc(a.bestLine.text)}</div><div class="small muted">by ${esc(a.bestLine.author)} · ⭐ ${a.bestLine.stars}</div></div>`);
    if (a.bestDrawing) cards.push(`<div class="award" style="--r:-1deg"><div class="medal">🎨</div><h4>Best artist</h4><img src="${esc(a.bestDrawing.image)}" alt=""><div class="small muted">by ${esc(a.bestDrawing.author)} · ⭐ ${a.bestDrawing.stars}</div></div>`);
    if (a.starPlayer) cards.push(`<div class="award" style="--r:2deg"><div class="medal">🌟</div><h4>Star of the show</h4><div style="font-size:1.3rem">${esc(a.starPlayer.name)}</div><div class="small muted">⭐ ${a.starPlayer.stars} this game</div></div>`);
    $("#awards", view).innerHTML = F("stars") ? (cards.join("") || `<p class="center muted" style="grid-column:1/-1">No stars handed out. Tap ⭐ on your favourite lines to give awards!</p>`) : "";

    const board = s.players.filter(p => p.seated || p.score).sort((x, y) => y.score - x.score);
    $("#board", view).innerHTML = F("stars") ? html`<h2>Scoreboard</h2>${board.map((p, i) => html`<div class="leader"><span class="pos">${["🥇", "🥈", "🥉"][i] || i + 1}</span>${avatarHtml(p).replace('class="av"', 'class="av" style="width:34px;height:34px;font-size:1.1rem;border:3px solid ' + esc(p.color) + ';border-radius:50%;display:grid;place-items:center;background:var(--paper)"')}<b style="flex:1">${esc(p.name)}</b><span class="small muted">+${p.gameStars}</span><b>⭐ ${p.score}</b></div>`).join("")}<p class="small muted">You can still star lines on the left.</p>`
      : `<h2>Thanks for playing!</h2><p class="muted">Scroll through all the papers on the left.</p>`;

    const papers = $("#papers", view);
    for (const p of s.results.papers) {
      updateStars(papers, p.entries);
      const tools = $(`[data-tools="${p.index}"]`, papers);
      const h = tools && $("[data-hof]", tools);
      if (h && p.saved) { h.disabled = true; h.textContent = "🏆 In the Hall of Fame"; }
    }
    $("#again", view).innerHTML = isHost()
      ? `<button class="btn primary big block" id="again-btn">🔁 Back to the lobby for another round</button>`
      : `<div class="card center" style="padding:12px"><b>Waiting for ${esc(playerById(s.hostId)?.name || "the host")} to start another round…</b></div>`;
    $("#again-btn", view)?.addEventListener("click", () => call("BackToLobby").catch(() => { }));
  },
};

// ============================================================ export a paper as a picture
async function exportPaper(paper, picture, title = "Consequences") {
  if (!paper) return;
  const W = 900, pad = 50, margin = 90, lineH = 44;
  const c = document.createElement("canvas"), ctx = c.getContext("2d");
  await document.fonts?.load("30px 'Patrick Hand'").catch(() => { });
  const loadImg = src => new Promise(res => { const i = new Image(); i.onload = () => res(i); i.onerror = () => res(null); i.src = src; });

  // lay everything out first, then paint
  ctx.font = "30px 'Patrick Hand', cursive";
  const items = [];
  for (const e of paper.entries) {
    const prefix = (e.prefix || "").replace(/[“"]\s*$/, "").trim();
    const quote = /[“"]\s*$/.test(e.prefix || "") ? "“" : "";
    const words = ((prefix ? prefix + " " : "") + (e.text != null ? quote + e.text + (e.suffix || "") : (e.image ? "" : "(blank)"))).split(" ").filter(Boolean);
    const lines = []; let line = "";
    for (const w of words) {
      const test = line ? line + " " + w : w;
      if (ctx.measureText(test).width > W - margin - pad && line) { lines.push(line); line = w; } else line = test;
    }
    if (line) lines.push(line);
    const img = e.image ? await loadImg(e.image) : null;
    const imgH = img ? (picture ? (W - margin - pad) * img.height / img.width : Math.min(320, (W - margin - pad) * img.height / img.width)) : 0;
    items.push({ lines, img, imgH, author: e.author });
  }
  const H = pad + 70 + items.reduce((h, it) => h + it.lines.length * lineH + (it.img ? it.imgH + (picture ? 0 : 16) : 0) + (picture ? 0 : 10), 0) + pad + 30;
  c.width = W; c.height = Math.max(400, H);

  ctx.fillStyle = "#fffdf7"; ctx.fillRect(0, 0, W, c.height);
  ctx.strokeStyle = "#c9dcef"; ctx.lineWidth = 1.5;
  for (let y = pad + 80; y < c.height; y += lineH) { ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(W, y); ctx.stroke(); }
  ctx.strokeStyle = "#f0a8a8"; ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(margin - 20, 0); ctx.lineTo(margin - 20, c.height); ctx.stroke();
  ctx.fillStyle = "#e4572e"; ctx.font = "44px 'Gochi Hand', 'Patrick Hand', cursive"; ctx.fillText(title, margin, pad + 30);

  let y = pad + 80;
  for (const it of items) {
    ctx.font = "30px 'Patrick Hand', cursive"; ctx.fillStyle = "#1b3a8a";
    for (const l of it.lines) { ctx.fillText(l, margin, y + 32); y += lineH; }
    if (it.img) {
      const w = (W - margin - pad) * (it.imgH / ((W - margin - pad) * it.img.height / it.img.width));
      ctx.drawImage(it.img, margin, y + (picture ? 0 : 6), w, it.imgH);
      y += it.imgH + (picture ? 0 : 16);
    }
    if (!picture) y += 10;
  }
  ctx.font = "16px system-ui, sans-serif"; ctx.fillStyle = "#8a8f98"; ctx.fillText("Played on Consequences", margin, c.height - 20);

  c.toBlob(async blob => {
    const file = new File([blob], `consequences-paper-${paper.index + 1}.png`, { type: "image/png" });
    if (navigator.canShare?.({ files: [file] })) { try { await navigator.share({ files: [file], title: "Our Consequences story" }); return; } catch { } }
    const a = document.createElement("a"); a.href = URL.createObjectURL(blob); a.download = file.name; a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 5000);
  }, "image/png");
}

// ============================================================ reactions
function floatEmoji(r) {
  const f = el(`<div class="floaty" style="left:${10 + Math.random() * 80}vw">${esc(r.emoji)}<small style="color:${esc(r.color)}">${esc(r.from)}</small></div>`);
  document.body.appendChild(f);
  setTimeout(() => f.remove(), 2700);
}

// ============================================================ chat
let chatSeen = 0, chatOpen = false;
function updateChat() {
  const show = !!state && F("chat");
  $("#chat-fab").hidden = !show || chatOpen;
  $("#chat").hidden = !show || !chatOpen;
  if (!state) return;
  const msgs = state.chat || [];
  const box = $("#chat-msgs");
  const atBottom = box.scrollHeight - box.scrollTop - box.clientHeight < 40;
  box.innerHTML = msgs.map(m => m.system ? `<div class="msg sys">${esc(m.text)}</div>` : `<div class="msg"><b style="color:${esc(m.color)}">${esc(m.from)}:</b> ${esc(m.text)}</div>`).join("");
  if (atBottom || chatOpen) box.scrollTop = box.scrollHeight;
  const lastTs = msgs.filter(m => !m.system).at(-1)?.ts || 0;
  const unread = chatOpen ? 0 : msgs.filter(m => !m.system && m.ts > chatSeen).length;
  if (chatOpen) chatSeen = lastTs;
  const badge = $("#chat-fab .badge");
  badge.hidden = unread === 0; badge.textContent = unread;
}
$("#chat-fab").addEventListener("click", () => { chatOpen = true; updateChat(); $("#chat-input").focus(); });
$("#chat-close").addEventListener("click", () => { chatOpen = false; updateChat(); });
$("#chat-form").addEventListener("submit", e => {
  e.preventDefault();
  const i = $("#chat-input"); const t = i.value.trim(); if (!t) return;
  i.value = ""; call("Chat", t).catch(() => { });
});

// ============================================================ HALL OF FAME
const HallOfFameScreen = {
  key: () => "hof",
  async mount(view) {
    view.innerHTML = `<div class="row between"><h1 style="font-family:var(--display);font-weight:400;font-size:2.4rem;margin:0">🏆 Hall of Fame</h1><a class="btn ghost" href="#/">← Back</a></div><div class="hof-grid" id="hof" style="margin-top:16px"><p class="muted">Loading…</p></div>`;
    const stories = await (await fetch("api/halloffame")).json().catch(() => []);
    const pin = sessionStorage.getItem("conseq.adminPin");
    const canDelete = config.adminAvailable && (pin || config.viaIngress);
    $("#hof", view).innerHTML = stories.length ? stories.map(st => html`<div>
      <div class="paper reveal-paper ${st.pictureMode ? "picture" : ""}">
        ${st.folds.map(f => entryHtml({ prefix: f.prefix, suffix: f.suffix, text: f.text, image: f.imageId ? "halloffame/img/" + f.imageId : null, author: f.author, kind: f.pictureStrip ? "drawing" : "text", autoFilled: false }, { picture: st.pictureMode, canStar: false })).join("")}
      </div>
      <div class="small muted" style="margin-top:6px">${esc(st.templateName)} · saved by ${esc(st.savedBy)} · ${new Date(st.savedAt).toLocaleDateString()}
        <button class="btn sm ghost" data-exp="${st.id}">🖼️</button>
        ${canDelete ? `<button class="btn sm ghost" data-del="${st.id}">🗑️</button>` : ""}</div></div>`).join("")
      : `<p class="muted">Nothing here yet. At the end of a game, tap “Save to Hall of Fame” on your favourite paper.</p>`;
    view.addEventListener("click", async e => {
      const d = e.target.closest("[data-del]");
      if (d && await confirmBox("Delete this story?", "It's gone for good.", "Delete")) {
        const r = await fetch("api/admin/halloffame/" + d.dataset.del, { method: "DELETE", headers: { "X-Admin-Pin": pin || "" } });
        if (r.ok) { d.closest(".hof-grid > div").remove(); toast("Deleted", "ok"); } else toast("Not allowed", "err");
      }
      const x = e.target.closest("[data-exp]");
      if (x) {
        const st = stories.find(s => s.id === x.dataset.exp);
        exportPaper({ index: 0, entries: st.folds.map(f => ({ prefix: f.prefix, suffix: f.suffix, text: f.text, image: f.imageId ? "halloffame/img/" + f.imageId : null })) }, st.pictureMode, st.title || "Consequences");
      }
    });
  },
};

// ============================================================ ADMIN
const AdminScreen = {
  key: () => "admin",
  async mount(view) {
    view.innerHTML = `<div class="row between"><h1 style="font-family:var(--display);font-weight:400;font-size:2.4rem;margin:0">⚙️ Server settings</h1><a class="btn ghost" href="#/">← Back</a></div><div id="admin" style="margin-top:16px"></div>`;
    this.view = view;
    this.pin = sessionStorage.getItem("conseq.adminPin") || "";
    await this.load();
  },
  async api(path, opts = {}) {
    const r = await fetch("api/admin/" + path, { ...opts, headers: { "Content-Type": "application/json", "X-Admin-Pin": this.pin } });
    if (r.status === 401) { const j = await r.json().catch(() => ({})); throw Object.assign(new Error(j.error || "Wrong PIN"), { auth: true }); }
    if (!r.ok) throw new Error("Request failed");
    return r.status === 200 ? r.json().catch(() => ({})) : {};
  },
  async load() {
    const box = $("#admin", this.view);
    try {
      const data = await this.api("settings");
      const rooms = await this.api("rooms");
      const stats = await this.api("stats");
      this.render(box, data, rooms, stats);
    } catch (e) {
      if (!e.auth) { box.innerHTML = `<p>${esc(e.message)}</p>`; return; }
      box.innerHTML = html`<div class="card" style="max-width:420px"><h2>Admin PIN</h2><p class="small muted">${esc(e.message)}</p>
        <form id="pin-form" class="row"><input type="password" id="pin" placeholder="PIN" style="flex:1" autocomplete="current-password"><button class="btn primary">Unlock</button></form></div>`;
      $("#pin-form", box).addEventListener("submit", ev => { ev.preventDefault(); this.pin = $("#pin", box).value; sessionStorage.setItem("conseq.adminPin", this.pin); this.load(); });
    }
  },
  render(box, data, rooms, stats) {
    const s = data.settings;
    box.innerHTML = html`
      <div class="grid-2">
        <div class="stack">
          <div class="card">
            <h2>Features</h2>
            <p class="small muted">Switch any feature off for the whole server. Room features can also be switched off per room by the host.</p>
            <div class="feature-grid" id="a-features">${config.catalog.map(f => html`<label class="switch"><input type="checkbox" data-key="${f.key}" ${s.features[f.key] ? "checked" : ""}><span class="knob"></span><span class="txt"><b>${f.icon} ${esc(f.label)}</b><small>${esc(f.description)}${f.scope === "server" ? " <i>(server-wide)</i>" : ""}</small></span></label>`).join("")}</div>
          </div>
        </div>
        <div class="stack">
          <div class="card">
            <h2>General</h2>
            <label class="field"><span>App name</span><input type="text" id="a-name" value="${esc(s.appName)}" maxlength="40"></label>
            <label class="field"><span>Public URL (for invite links & QR codes)</span><input type="url" id="a-url" value="${esc(s.publicUrl)}" placeholder="https://consequences.example.com"></label>
            <label class="field"><span>Room-creation PIN (optional)</span><input type="text" id="a-cpin" value="${esc(s.createRoomPin)}" placeholder="Leave empty so anyone can create rooms" autocomplete="off"></label>
            <div class="row">
              <label class="field" style="flex:1"><span>Max rooms</span><input type="number" id="a-rooms" value="${s.maxRooms}" min="1"></label>
              <label class="field" style="flex:1"><span>Max players / room</span><input type="number" id="a-players" value="${s.maxPlayersPerRoom}" min="2" max="40"></label>
            </div>
            <div class="row">
              <label class="field" style="flex:1"><span>Default write secs</span><input type="number" id="a-ts" value="${s.defaultTextSeconds}"></label>
              <label class="field" style="flex:1"><span>Default draw secs</span><input type="number" id="a-ds" value="${s.defaultDrawSeconds}"></label>
              <label class="field" style="flex:1"><span>Close idle rooms (mins)</span><input type="number" id="a-idle" value="${s.roomIdleMinutes}"></label>
            </div>
            <label class="field"><span>Extra words for the family filter (comma separated)</span><textarea id="a-words" rows="2">${esc(s.extraBannedWords.join(", "))}</textarea></label>
            <div class="row"><button class="btn primary" id="a-save">💾 Save settings</button><button class="btn ghost" id="a-reset">↺ Reset to app options</button></div>
            ${data.runningInHomeAssistant ? `<p class="small muted" style="margin-top:10px">🏠 Running as a Home Assistant App. If you later change the app's <b>Configuration</b> tab and restart, those options take over again.</p>` : ""}
          </div>
          <div class="card">
            <h2>Live rooms <span class="small muted">${stats.rooms} rooms · ${stats.players} online · ${stats.gamesPlayed} games since start</span></h2>
            ${rooms.length ? `<table class="admin-table"><tr><th>Code</th><th>Status</th><th>Players</th><th></th></tr>${rooms.map(r => html`<tr><td><b>${esc(r.code)}</b><br><span class="small muted">${esc(r.name || "")}</span></td><td>${esc(r.phase)}${r.isPublic ? " 🚪" : ""}</td><td class="small">${r.players.map(p => esc(p.name) + (p.isBot ? "🤖" : "") + (p.connected ? "" : "📵")).join(", ")}</td><td><button class="btn sm ghost" data-close-room="${esc(r.code)}">Close</button></td></tr>`).join("")}</table>` : `<p class="muted">No rooms right now.</p>`}
          </div>
        </div>
      </div>`;

    $("#a-save", box).addEventListener("click", async () => {
      const features = {};
      $$("#a-features input", box).forEach(i => features[i.dataset.key] = i.checked);
      const body = {
        appName: $("#a-name", box).value, publicUrl: $("#a-url", box).value, createRoomPin: $("#a-cpin", box).value,
        maxRooms: +$("#a-rooms", box).value, maxPlayersPerRoom: +$("#a-players", box).value,
        defaultTextSeconds: +$("#a-ts", box).value, defaultDrawSeconds: +$("#a-ds", box).value, roomIdleMinutes: +$("#a-idle", box).value,
        extraBannedWords: $("#a-words", box).value.split(",").map(x => x.trim()).filter(Boolean), features,
      };
      try { await this.api("settings", { method: "POST", body: JSON.stringify(body) }); config = await loadConfig(); toast("Saved ✔️", "ok"); this.load(); }
      catch (e) { toast(e.message, "err"); }
    });
    $("#a-reset", box).addEventListener("click", async () => {
      if (!await confirmBox("Reset settings?", "Go back to the app options / environment defaults.", "Reset")) return;
      await this.api("reset", { method: "POST" }); config = await loadConfig(); toast("Reset ✔️", "ok"); this.load();
    });
    box.querySelectorAll("[data-close-room]").forEach(b => b.addEventListener("click", async () => {
      if (await confirmBox("Close room " + b.dataset.closeRoom + "?", "Everyone in it will be sent home.", "Close")) { await this.api("rooms/" + b.dataset.closeRoom + "/close", { method: "POST" }); this.load(); }
    }));
  },
};

// ============================================================ help, theme, boot
function showHelp() {
  modal(html`<div class="howto"><h2>How to play</h2>
    <ol>
      <li><b>Everyone gets a sheet of paper.</b> Each fold asks for one thing: a name, a place, what they wore…</li>
      <li><b>Write (or draw) your bit</b>, then hit <i>Fold & pass</i>. Your answer is folded out of sight and the paper goes to the next player.</li>
      <li>You only ever see ${F("peek") ? "the last few words that poke over the fold" : "the prompt, never what came before"}.</li>
      <li>When every fold is filled, <b>the papers are unfolded one by one</b> and read out. Chaos ensues.</li>
      ${F("stars") ? "<li>Tap ⭐ on the best lines and drawings. Awards at the end!</li>" : ""}
      ${F("pictureMode") ? "<li><b>Picture Consequences:</b> draw a head, body, legs and feet. Carry on the lines poking over the fold to make one glorious creature.</li>" : ""}
    </ol>
    <p class="small muted">Tip: add it to your home screen for a full-screen app. Works on phones, tablets and computers.</p>
    <button class="btn primary block" data-close>Got it!</button></div>`);
}

function applyTheme() {
  const t = store.get("theme", "auto");
  if (t === "auto") document.documentElement.removeAttribute("data-theme"); else document.documentElement.dataset.theme = t;
  $("#btn-theme").textContent = t === "dark" ? "🌙" : t === "light" ? "☀️" : "🌓";
}

async function loadConfig() { return (await fetch("api/config")).json(); }

async function boot() {
  applyTheme();
  $("#btn-sound").textContent = soundOn ? "🔔" : "🔕";
  $("#btn-theme").addEventListener("click", () => { const order = ["auto", "light", "dark"]; store.set("theme", order[(order.indexOf(store.get("theme", "auto")) + 1) % 3]); applyTheme(); });
  $("#btn-sound").addEventListener("click", () => { soundOn = !soundOn; store.set("sound", soundOn); $("#btn-sound").textContent = soundOn ? "🔔" : "🔕"; if (soundOn) sfx("ding"); });
  $("#btn-help").addEventListener("click", showHelp);
  $("#logo").addEventListener("click", async () => {
    if (state && !await confirmBox("Leave the room?", "You can rejoin with the code while the room is open.", "Leave")) return;
    if (state) { await call("Leave").catch(() => { }); leaveLocal(); }
    location.hash = ""; route = ""; render();
  });
  window.addEventListener("hashchange", () => { route = location.hash.replace(/^#\/?/, ""); render(); });

  try { config = await loadConfig(); }
  catch { $("#view").innerHTML = `<div class="card center"><h2>Can't reach the server 😢</h2><p>Check your connection and refresh.</p></div>`; return; }
  document.title = config.appName;

  // A link for a different room than the one we were in? Forget the old seat.
  const urlRoom = new URLSearchParams(location.search).get("room")?.toUpperCase();
  if (session && urlRoom && session.code !== urlRoom) { session = null; store.del("session"); }

  render();
  await startConnection();
  // Link to a room, already have a name and no seat: join straight away
  if (!session && urlRoom && profile.name.trim() && !route) joinRoom(urlRoom, false);
}

boot();
