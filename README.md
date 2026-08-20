# StatsHub

A full-stack basketball stats tracker for parents and kids, built with:
- **Backend**: ASP.NET Core 10 Web API + EF Core (SQLite)
- **Frontend**: React 18 + TypeScript + Vite

## What it does

- **Parents** sign in with Google, create a profile for each kid, and track live games:
  tap buttons for 2PT/3PT makes & misses, free throws, rebounds, assists, steals, blocks,
  turnovers and fouls as the game happens. Stats save to the backend as you go.
- **Kids** get their own Google login. A parent generates a one-time invite code from the
  player's profile; the kid signs in, enters the code on the "Join" page, and from then on
  sees a read-only view of their own stats.
- **Sharing**: anyone (a player profile or a specific game) can be shared via a public link
  - no login required. A live game's share link auto-refreshes every 5 seconds while the
  game is in progress.
- Season averages (PPG/RPG/APG, shooting %) are computed automatically after every game.

## Project Structure

```
StatsHub/
├── backend/                    # .NET Web API
│   └── StatsHub.Api/          # Main API project
├── frontend/                   # React + Vite application
│   ├── src/                   # React components, pages, and styles
│   ├── package.json           # Frontend dependencies
│   └── vite.config.ts         # Vite configuration
├── StatsHub.sln               # .NET Solution file
└── README.md                  # This file
```

## Quick Start

### 1. Backend (ASP.NET Core)

```bash
cd backend/StatsHub.Api
dotnet restore
dotnet run
```

The API runs at `http://localhost:5132` by default (see `Properties/launchSettings.json`)
and creates a local `statshub.db` SQLite file on first run - no separate database server
needed.

### 2. Frontend (React + Vite)

```bash
cd frontend
npm install
npm run dev
```

The frontend runs at `http://localhost:5173` and proxies `/api` requests to the backend.

### 3. Sign in

You have two options while developing:

- **Local dev login** (fastest): on the login page, use the "Local dev login" box with any
  email address. This only works when the backend is running in the `Development`
  environment (the default for `dotnet run`).
- **Real Google sign-in**: create an OAuth Web client at
  [Google Cloud Console](https://console.cloud.google.com/apis/credentials), add
  `http://localhost:5173` as an authorized JavaScript origin, then set:
  - `frontend/.env` → `VITE_GOOGLE_CLIENT_ID=<your client id>`
  - `backend/StatsHub.Api/appsettings.Development.json` (or the `Google__ClientId` env var)
    → the same client ID, so the backend validates tokens issued for it.

Copy `.env.example` → `.env` in both `backend/` and `frontend/` to get started.

## How the pieces fit together

- **Auth**: Google ID tokens (or the dev-login shortcut) are exchanged for a StatsHub JWT
  at `POST /api/auth/google` / `POST /api/auth/dev-login`. The frontend stores the JWT in
  `localStorage` and attaches it as a Bearer token on every API call.
- **Roles**: every user is `Parent` or `Kid`. Parents own players/seasons/games they create.
  Kids are linked to exactly one `Player` via the invite-code flow and only ever get
  read-only access to that player's own data.
- **Live games**: starting a game creates a `Game` + a `GameStats` row, then every tap in
  the Live Game screen recomputes the stat line from a local event log (so "undo" is free)
  and saves it to the backend a few hundred milliseconds later.
- **Sharing**: `POST /api/share` mints a random token tied to a player (and optionally one
  specific game); `GET /api/share/{token}` is the only endpoint that doesn't require login.

## Development

### Requirements

- .NET 10.0 SDK or higher
- Node.js 18+ and npm

### Build

**Backend:**
```bash
cd backend/StatsHub.Api
dotnet build
dotnet publish -c Release
```

**Frontend:**
```bash
cd frontend
npm run build     # type-checks then builds
npm run type-check
npm run lint
```

## Known limitations / next steps

- The database schema is created with `EnsureCreated()` for simplicity - switch to real EF
  Core migrations before you need to evolve the schema without dropping data.
- Live game updates are pulled by the sharer's viewers via polling (every 5s), not pushed;
  a SignalR hub would make this instant if that matters later.
- There's no way yet to add teammates/full team rosters to a game - only the tracked
  player's own stat line is recorded per game.

## License

MIT

<!-- deployment verified 2026-08-20 -->

<!-- vercel auto-deploy retest -->
