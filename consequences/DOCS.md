# Consequences

## Getting started

1. **Start** the app and switch on **Show in sidebar**.
2. Open **Consequences** from the sidebar, type your name and **Create a room**.
3. Share the room code, link or QR code. Everyone plays from their own phone, tablet or computer.

Opening the app from the Home Assistant sidebar also unlocks the **⚙️ Settings** page without a PIN (you're already logged in to Home Assistant).

## Letting people outside your home play (Cloudflare Tunnel)

People without a Home Assistant login can't use the sidebar link. They need the game's own port, **8080**.

If you use the **Cloudflared** app, add this to *its* configuration and restart it:

```yaml
additional_hosts:
  - hostname: consequences.example.com
    service: http://YOUR_HA_LAN_IP:8080
```

With a remote-managed tunnel, add a *Public Hostname* in the Cloudflare Zero Trust dashboard pointing to `HTTP` → `YOUR_HA_LAN_IP:8080`.

Then set **Public URL** below to `https://consequences.example.com` so invite links and QR codes use it.

When the game is public, we recommend setting a **Room-creation PIN** and an **Admin PIN**.

## Options

| Option | What it does |
|---|---|
| **Admin PIN** | PIN for the ⚙️ Settings page when opened from outside Home Assistant. Empty = only from the sidebar. |
| **Public URL** | Your public address, used for invite links & QR codes. |
| **Room-creation PIN** | If set, people need it to *create* rooms. Joining with a code stays open. |
| **App name** | Title shown in the app. |
| **Max rooms / Max players per room** | Limits. |
| **Close idle rooms after** | Minutes of inactivity before a room is tidied away. |
| **Default seconds to write / draw** | Starting values for the round timer. |
| **Extra filtered words** | Added to the family-friendly filter. |
| **Feature: …** | One switch per feature. Turning one off removes it everywhere. Most features can also be switched off per room by the host in the lobby. |

You can also change all of these live on the in-game ⚙️ Settings page. If you change the options here and restart the app, they take over again.

## Sensors

Turn on **Feature: Stats API** and add a REST sensor for `http://localhost:8080/api/stats` (`rooms`, `players`, `gamesInProgress`, `gamesPlayed`). See the project README for a ready-made YAML snippet.

## Data

Settings and the Hall of Fame are stored in the app's `/data` folder and are included in Home Assistant backups. Games in progress live in memory, so restarting the app ends them.
