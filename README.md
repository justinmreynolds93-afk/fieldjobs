# fieldjobs

A desktop tracker for multi-stage field-service work — inspections, scoping,
walkthroughs — with gated stage checklists, per-stage invoicing, payment
tracking, document capture, and PDF reports. WPF / .NET 8, SQLite, no server.

> **Status: M4 — CI + installer.** A generalised, open-source extraction of a
> private line-of-business app; see [PLAN.md](PLAN.md) for exactly what was
> renamed/genericised. M5 (README screenshots) is all that's left.

![ci](https://github.com/justinmreynolds93-afk/fieldjobs/actions/workflows/ci.yml/badge.svg)
![license](https://img.shields.io/badge/license-MIT-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)

## The idea

A lot of field work is the same shape: a job comes in, you inspect, you write a
scope, someone approves it, work happens, you walk it, you punch it, you close
it — and you invoice at a few points along the way. `fieldjobs` models that as:

- **Jobs** with an address, a client, external reference numbers (labels are yours)
- **Gated stages** — a stage unlocks only when the previous non-skipped stage is
  complete; optional stages can be marked N/A
- **Per-stage checklists** copied from an editable template, so every job starts
  consistent but can diverge
- **Invoices** raised against a fee schedule, **payments** recorded against them
- **Files** attached to a job or a specific stage (scope PDFs, photos, proof of payment)
- A **dashboard** that splits jobs by who you're waiting on and nags about stale
  jobs, overdue invoices, and missing backups
- **PDF export** — a job summary and an invoice

Everything program-specific — labels, parties, project types, the day-count
rules, even which stage template to use — is configuration, not code. Copy
`src/FieldJobs/appsettings.Local.json.example` to `appsettings.Local.json` (next
to the exe; gitignored) and override only what you need:

```jsonc
{
  "job": { "ref1Label": "PO #", "clientLabel": "Tenant" },
  "parties": { "agency": "Property Manager", "client": "Tenant" },
  "stageTemplate": "basic-3"
}
```

Fee amounts are the one thing that's *never* in a config file, shipped or
local — they default to $0 and you set your real rates in Settings, so they
never end up in git even by accident.

## Build

```
dotnet build FieldJobs.sln
dotnet run --project src/FieldJobs
```

Requires the .NET 8 SDK (Windows — it's WPF). First run seeds two obviously-fake
demo jobs into `%LOCALAPPDATA%\FieldJobs\fieldjobs.db`.

## Test

```
dotnet test FieldJobs.sln
```

16 xunit tests over the config layer and the seeding logic, run against a
throwaway in-memory SQLite connection — no real app data involved. CI runs
these on every push.

## Installer

```powershell
.\build.ps1                 # dotnet publish (self-contained win-x64) + Inno Setup
.\build.ps1 -NoInstaller     # skip the Inno Setup step
```

Needs the .NET 8 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php)
(`winget install JRSoftware.InnoSetup`). Produces
`dist/FieldJobs-Setup-<version>.exe` — a ~50 MB self-contained installer, no
.NET runtime required on the target machine. CI builds this on every push too
(see the Actions tab for the artifact).

## License

[MIT](LICENSE)
