# Changelog

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added — M2
- `Config/AppConfig.cs`: loads `appsettings.json` (shipped) + `appsettings.Local.json`
  (gitignored override, section-wholesale-replace merge)
- `Vocab` is now config-driven: project types, job statuses, ref/client field
  labels, agency/client party labels, brand name, and the three attention-rule
  day counts all come from config instead of being hard-coded
- A second stage template (`"basic-3"`) alongside the original 8-stage one,
  selected via `stageTemplate` in config
- Fixed a bug from M1's demo-data rewrite: a hardcoded `'Waiting on agency'`
  seed status didn't match the (now config-generated) `"Waiting on Agency"` —
  found by actually re-running the seeded app and checking the DB
- Verified: default config seeds 8 stages; `{"stageTemplate":"basic-3"}` seeds
  exactly 3; the original METEC/IHDA label set (as a Local override) round-trips
  correctly into the seeded job's status

### Added — M1
- `src/FieldJobs`: the WPF app, extracted from a private line-of-business
  tracker and genericised — see `PLAN.md` for the exact rename/scrub list
- `FieldJobs.sln`, `build.ps1`, `installer/installer.iss` (new GUID, `v0.1.0`)
- Verified: `dotnet build` clean (0 warnings/errors); the built exe launches,
  initializes its SQLite schema, and seeds two fake demo jobs

### Added — M0
- Repo scaffold, `PLAN.md` (extraction plan), README, LICENSE (MIT)
