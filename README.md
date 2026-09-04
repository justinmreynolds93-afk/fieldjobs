# fieldjobs

A desktop tracker for multi-stage field-service work — inspections, scoping,
walkthroughs — with gated stage checklists, per-stage invoicing, payment
tracking, document capture, and PDF reports. WPF / .NET 8, SQLite, no server.

> **Status: M1 — builds.** A generalised, open-source extraction of a private
> line-of-business app; see [PLAN.md](PLAN.md) for exactly what was renamed and
> genericised (and the M2+ roadmap: a config layer, CI, the installer).

![license](https://img.shields.io/badge/license-MIT-blue)
![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![status](https://img.shields.io/badge/status-M1%20builds-yellow)

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

Everything program-specific (labels, parties, thresholds, stage template, fee
amounts) is meant to be configuration, not code — an `appsettings.json` layer
lands in M2 (see [PLAN.md](PLAN.md)). For now those values are generic defaults
baked into `Vocab.cs` / `SeedData.cs`.

## Build

```
dotnet build FieldJobs.sln
dotnet run --project src/FieldJobs
```

Requires the .NET 8 SDK (Windows — it's WPF). First run seeds two obviously-fake
demo jobs into `%LOCALAPPDATA%\FieldJobs\fieldjobs.db`.

## License

[MIT](LICENSE)
