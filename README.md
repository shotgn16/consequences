# Consequences 📜✏️

The classic folded-paper party game, online. Write a bit, fold it over, pass it on, then unfold the chaos together. You can draw the people, outfits and meeting places right on the paper.

* **ASP.NET Core 10** + SignalR, real-time, no database needed
* Plays in **any modern browser**: phone, tablet, laptop, PC. Touch, mouse and stylus drawing. Installable as a home-screen app.
* **Many rooms at once**, each with its own host, players, spectators and settings
* Runs on **Docker** or as a **Home Assistant App** (sidebar panel included)
* Ready for **Cloudflare Tunnel** public access (WebSockets, no open ports)
* **Every feature can be switched on/off**: server-wide (admin page, HA options or env vars) and per room by the host

---

## How it plays (just like the paper version)

1. Everyone joins a room with a 4-letter code (or by scanning the QR code).
2. Each player gets a sheet. Every *fold* asks for one thing: an adjective, a name, where they met, what they wore, what they said, the consequence, what the world said.
3. You write (and optionally draw), hit **Fold & pass**, and the paper animates folding away to the next player, **passing left** just like at the table (or shuffled, if you like).
4. You only see what pokes over the fold: the last few words, or, in Picture Consequences, the lines carried over from the drawing above.
5. When all the papers are full, they're **unfolded one fold at a time**. Players take turns being the reader, just like passing the papers round, with optional text-to-speech, emoji reactions flying across everyone's screen, and ⭐ stars for the best lines.
6. Awards, a scoreboard, save favourites to the **Hall of Fame** or share them as an image. Then play again.

### Game modes
| Mode | Folds | Notes |
|---|---|---|
| **Classic** | 11 | (adj) man met (adj) woman at… he wore / she wore / he said / she said / consequence / world said |
| **Anyone meets anyone** | 11 | Gender-neutral version |
| **Quickie** | 7 | Short and sweet |
| **Picture Consequences** | 5 | Exquisite corpse: head → body → legs → feet, lines carry over the fold, then someone names the creature |
| **Scene Painter** | 6 | Every fold is a place to paint: a journey nobody planned |
| **Custom** | 2–20 | Hosts write their own folds and connectives |

### Extras
Drawing pad (pen, eraser, fill bucket, undo, 14 colours + picker, 4 sizes, pressure-sensitive stylus) · round timers with auto-submit · "Inspire me" dice · auto-fill for slow or departed players · computer players (bots) · spectators & late joiners · room chat · emoji reactions · stars, awards & a running scoreboard · read-aloud (TTS) · take-turns reading · show/hide authors · shuffle passing · family-friendly word filter · save/share story as PNG · Hall of Fame gallery · public room browser · room passwords · QR join codes · reconnect on refresh, lost signal or app switch (your seat is kept) · screen wake-lock during games · light/dark theme · sound effects · `/api/stats` for Home Assistant sensors.

---

## Feature switches

Every optional feature has a switch. **Server-wide** switches live on the admin **Settings** page (⚙️ on the home screen), in the Home Assistant app's **Configuration** tab, or in environment variables. Features marked *Server + room* can also be switched off by the host for a single room, in the lobby. A feature that's off server-wide shows as "Switched off by the server admin" to hosts.

| Feature | What it does | Switchable by | HA option | Docker env var |
|---|---|---|---|---|
| 🎨 Drawing | Draw the people, outfits and meeting places right on the paper. | Server + room | `feature_drawing` | `CONSEQ_FEATURE_DRAWING` |
| 🧍 Picture Consequences | The drawing version: head, body, legs and feet drawn by different players. | Server + room | `feature_picture_mode` | `CONSEQ_FEATURE_PICTURE_MODE` |
| 👀 Peek over the fold | Next player sees the last few words of the previous fold, like a badly folded paper. (Picture mode always shows the lines over the fold.) | Server + room | `feature_peek` | `CONSEQ_FEATURE_PEEK` |
| ⏱️ Round timer | Put a countdown on every fold. | Server + room | `feature_timer` | `CONSEQ_FEATURE_TIMER` |
| 🪄 Auto-fill | Fill in for slow or departed players with a random silly answer. | Server + room | `feature_auto_fill` | `CONSEQ_FEATURE_AUTO_FILL` |
| 🎲 Inspire me | A dice button that suggests something to write. | Server + room | `feature_inspire` | `CONSEQ_FEATURE_INSPIRE` |
| 💬 Room chat | Text chat between players in a room. | Server + room | `feature_chat` | `CONSEQ_FEATURE_CHAT` |
| 😂 Emoji reactions | Throw emoji at the screen during the big reveal. | Server + room | `feature_reactions` | `CONSEQ_FEATURE_REACTIONS` |
| ⭐ Stars & awards | Star your favourite lines and drawings; awards and scores at the end. | Server + room | `feature_stars` | `CONSEQ_FEATURE_STARS` |
| 🔊 Read aloud | Text-to-speech reads each fold as it is unfolded. | Server + room | `feature_read_aloud` | `CONSEQ_FEATURE_READ_ALOUD` |
| 📜 Take turns reading | Each paper is unfolded by a different player, like passing the papers round to read. | Server + room | `feature_rotate_reader` | `CONSEQ_FEATURE_ROTATE_READER` |
| 🏷️ Show who wrote what | Show the author's name on each fold during the reveal. | Server + room | `feature_show_authors` | `CONSEQ_FEATURE_SHOW_AUTHORS` |
| 🤖 Computer players | Add bots to make up the numbers. | Server + room | `feature_bots` | `CONSEQ_FEATURE_BOTS` |
| 🍿 Spectators & late joiners | Let people watch, or join a game already in progress as a spectator. | Server + room | `feature_spectators` | `CONSEQ_FEATURE_SPECTATORS` |
| ✏️ Custom prompts | Hosts can write their own set of folds. | Server + room | `feature_custom_templates` | `CONSEQ_FEATURE_CUSTOM_TEMPLATES` |
| 🔀 Shuffle passing | Papers jump to a random player instead of always passing left. | Server + room | `feature_shuffle_pass` | `CONSEQ_FEATURE_SHUFFLE_PASS` |
| 🔔 Sound effects | Paper rustles, dings and fanfares. | Server + room | `feature_sounds` | `CONSEQ_FEATURE_SOUNDS` |
| 🧼 Family-friendly filter | Masks rude words. | Server + room | `feature_word_filter` | `CONSEQ_FEATURE_WORD_FILTER` |
| 🖼️ Save & share images | Download or share a finished story as a picture. | Server + room | `feature_export_image` | `CONSEQ_FEATURE_EXPORT_IMAGE` |
| 🏆 Hall of Fame | Save favourite stories permanently to the server's gallery. | Server + room | `feature_hall_of_fame` | `CONSEQ_FEATURE_HALL_OF_FAME` |
| 🚪 Public room browser | List open rooms on the home screen so anyone can join. | Server | `feature_public_rooms` | `CONSEQ_FEATURE_PUBLIC_ROOMS` |
| 🔒 Room passwords | Hosts can lock a room with a password. | Server | `feature_room_passwords` | `CONSEQ_FEATURE_ROOM_PASSWORDS` |
| 📱 QR join codes | Show a QR code so phones can join in a snap. | Server | `feature_qr_code` | `CONSEQ_FEATURE_QR_CODE` |
| 📊 Stats API | Expose /api/stats for Home Assistant REST sensors. | Server | `feature_stats_api` | `CONSEQ_FEATURE_STATS_API` |
| 🏠 Admin via HA sidebar | Open the admin page without a PIN when coming through the Home Assistant sidebar (ingress). | Server | `feature_admin_via_ingress` | `CONSEQ_FEATURE_ADMIN_VIA_INGRESS` |

How the layers combine: built-in defaults → HA options / env vars → admin Settings page. If you later change the HA options (or env vars) and restart, they take over again. The most recent change always wins. **Reset to app options** on the Settings page drops the admin-page overrides.

---

## Run with Docker

```bash
git clone https://github.com/shotgn16/consequences.git
cd consequences
docker compose up -d --build
# open http://localhost:8080
```

Or without compose:

```bash
docker build -t consequences ./consequences
docker run -d --name consequences -p 8080:8080 -v consequences-data:/data \
  -e CONSEQ_ADMIN_PIN=change-me consequences
```

### Settings (environment variables)

| Variable | Default | Meaning |
|---|---|---|
| `CONSEQ_ADMIN_PIN` | *(empty)* | PIN for the ⚙️ Settings page. Empty = admin disabled (except via the HA sidebar). |
| `CONSEQ_PUBLIC_URL` | *(empty)* | Public address for invite links & QR codes, e.g. `https://consequences.example.com` |
| `CONSEQ_CREATE_ROOM_PIN` | *(empty)* | If set, a PIN is needed to *create* rooms (joining stays open) |
| `CONSEQ_APP_NAME` | `Consequences` | Title shown in the app |
| `CONSEQ_MAX_ROOMS` | `50` | Rooms allowed at once |
| `CONSEQ_MAX_PLAYERS_PER_ROOM` | `16` | Upper limit a host can pick |
| `CONSEQ_ROOM_IDLE_MINUTES` | `120` | Close rooms with no activity |
| `CONSEQ_DEFAULT_TEXT_SECONDS` / `CONSEQ_DEFAULT_DRAW_SECONDS` | `90` / `180` | Default timers |
| `CONSEQ_EXTRA_BANNED_WORDS` | *(empty)* | Comma-separated words for the family filter |
| `CONSEQ_FEATURE_*` | see table | `true`/`false` per feature |
| `CONSEQ_BASE_PATH` | *(empty)* | Serve under a sub-path behind your own reverse proxy, e.g. `/consequences` |
| `CONSEQ_DATA_PATH` | `/data` | Where settings and the Hall of Fame are stored |

---

## Install as a Home Assistant App

1. In Home Assistant go to **Settings → Apps → App store** (older versions: *Add-ons → Add-on store*), open the **⋮** menu → **Repositories**, and add the URL of this Git repository (it contains `repository.yaml`).
2. Find **Consequences** in the store, click **Install** (the first build on a Raspberry Pi takes a few minutes), then **Start**.
3. Turn on **Show in sidebar**. Opening it from the sidebar gives you the game *and* the ⚙️ Settings page with no PIN, because Home Assistant has already logged you in.
4. On the **Configuration** tab you'll find every feature switch plus the general options (admin PIN, public URL…).

> Want to skip building on the device? The included GitHub Actions workflow publishes images to GHCR. Uncomment the `image:` line in `consequences/config.yaml`.

Players who aren't Home Assistant users can't use the sidebar link. They need the direct port **8080**, which is where your Cloudflare Tunnel points (below).

---

## Public access with Cloudflare Tunnel

The game only needs one hostname pointing at port **8080**. WebSockets work through Cloudflare Tunnels out of the box.

### A) You use the Cloudflared app in Home Assistant (most common)

Add an extra hostname to the **Cloudflared** app's configuration, then restart it:

```yaml
external_hostname: ha.example.com          # your existing HA hostname
additional_hosts:
  - hostname: consequences.example.com
    service: http://192.168.1.50:8080      # your Home Assistant's LAN IP + the game port
```

Then set **public_url** in the Consequences app options to `https://consequences.example.com`, so invite links and QR codes use it. Restart the app.

### B) Remote-managed tunnel (Cloudflare Zero Trust dashboard)

**Networks → Tunnels → your tunnel → Public Hostname → Add**: subdomain `consequences`, service type **HTTP**, URL `HA_or_docker_host_IP:8080`.

### C) Plain Docker with a cloudflared sidecar

Put your tunnel token in `.env` (`TUNNEL_TOKEN=...`) and run `docker compose --profile tunnel up -d`. In the Cloudflare dashboard, point the public hostname at `http://consequences:8080`.

### Recommended when public
* Set an **admin PIN** (or leave it empty to only manage from the HA sidebar).
* Consider a **room-creation PIN** so strangers can't open rooms (anyone with a code can still join).
* Room creation is rate-limited per visitor (uses Cloudflare's `CF-Connecting-IP`), and rooms, players, message sizes and drawings are capped.
* Optional: put **Cloudflare Access** in front for extra login protection.

---

## Home Assistant sensors (optional)

With the *Stats API* feature on, `GET /api/stats` returns `{"rooms":1,"players":4,"gamesInProgress":1,"gamesPlayed":12,"images":30}`.

```yaml
# configuration.yaml
rest:
  - resource: http://localhost:8080/api/stats   # or http://<HA IP>:8080/api/stats
    scan_interval: 30
    sensor:
      - name: Consequences players online
        value_template: "{{ value_json.players }}"
        unit_of_measurement: players
      - name: Consequences rooms
        value_template: "{{ value_json.rooms }}"
      - name: Consequences games played
        value_template: "{{ value_json.gamesPlayed }}"
        state_class: total_increasing
```

---

## Development

```bash
cd consequences/src/Consequences
dotnet run            # http://localhost:5000 (or the port it prints)
```

The front end is plain HTML/CSS/JS in `wwwroot/` with no build step. Libraries (SignalR client, QR generator, fonts) are bundled locally, so the app works on a LAN with no internet.

```
consequences/                  ← Home Assistant App folder (also the Docker build context)
  config.yaml, translations/   ← HA options: one toggle per feature
  Dockerfile
  src/Consequences/
    Program.cs                 ← HTTP endpoints, admin API, static files
    Hubs/GameHub.cs            ← real-time API (join, write, fold, unfold, star, react, chat…)
    Game/Room.cs               ← game rules: seating, passing, rounds, reveal, scores
    Game/StateBuilder.cs       ← per-player view (folded content is never sent early: no peeking via dev tools)
    Game/Templates.cs          ← game modes
    Settings/FeatureCatalog.cs ← the list of switchable features
    Services/                  ← rooms, janitor (timers/bots/cleanup), images, Hall of Fame
    wwwroot/                   ← the browser app
```

Rooms live in memory. A restart ends games in progress, while settings and the Hall of Fame are kept in `/data`.

## Licence

MIT. Bundled third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
