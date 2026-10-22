# Project1 — Purchasing Management System

Vue.js · ASP.NET Core · SQL Server

## Live Demo

[Open Live Demo](https://shunbin2310.github.io/Project1/)

## Availability

Available while the server is running. Offline after midnight (Malaysia Time, UTC+8).

## Run Locally with Docker

Use `compose.local.yml` to run Project1 on your computer when the Ubuntu demo server
is unavailable. It runs the frontend, API, SQL Server, and Mailpit independently of
Ubuntu. This is a local development/demo environment with its own
`Project1DockerPracticeDb` database; it does not automatically copy or synchronize
the Ubuntu server's data, or switch the public demo link to your computer.

### Requirements

- Docker Desktop running with **Linux containers** on an x64 Windows computer.
- Docker Compose v2 with `--wait` support.
- The repository checked out locally, and internet access for the first image build.
- Run the commands below in **PowerShell at the repository root**.

### First-time Setup

Run each command separately and continue only after it succeeds. If you have already
completed setup and have no pending database migrations, use **Daily Startup** below.

**1. Create the local configuration, if it does not already exist.**

```powershell
if (-not (Test-Path -LiteralPath '.env.docker.local')) {
    Copy-Item -LiteralPath '.env.docker.example' -Destination '.env.docker.local'
}
```

Open `.env.docker.local` in your editor and fill in these values:

| Variable | Value |
| --- | --- |
| `SQL_PASSWORD` | A new local SQL password with at least 16 characters, including uppercase, lowercase, a digit, and a symbol. Use only letters, digits, and `! @ # _ -` for this connection string. |
| `DEMO_PASSWORD` | A local demo account password with at least 16 characters, including uppercase, lowercase, a digit, and a symbol. |
| `JWT_SIGNING_KEY` | A random signing key with at least 64 characters. |

Keep this file private and retain its existing values on subsequent starts. Do not
reuse production passwords or commit the file. Check the configuration without
printing its secrets:

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml config --quiet
```

**2. Start SQL Server and wait until it is healthy.**

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml up -d --wait sqlserver
```

**3. Build the application and database migration tool.**

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml --profile tools build api frontend migrate
```

**4. Apply the existing migrations to the local database.**

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml --profile tools run --rm migrate
```

This creates or updates the local database schema. Wait for successful completion
before continuing. The API in this Compose file does not automatically apply
migrations, and a normal `up` does not run the `migrate` service.

**5. Start the application and check its status.**

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml up -d --wait
```

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml ps
```

Open [Project1](http://localhost:8081) and sign in with `admin@demo.local` and the
`DEMO_PASSWORD` from your local configuration. Enter the password manually; the
login page's **Use account** shortcut may use a different default password.

| Service | Local address |
| --- | --- |
| Project1 website | <http://localhost:8081> |
| Captured test emails (Mailpit) | <http://localhost:8026> |
| API OpenAPI document | <http://localhost:5166/openapi/v1.json> |

The published ports bind to `127.0.0.1`, so this setup is intended for access on
the computer running Docker. Mailpit captures test emails locally instead of
sending them to real recipients. After startup, verify login and opening a business
page; healthy containers alone do not verify every application operation.

### Daily Startup

Start Docker Desktop, wait for its engine to be running, then execute:

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml up -d
```

Open <http://localhost:8081>. Existing images and database data are reused; there
is no need to rebuild when the code has not changed.

### Rebuild After Code Changes

For code changes that do not require database migrations:

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml up -d --build
```

This rebuilds the application images and starts or updates the containers. `build`
on its own only builds images. If an update includes new EF migrations, stop the
application using the command below, then repeat first-time setup steps 2–5 to
build the current migration tool and apply the migrations before starting the API.

### Shut Down

```powershell
docker compose --env-file .env.docker.local -f compose.local.yml down
```

This stops and removes the project containers and network. Images and named volumes
remain, so the local database and captured emails are retained for the next startup.
**Do not add `-v` unless you intend to delete those volumes and their data.**

For more detailed local setup and troubleshooting, see
[Project Notes](docs/PROJECT_NOTES.md). Docker command references:
[up](https://docs.docker.com/reference/cli/docker/compose/up/),
[run](https://docs.docker.com/reference/cli/docker/compose/run/), and
[down](https://docs.docker.com/reference/cli/docker/compose/down/).
