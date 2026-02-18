# Codions MVP — Unattended Coding Agent

An automated coding agent system that accepts task requests via REST API, orchestrates isolated Docker containers to generate code changes using Ollama (local LLM), runs deterministic gates (format/build/test), and outputs GitHub Pull Requests.

## Architecture

```
REST Client → Codions.Api (ASP.NET Core)
                → Orchestrator: normalize request, build context, route model tier
                → SQLite DB: persist job state
                → Codions.Worker (BackgroundService)
                    → Docker: spawn ephemeral bot container per job
                        → Codions.BotHarness (inside container)
                            → Ollama (local LLM): generate code patches
                            → Git: clone, branch, commit, push
                            → GitHub: create Pull Request
```

## Prerequisites

- .NET 10 SDK
- Docker Desktop
- GitHub personal access token (with `repo` scope)
- [Ollama](https://ollama.com) installed and running locally

## Quick Start

### 1. Install Ollama and pull models

```bash
# Install Ollama from https://ollama.com, then pull models:
ollama pull qwen2.5-coder:7b
ollama pull qwen2.5-coder:14b
ollama pull qwen2.5-coder:32b
```

Ollama runs on `http://localhost:11434` by default. The bot containers reach it via `http://host.docker.internal:11434` (Docker Desktop on Windows/Mac).

### 2. Build the bot Docker image

```bash
docker build -t codions-bot:latest -f docker/bot/Dockerfile .
```

### 3. Configure secrets

Set environment variables or edit `appsettings.json` in both Api and Worker projects:

```bash
export GITHUB__Token=ghp_your_token_here
```

No API key is needed for Ollama -- it runs locally.

### 4. Run the API

From repo root (API listens on **port 5005**):

```bash
cd src/Codions.Api
dotnet run
```

Or use the **http** launch profile: `dotnet run --launch-profile http`

### 5. Run the Worker (in a separate terminal)

```bash
cd src/Codions.Worker
dotnet run
```

### 6. Submit a job

```bash
curl -X POST http://localhost:5005/api/jobs \
  -H "Content-Type: application/json" \
  -d '{
    "source": "cli",
    "requester": { "id": "u1", "displayName": "Developer" },
    "repo": {
      "provider": "GitHub",
      "owner": "your-org",
      "name": "your-repo",
      "cloneUrl": "https://github.com/your-org/your-repo.git",
      "defaultBranch": "main"
    },
    "task": {
      "title": "Fix failing test",
      "description": "TestX fails with NullReferenceException. Fix it.",
      "acceptanceCriteria": ["dotnet test tests/Foo.Tests passes"],
      "scopeHints": ["src/Foo", "tests/Foo.Tests"]
    },
    "preferences": {
      "priority": "Normal",
      "modelHint": "balanced",
      "maxMinutes": 25
    }
  }'
```

### 7. Check status

```bash
curl http://localhost:5005/api/jobs/{jobId}
```

---

## Running with Google Chat

To receive tasks from **Google Chat** instead of (or in addition to) the REST API:

1. **Start Codions.Api** (port 5005), then **Codions.ChatAdapter** (port 5006):
   ```bash
   # Terminal 1
   cd src/Codions.Api
   dotnet run

   # Terminal 2
   cd src/Codions.ChatAdapter
   dotnet run
   ```
   ChatAdapter is configured to call the Api at `http://localhost:5005` via `CodionsApi:BaseUrl` in `src/Codions.ChatAdapter/appsettings.json`.

2. **Expose the ChatAdapter for webhooks** (Google must reach your `/webhook` endpoint):
   - **Local:** use [ngrok](https://ngrok.com), e.g. `ngrok http 5006`. You’ll get a public URL like `https://abc123.ngrok.io`.
   - **Hosted:** use your real host and port (e.g. `https://your-app.example.com`).

3. **Configure the Google Chat app** so its webhook URL is:
   ```text
   https://<your-public-host>/webhook
   ```
   Example with ngrok: `https://abc123.ngrok.io/webhook`.

4. **Optional:** set `GoogleChat:VerificationToken` in ChatAdapter’s `appsettings.json` (or secrets) and use the same value in the Google Chat app configuration so the adapter can verify requests.

## Project Structure

| Project | Purpose |
|---------|---------|
| `Codions.Contracts` | Shared DTOs, enums, interfaces |
| `Codions.Core` | Orchestration, model routing, context building |
| `Codions.Infrastructure` | EF Core/SQLite, Docker, GitHub, Ollama |
| `Codions.Api` | REST API gateway |
| `Codions.ChatAdapter` | Google Chat webhook adapter (forwards messages to Api) |
| `Codions.Worker` | Background job processor |
| `Codions.BotHarness` | Console app running inside Docker container |

## Model Tiers

| Tier | Default Model | Use Case |
|------|--------------|----------|
| Cheap | `qwen2.5-coder:7b` | Docs, typos, simple renames |
| Balanced | `qwen2.5-coder:14b` | General tasks (default) |
| Strong | `qwen2.5-coder:32b` | Refactors, security, architecture |

You can customize model names in `appsettings.json` under `Ollama:Models`.

## Security

- Bot containers run as non-root user
- Tokens are injected via environment variables, never logged
- Log output is redacted for known token patterns
- Disallowed paths are enforced by the agent loop
- Audit trail logged per job (requester, repo, branch, PR URL, timestamps)
- Ollama runs locally -- no data leaves your machine for LLM inference
