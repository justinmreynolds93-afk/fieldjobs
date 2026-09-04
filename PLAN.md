# fieldjobs — extraction plan

`fieldjobs` is a generalised, open-source version of a private line-of-business
app ("METEC Job Tracker") that tracks multi-stage field-service jobs —
inspections, scoping, walkthroughs, per-stage invoicing, payments, document
capture, PDF reports.

**Nothing is copied until you sign off on this file.**

## Source app, in one paragraph

A WPF / .NET 8 desktop app (Dapper + SQLite, MVVM, services layer, PDF export).
A *job* moves through eight **gated stages** (assignment → preview → scope →
submitted → interim → final walkthrough → punch → closeout); each stage has an
editable checklist copied from a template, can have files attached, and can be
invoiced against a fee schedule. Payments are recorded against invoices. A
dashboard splits jobs by "who we're waiting on" and flags stale jobs / overdue
invoices / backup nags. Two PDFs: a job summary and an invoice.

The architecture is already clean and domain-neutral in most places — this is an
extraction, not a rewrite.

## Stack decision

**Keep WPF / .NET 8.** It's the fastest path to a real deliverable, the code is
already MVVM + services, and it adds .NET/desktop breadth next to the TS and
Python repos. *Follow-up option, not now:* an Avalonia port (same XAML/MVVM
knowledge, cross-platform) — noted in the repo's roadmap.

## Rename / genericise

| METEC-specific | fieldjobs |
|---|---|
| `MetecNumber`, `IhdaNumber` on `Job` | `ExternalRef1`, `ExternalRef2` + configurable labels |
| `HomeownerName` | `ClientName` |
| `ProjectType` default `"Full Rehabilitation"`; values Accessibility / Roof-Only | configurable list, generic defaults ("Standard", "Priority", "Other") |
| `Vocab.WaitingOn` → `"METEC"` / `"Homeowner"` | configurable party names ("Agency" / "Client" defaults) |
| Stage template names ("Scope submitted to METEC") | generic ("Scope submitted") |
| Fee schedule amounts ($550 preview, $250 final) | $0 defaults, sample values in docs only |
| `SeedData.cs` demo jobs (addresses, names) | 3 obviously-fake demo jobs ("123 Example St", "Sample Client") |
| App name, window titles, installer IDs, namespaces `MetecJobTracker.*` | `FieldJobs.*` |

## What stays as-is (already generic)

`Stage` / `ChecklistItem` / `JobFile` / `Invoice` / `InvoiceLine` / `Payment` /
`Contact` / `ActivityEntry` / `FeeScheduleItem` / `StageTemplate` /
`ChecklistTemplate`; the gated-stage engine; the dashboard "attention" rules
(stale-job / overdue-invoice / backup-nag thresholds → move to config);
Dapper data layer; PDF kit; MVVM infra.

## Config layer (new)

One `appsettings.json` (shipped generic, gitignored override) that carries every
former hard-coded label:

```jsonc
{
  "brand":        { "appName": "FieldJobs", "org": "" },
  "job":          { "ref1Label": "Ref #", "ref2Label": "Program #", "clientLabel": "Client" },
  "parties":      { "agency": "Agency", "client": "Client" },
  "projectTypes": ["Standard", "Priority", "Other"],
  "rules":        { "overdueDays": 30, "staleJobDays": 21, "backupNagDays": 10 },
  "stageTemplate": "inspection-8"   // or "basic-3"
}
```

Your real METEC values live in a local `appsettings.Local.json` that the repo
ignores — so the private config never leaves your machine.

## Scrub checklist (before first commit)

- [ ] no real addresses / homeowner names / phone / email anywhere (grep `SeedData.cs`, tests, docs, screenshots)
- [ ] no real fee amounts (your rates) — defaults to `0`
- [ ] no METEC / IHDA / program identifiers in code, comments, commit messages, or the installer
- [ ] screenshots (if any) use demo data
- [ ] `git log` of the new repo starts clean (fresh history, not a filter-branch of the private one)

## Milestones

- **M0** — repo scaffold, this plan, README, LICENSE *(done)*
- **M1** — copy the solution, rename namespaces/app, `dotnet build` green *(done)*
- **M2** — config layer; every former hard-coded label reads from `appsettings.json` *(done)*
- **M3** — genericise `SeedData.cs` (fake demo data) + the two stage templates *(done as part of M1 — see below)*
- **M4** — CI (`dotnet build` + `dotnet test`) + Inno Setup installer with generic IDs
- **M5** — README with screenshots (demo data), a short "why gated stages" design note

### M2 notes (2026-09-05)

`Config/AppConfig.cs` loads `appsettings.json` (shipped, generic — committed) then
layers `appsettings.Local.json` (gitignored) on top; each top-level JSON section
present in a file replaces that section wholesale, so a Local file only needs to
specify what it's overriding. `App.xaml.cs` calls `Vocab.Configure(AppConfig.Load())`
before `Db.Initialize()`, so `Vocab` — already the single source of truth for
status/type dropdowns since M1 — now derives `ProjectTypes`, `JobStatuses`,
`Ref1Label`/`Ref2Label`/`ClientFieldLabel`, `AgencyPartyLabel`/`ClientPartyLabel`,
`BrandAppName`/`BrandOrg`, and the three `Rules` day-counts from config instead
of being hard-coded. Wired into every label that showed one of these (PDF
headers, XLSX/CSV export headers, the job form, the jobs grid, the dashboard,
settings) — mostly pure C# string swaps since `Vocab` was already the
abstraction boundary from M1; the few XAML labels needed an `x:Name` + a
code-behind line.

Also added a second stage template (`"basic-3"`: assignment → work → closeout)
selected by `stageTemplate` in config — `SeedData.Stages`/`Checklists` are now
properties that switch on `AppConfig.Instance.StageTemplate` instead of fixed
fields.

**Bug found and fixed during verification:** M1's demo-data rewrite had hardcoded
job 2's seed status as the SQL literal `'Waiting on agency'` (lowercase). Once
`Vocab.JobStatuses` became config-driven it generates `"Waiting on Agency"`
(Title Case) by default — the literal no longer matched anything in the
dropdown, so the demo job's status wouldn't have round-tripped through the
Status combo box correctly. Fixed by parameterising it as
`$"Waiting on {Vocab.AgencyPartyLabel}"` so it always matches whatever's
configured. Caught by actually re-running the seeded app and checking the DB,
not by reading the diff — the lesson from M1's own "test against live data"
principle held up a second time.

**Verified, not just built:**
- `dotnet build` → 0 warnings / 0 errors
- Ran the exe with the shipped `appsettings.json`: 8 inspection stages seed, job
  statuses match `Vocab.JobStatuses` exactly
- Ran it again with `appsettings.Local.json` set to `{"stageTemplate":"basic-3"}`:
  exactly 3 stages seed (`assignment`/`work`/`closeout`) — proves the branch is real
- Ran it a third time with the full `appsettings.Local.json.example` (the
  original METEC/IHDA labels, as an override) copied to `appsettings.Local.json`:
  seeded job status came back as `"Waiting on METEC"` — proves the party-label
  override propagates end-to-end, not just into the dropdown list

**Known limitation, not fixed:** the two seed *demo jobs* still use fixed
`project_type` values (`"Standard"`, `"Priority"`) and are written against the
inspection-8 stage keys. Under a customised `projectTypes` list or
`stageTemplate: "basic-3"` the demo jobs still create fine, they just don't look
as polished (a project type outside your configured list; stages that stay
"Not started" instead of mid-workflow, since the `SetStage(...,"preview",...)`
calls target inspection-8-only keys). Per-template/per-config demo data is a
reasonable follow-up, not required for the config layer itself to be correct.

### M1 notes (2026-09-04)

Copied `src/MetecJobTracker` → `src/FieldJobs`, renamed the namespace/csproj/sln/
installer/assembly, and genericised in the same pass since M3's scope turned out
to be inseparable from a correct rename (`MetecNumber`/`IhdaNumber`/
`HomeownerName` are both C# identifiers *and* the strings shown to a user):

- `MetecNumber`/`IhdaNumber`/`HomeownerName` → `ExternalRef1`/`ExternalRef2`/`ClientName`
  (and the matching `metec_number`/`ihda_number`/`homeowner_name` DB columns)
- `Vocab.ProjectTypes`: `Full Rehabilitation/Accessibility/Roof-Only/Other` →
  `Standard/Priority/Other`; `Vocab.JobStatuses` + `WaitingOn()`:
  `Waiting on METEC/homeowner` → `Waiting on agency/client`
- Every "METEC"/"IHDA"/"HRAP" string (labels, PDF headers, contact seed, bill-to)
  → "Agency" / "Ref #" / "Program #" as appropriate
- `SeedData.cs` demo data fully replaced: fake client names, `123 Example St` /
  `456 Sample Ave` addresses, generic reference numbers, a $100 placeholder
  invoice amount instead of the real fee schedule; **fee_schedule defaults to $0**
  (your real rates go in Settings, they're not shipped in the repo)
- New installer GUID (`FDB821F1-...`), version reset to `0.1.0`
- Verified: `dotnet build FieldJobs.sln` → 0 warnings / 0 errors; ran the built
  exe, confirmed it seeds ~2 MB of demo data with no crash

Not yet ported: `docs/ARCHITECTURE.md` and friends, and the sample CSV export —
deferred so M1 stayed scoped to "code builds and runs clean."

## What I need from you

1. ~~Confirm keep-WPF~~ — confirmed, keeping WPF.
2. ~~Confirm fresh git history~~ — confirmed (this repo was `git init`'d standalone).
3. ~~Anything more sensitive than addresses/names/rates~~ — answered: just fake
   names, no other special sensitivity. Noted and scrubbed.
