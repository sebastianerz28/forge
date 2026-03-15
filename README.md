# Forge

Forge is a system that gets better at building software every time it runs.

It lives in its own repo. You point it at other repos. It does the work there — polling GitHub for issues, claiming them atomically via Postgres, dispatching Claude Code agents, opening PRs, and looping back on review comments. Every run across every target repo is instrumented, and that signal feeds back into how Forge approaches future work.

The automation is the mechanism. The compounding improvement is the point.

## The Core Loop

```
Issue → Claim → Execute → PR → Review → Re-run → better output next time
```

This isn't a task runner that happens to use an LLM. It's a quality ratchet. Metrics from every run — timing, tokens, review cycles, success/fail — feed back into how future runs are prompted and prioritized. Patterns learned fixing a bug in one target repo inform how Forge approaches similar work in another. The system is designed to surface its own failure modes and eliminate them.

## Architecture

Forge is infrastructure you operate, not a tool that lives in your project.

- **Forge repo** — runs centrally; contains all runner logic, coordination, and learned context
- **Target repos** — where the actual work happens; Forge reads the code, opens PRs, and learns from outcomes there
- **GitHub** — work source (issues labeled `forge-ready`) and output destination (PRs) in target repos
- **Postgres** — coordination layer; runners never talk to each other, only to the DB
- **Claude Code CLI** — executor (swappable via `IAgentRunner` interface)
- Multiple runners on different machines can work simultaneously with no double-claiming

**Learning is centralized** — run metrics and outcomes are stored in Forge's own DB, not in target repos. A pattern that works in one codebase can inform runs in another.

Two implementations live side-by-side:

- **`factory/`** — original Python prototype (still functional, reference implementation)
- **`forge/`** — production .NET 10 C# rewrite

## Setup (.NET / Forge)

```bash
# Install .NET 10 SDK
curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0

# Build
cd forge
dotnet build

# Set up Postgres (run migrations if fresh DB)
psql $FORGE_DB_URL -f migrations/001_initial_schema.sql
psql $FORGE_DB_URL -f migrations/002_add_runner_name.sql

# Configure
# Edit forge/src/Forge.Runner/appsettings.json with your target repo details

# Set environment variables
export GITHUB_TOKEN=ghp_...
export FORGE_DB_URL="postgresql://user:pass@host:5432/factory_db"

# Run
cd forge
dotnet run --project src/Forge.Runner
```

## Setup (Python / Factory — legacy)

```bash
pip install -r requirements.txt
cp config.example.yaml config.yaml
# Edit config.yaml with your target repo and connection details

export GITHUB_TOKEN=ghp_...
export FACTORY_DB_URL=postgresql://user:pass@host:5432/factory_db

python scripts/start.py
```

## How It Works

1. **Register** — runner generates/loads a UUID, registers in Postgres, starts heartbeat
2. **Poll** — fetches open issues labeled `forge-ready` from target repos, upserts them as pending tasks
3. **Claim** — atomically claims one pending task (`SELECT ... FOR UPDATE SKIP LOCKED`)
4. **Dispatch** — reads issue body + target repo context + memory files + CLAUDE.md, builds a prompt
5. **Execute** — runs Claude Code CLI against the target repo
6. **PR** — pushes the branch, opens a PR in the target repo linking back to the issue
7. **Review** — polls PRs for new review comments, triggers follow-up runs
8. **Learn** — logs timing, tokens, and success/fail centrally; this data drives future improvements across all target repos

## Solution Structure (Forge)

```
forge/
├── Forge.sln
├── Directory.Build.props
├── src/
│   ├── Forge.Core/           # Models, interfaces, config — no external deps (except Npgsql types)
│   ├── Forge.GitHub/         # GitHub integration via Octokit.net
│   ├── Forge.Coordination/   # EF Core + PostgreSQL (atomic claims, heartbeat, metrics)
│   ├── Forge.Executor/       # Agent execution backends (CLI, API stub)
│   └── Forge.Runner/         # Console app entry point (BackgroundServices, DI, Serilog)
└── tests/
    ├── Forge.Core.Tests/
    ├── Forge.Coordination.Tests/
    ├── Forge.GitHub.Tests/
    └── Forge.Executor.Tests/
```

## Label Lifecycle

Labels live on issues in target repos.

| Label | Meaning |
|---|---|
| `forge-ready` | Issue is ready for pickup |
| `na#<runner-name>` | Currently being worked on by a specific runner |
| `done` | Task completed, issue closed |
| `forge-failed` | Agent failed, needs manual review |

## Design Principles

- **Self-improvement is the goal** — the system should be measurably better next month than it is today; everything else is in service of that
- **Automation is the mechanism, not the mission** — Forge automates so it can learn, not the other way around
- **Learning is centralized** — outcomes from all target repos feed one model of what good work looks like
- **No methodology coupling** — Forge is agnostic; methodology lives in memory files and prompts in target repos
- **Executor is swappable** — `IAgentRunner` interface with CLI (current) and API (future) backends
- **Runners are stateless** — if one dies, its heartbeat stops, tasks get reclaimed automatically
- **Interoperable** — Python Factory and .NET Forge can run against the same Postgres DB