# Changelog

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Added — M1
- `src/FieldJobs`: the WPF app, extracted from a private line-of-business
  tracker and genericised — see `PLAN.md` for the exact rename/scrub list
- `FieldJobs.sln`, `build.ps1`, `installer/installer.iss` (new GUID, `v0.1.0`)
- Verified: `dotnet build` clean (0 warnings/errors); the built exe launches,
  initializes its SQLite schema, and seeds two fake demo jobs

### Added — M0
- Repo scaffold, `PLAN.md` (extraction plan), README, LICENSE (MIT)
