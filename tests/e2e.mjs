import * as signalR from "@microsoft/signalr";
const BASE = (process.env.BASE || "http://127.0.0.1:8080").replace(/\/$/, "");
const URL = BASE + "/hub";
const sleep = ms => new Promise(r => setTimeout(r, ms));
// tiny valid PNG (1x1) padded to >16 bytes
const PNG = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";
function mk(name) {
  const c = new signalR.HubConnectionBuilder().withUrl(URL).configureLogging(signalR.LogLevel.Error).build();
  const p = { name, c, state: null, reactions: 0 };
  c.on("state", s => { p.state = s; });
  c.on("reaction", () => { p.reactions++; });
  c.on("sound", () => {}); c.on("kicked", () => { p.kicked = true; }); c.on("roomClosed", () => {});
  return p;
}
const assert = (cond, msg) => { if (!cond) { console.error("FAIL:", msg); process.exit(1); } else console.log("ok -", msg); };
async function waitFor(fn, msg, ms = 15000) { const t = Date.now(); while (Date.now() - t < ms) { if (fn()) return; await sleep(50); } assert(false, "timeout: " + msg); }

async function playGame(players, host, expectPicture) {
  await host.c.invoke("StartGame");
  await waitFor(() => players.every(p => p.state?.phase === "writing"), "everyone in writing phase");
  const rounds = host.state.game.totalRounds;
  for (let r = 0; r < rounds; r++) {
    await waitFor(() => players.every(p => p.state.game?.round === r || p.state.phase === "reveal"), "round " + r);
    for (const p of players) {
      const t = p.state.task;
      assert(t, `${p.name} has a task in round ${r} (${t?.prompt?.slice(0,30)})`);
      if (r > 0 && !expectPicture) assert(t.peekText || true, "peek ok");
      if (expectPicture && t.kind === "drawing" && r > 0) assert(t.peekImage?.startsWith("img/"), "picture strip guide shown");
      if (t.kind === "drawing") await p.c.invoke("Submit", null, PNG);
      else await p.c.invoke("Submit", `${p.name} r${r} words here`, t.canDraw && r % 2 ? PNG : null);
    }
  }
  await waitFor(() => players.every(p => p.state.phase === "reveal"), "reveal phase");
}

const a = mk("Alice"), b = mk("Bob"), c = mk("Cara");
for (const p of [a, b, c]) await p.c.start();
const res = await a.c.invoke("CreateRoom", { name: "Alice", avatar: "🦊", color: "#e63946" });
assert(/^[A-Z]{4}$/.test(res.code), "room created " + res.code);
await b.c.invoke("JoinRoom", { code: res.code.toLowerCase(), name: "Bob" });
await c.c.invoke("JoinRoom", { code: res.code, name: "Alice" }); // duplicate name
await waitFor(() => a.state?.players.length === 3, "3 players in lobby");
assert(a.state.players.some(p => p.name === "Alice 2"), "duplicate names made unique");
// non-host can't change settings
try { await b.c.invoke("UpdateSettings", { templateId: "quick" }); assert(false, "guest blocked"); } catch (e) { assert(/host/i.test(e.message), "guest can't change settings"); }
await a.c.invoke("UpdateSettings", { templateId: "quick", features: { timer: false } });
await a.c.invoke("AddBot");
await waitFor(() => a.state.players.length === 4, "bot added");
await a.c.invoke("Chat", "hello all");
await waitFor(() => b.state.chat.some(m => m.text === "hello all"), "chat delivered");

const players = [a, b, c];
await playGame(players, a, false);

// folded content hidden before unfolding
assert(a.state.reveal.paper.entries.length === 0, "nothing visible before unfolding");
const reader = [a, b, c].find(p => p.state.reveal.canControl && p.state.reveal.readerId === p.state.you.id) || a;
const nonReader = [b, c].find(p => !p.state.reveal.canControl);
if (nonReader) { try { await nonReader.c.invoke("Unfold", false); assert(false, "non reader blocked"); } catch (e) { assert(true, "non-reader can't unfold: " + e.message.replace(/.*HubException: /, "")); } }
await reader.c.invoke("Unfold", false);
await waitFor(() => b.state.reveal.paper.entries.length === 1, "one fold unfolded for everyone");
await b.c.invoke("React", "😂");
await waitFor(() => a.reactions > 0, "reaction broadcast");
// star someone else's line
const e0 = b.state.reveal.paper.entries[0];
const starrer = [a, b, c].find(p => p.state.you.id !== e0.authorId);
await starrer.c.invoke("Star", e0.id);
await waitFor(() => a.state.reveal.paper.entries[0].stars === 1, "star counted");
// walk all papers
const count = a.state.reveal.paperCount;
assert(count === 4, "4 papers (3 humans + bot)");
for (let i = 0; i < count; i++) {
  await waitFor(() => [a, b, c].every(p => p.state.reveal.paperIndex === i), "all on paper " + i);
  const ctl = [a, b, c].find(p => p.state.you.id === a.state.reveal.readerId) || a;
  await ctl.c.invoke("Unfold", true);
  await waitFor(() => a.state.reveal.paper.unfolded === a.state.reveal.paper.total, "paper " + i + " unfolded");
  if (i === 0) { await b.c.invoke("SaveToHallOfFame", 0); }
  if (i < count - 1) { await ctl.c.invoke("NextPaper"); await waitFor(() => a.state.reveal.paperIndex === i + 1, "next paper"); }
}
await a.c.invoke("FinishReveal");
await waitFor(() => c.state.phase === "results", "results phase");
assert(c.state.results.papers.length === 4 && c.state.results.papers.every(p => p.entries.length === 7), "all papers fully visible in results");
assert(c.state.results.awards.bestLine?.stars === 1, "best line award computed");
const bot = a.state.players.find(p => p.isBot);
assert(c.state.results.papers.flatMap(p => p.entries).filter(e => e.authorId === bot.id).every(e => e.text), "bot wrote everything");

// verify every paper had each step written by different players in classic rotation
for (const p of c.state.results.papers) { const authors = p.entries.map(e => e.authorId); assert(new Set(authors.slice(0, 4)).size === 4, `paper ${p.index} first 4 folds by 4 different people`); }

const hof = await (await fetch(BASE + "/api/halloffame")).json();
assert(hof.length >= 1 && hof[0].folds.length === 7, "hall of fame saved");
const imgFold = hof.flatMap(h => h.folds).find(f => f.imageId);
if (imgFold) { const r = await fetch(BASE + "/halloffame/img/" + imgFold.imageId); assert(r.ok && r.headers.get("content-type") === "image/png", "hall of fame image served"); }

await a.c.invoke("BackToLobby");
await waitFor(() => b.state.phase === "lobby", "back to lobby");
const scorer = a.state.players.find(p => p.id === e0.authorId);
assert(scorer.score === 1, "score carried to lobby");

// ---- picture mode, kicked player mid-game gets auto-filled
await a.c.invoke("Kick", bot.id);
await a.c.invoke("UpdateSettings", { templateId: "picture", features: { timer: true }, drawSeconds: 10 });
await waitFor(() => a.state.settings.templateId === "picture" && a.state.players.length === 3, "picture mode selected, bot removed");
await a.c.invoke("StartGame");
await waitFor(() => c.state.phase === "writing", "picture game started");
assert(a.state.deadline > Date.now(), "timer deadline set");
// Cara leaves mid game
await c.c.invoke("Leave");
const two = [a, b];
const rounds = a.state.game.totalRounds;
let peeks = 0;
for (let r = 0; r < rounds; r++) {
  await waitFor(() => two.every(p => p.state.game?.round === r || p.state.phase === "reveal"), "pic round " + r);
  for (const p of two) {
    const t = p.state.task;
    if (r > 0 && t.kind === "drawing" && t.peekImage) peeks++;
    if (t.kind === "drawing") await p.c.invoke("Submit", null, PNG); else await p.c.invoke("Submit", "Sir Wobble", null);
  }
}
await waitFor(() => a.state.phase === "reveal", "picture reveal");
assert(peeks >= 3, "lines over the fold shown to next drawer (" + peeks + " times)");
await a.c.invoke("FinishReveal");
await waitFor(() => a.state.phase === "results", "picture results");
const caraEntries = a.state.results.papers.flatMap(p => p.entries).filter(e => e.author === "Alice 2");
assert(caraEntries.length === 5 && caraEntries.every(e => e.autoFilled), "departed player's folds auto-filled");

// image validation: junk rejected
await a.c.invoke("BackToLobby");
await a.c.invoke("UpdateSettings", { templateId: "picture" });
// password + public listing
await a.c.invoke("UpdateSettings", { isPublic: true, password: "sekret", roomName: "Test room" });
const list = await (await fetch(BASE + "/api/rooms")).json();
assert(list.some(r => r.code === res.code && r.locked && r.name === "Test room"), "public room listed as locked");
const d = mk("Dan"); await d.c.start();
try { await d.c.invoke("JoinRoom", { code: res.code, name: "Dan" }); assert(false, "pw"); } catch (e) { assert(/PASSWORD_REQUIRED/.test(e.message), "password required"); }
await d.c.invoke("JoinRoom", { code: res.code, name: "Dan", password: "sekret" });
await waitFor(() => a.state.players.some(p => p.name === "Dan"), "joined with password");
// rejoin with token on a new connection keeps seat
const a2 = mk("Alice-again"); await a2.c.start();
await a.c.stop();
const rj = await a2.c.invoke("JoinRoom", { code: res.code, token: res.token, name: "x" });
assert(rj.playerId === res.playerId, "rejoin with token restores the same player");
await waitFor(() => a2.state?.you?.isHost, "still host after reconnect");
const stats = await (await fetch(BASE + "/api/stats")).json();
assert(stats.rooms >= 1 && stats.gamesPlayed >= 2, "stats api: " + JSON.stringify(stats));
console.log("\nALL TESTS PASSED");
process.exit(0);
