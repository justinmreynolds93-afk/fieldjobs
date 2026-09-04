using Dapper;
using FieldJobs.Config;
using FieldJobs.Models;
using Microsoft.Data.Sqlite;

namespace FieldJobs.Data;

/// <summary>
/// First-run defaults: fee schedule, the gated stages for the configured stage
/// template ("inspection-8" or "basic-3" — see appsettings.json), the checklist
/// templates, and two fake demo jobs so the app is not empty on install.
/// Nothing here overwrites data the user has already changed.
/// </summary>
public static class SeedData
{
    public record StageDef(int Seq, string Key, string Name, bool Optional);

    /// <summary>The default 8-stage inspection/scope/walkthrough workflow.</summary>
    public static readonly StageDef[] Inspection8Stages =
    {
        new(1, "assignment",        "Assignment / intake",                  false),
        new(2, "preview",           "Preview / initial inspection",         false),
        new(3, "scope_writing",     "Scope writing",                        false),
        new(4, "scope_submitted",   "Scope submitted to Agency",             false),
        new(5, "work_in_progress",  "Work in progress / interim visit",     true),
        new(6, "final_walkthrough", "Final walkthrough",                    false),
        new(7, "punch",             "Punch / follow-up",                    true),
        new(8, "closeout",          "Job closeout",                         false),
    };

    /// <summary>A minimal 3-stage workflow for simpler work that doesn't need scoping/punch.</summary>
    public static readonly StageDef[] Basic3Stages =
    {
        new(1, "assignment", "Assignment / intake", false),
        new(2, "work",       "Work in progress",    false),
        new(3, "closeout",   "Job closeout",         false),
    };

    /// <summary>Stages for whichever template <c>appsettings.json</c>'s <c>stageTemplate</c> selects.</summary>
    public static StageDef[] Stages =>
        AppConfig.Instance.StageTemplate == "basic-3" ? Basic3Stages : Inspection8Stages;

    private static readonly Dictionary<string, string[]> Inspection8Checklists = new()
    {
        ["assignment"] = new[]
        {
            "Assignment received from Agency",
            "Job / agency reference numbers recorded",
            "Property address and client confirmed",
            "Scope of my involvement agreed (inspection / scope / walkthrough)",
            "Target dates noted",
        },
        ["preview"] = new[]
        {
            "Site visit completed",
            "Photos taken",
            "Health / safety / code notes captured",
            "Accessibility needs noted",
            "Roof / envelope notes captured",
            "Preview report saved",
        },
        ["scope_writing"] = new[]
        {
            "Preview complete (required gate)",
            "Line-item scope drafted",
            "Allowances / notes added",
            "Scope PDF saved",
            "Scope sent to Agency",
        },
        ["scope_submitted"] = new[]
        {
            "Scope package emailed / uploaded to Agency",
            "Submission date recorded",
            "Confirmation of receipt from Agency",
        },
        ["work_in_progress"] = new[]
        {
            "Interim site visit completed",
            "Progress photos taken",
            "Change orders / field issues noted",
            "Interim notes saved",
        },
        ["final_walkthrough"] = new[]
        {
            "Work compared to approved scope",
            "Punch items listed",
            "Photos of completed work",
            "Final notes saved",
            "Ready to invoice final",
        },
        ["punch"] = new[]
        {
            "Punch list sent to contractor / Agency",
            "Re-inspection of punch items completed",
            "Punch items photographed as resolved",
        },
        ["closeout"] = new[]
        {
            "All my invoices submitted",
            "All payments received and recorded",
            "Files archived for this job",
            "Job marked complete",
        },
    };

    private static readonly Dictionary<string, string[]> Basic3Checklists = new()
    {
        ["assignment"] = new[]
        {
            "Job details recorded",
            "Client confirmed",
            "Target dates noted",
        },
        ["work"] = new[]
        {
            "Site visit completed",
            "Photos taken",
            "Progress notes saved",
        },
        ["closeout"] = new[]
        {
            "Invoice submitted",
            "Payment received",
            "Files archived for this job",
            "Job marked complete",
        },
    };

    /// <summary>Checklists for whichever template <c>appsettings.json</c>'s <c>stageTemplate</c> selects.</summary>
    public static Dictionary<string, string[]> Checklists =>
        AppConfig.Instance.StageTemplate == "basic-3" ? Basic3Checklists : Inspection8Checklists;

    // Zeroed by default (a fresh install has no idea what you charge) — set your
    // real rates in Settings. See docs/STAGE-TEMPLATES.md for example figures.
    public static readonly (string Label, double Amount, int Sort)[] Fees =
    {
        ("Preview / initial inspection", 0, 1),
        ("Final walkthrough",            0, 2),
        ("Scope writing",                0, 3),
        ("Extra visit / change-order review", 0, 4),
    };

    public static void EnsureDefaults(SqliteConnection c)
    {
        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM stage_templates") == 0)
        {
            foreach (var s in Stages)
                c.Execute(
                    "INSERT INTO stage_templates(seq, stage_key, name, is_optional) VALUES (@Seq,@Key,@Name,@Optional)",
                    new { s.Seq, s.Key, s.Name, Optional = s.Optional ? 1 : 0 });
        }

        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM checklist_templates") == 0)
        {
            foreach (var (key, items) in Checklists)
                for (var i = 0; i < items.Length; i++)
                    c.Execute(
                        "INSERT INTO checklist_templates(stage_key, text, seq) VALUES (@key,@text,@seq)",
                        new { key, text = items[i], seq = i });
        }

        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM fee_schedule") == 0)
        {
            foreach (var (label, amount, sort) in Fees)
                c.Execute("INSERT INTO fee_schedule(label, amount, sort_order) VALUES (@label,@amount,@sort)",
                    new { label, amount, sort });
        }

        SettingsDefaults(c);

        if (c.ExecuteScalar<long>("SELECT COUNT(*) FROM contacts") == 0)
        {
            c.Execute("""
                INSERT INTO contacts(name, role, email, phone, is_primary, sort_order)
                VALUES ('Sample Contact', 'Agency — Program Coordinator', 'contact@example.com', '(555) 555-0100', 1, 0)
                """);
        }

        if (c.ExecuteScalar<string?>("SELECT value FROM settings WHERE key='sample_data_loaded'") != "yes"
            && c.ExecuteScalar<long>("SELECT COUNT(*) FROM jobs") == 0)
        {
            SeedSampleJobs(c);
            c.Execute("INSERT OR REPLACE INTO settings(key,value) VALUES ('sample_data_loaded','yes')");
        }
    }

    private static void SettingsDefaults(SqliteConnection c)
    {
        var defaults = new Dictionary<string, string>
        {
            ["business_name"] = "Your Name / Business",
            ["dba"] = "",
            ["address"] = "",
            ["email"] = "",
            ["phone"] = "",
            ["backup_folder"] = AppPaths.DefaultBackupFolder,
            ["theme"] = "System",
            ["invoice_prefix"] = "FJ-",
            ["payment_terms"] = "Net 30",
            ["bill_to"] = "",
        };
        foreach (var (k, v) in defaults)
            c.Execute("INSERT OR IGNORE INTO settings(key,value) VALUES (@k,@v)", new { k, v });
    }

    // Written against the inspection-8 stage keys. Under stageTemplate "basic-3"
    // the jobs still get created (with basic-3's stages), but the SetStage calls
    // below that reference "preview"/"scope_writing"/etc. simply match nothing —
    // both demo jobs land at "Not started" instead of mid-workflow. Harmless, just
    // a less impressive demo; a per-template sample dataset is a nice follow-up.
    private static void SeedSampleJobs(SqliteConnection c)
    {
        var now = DateTime.Now.ToString("s");

        // ---- Job 1: fully through Preview so Scope Writing is unlocked ----
        var j1 = c.ExecuteScalar<long>("""
            INSERT INTO jobs(job_number, external_ref1, external_ref2, address_street, address_city, address_zip,
                             client_name, project_type, date_assigned, role_notes, status, notes, created_at, updated_at)
            VALUES ('2026-014', 'REF-1001', 'PRG-1001', '123 Example St', 'Anytown', '00000',
                    'Sample Client A', 'Standard', @assigned,
                    'Inspection, scope writing, final walkthrough.', 'In progress',
                    'Older two-story home. Some deferred maintenance noted in the rear bedrooms.', @now, @now);
            SELECT last_insert_rowid();
            """, new { assigned = DateTime.Today.AddDays(-24).ToString("yyyy-MM-dd"), now });

        CreateStagesFor(c, j1);
        SetStage(c, j1, "assignment", "Complete", -24, allChecked: true);
        SetStage(c, j1, "preview", "Complete", -17, allChecked: true);
        SetStage(c, j1, "scope_writing", "In progress", null, checkFirst: 2);

        var inv1 = c.ExecuteScalar<long>("""
            INSERT INTO invoices(job_id, invoice_number, invoice_date, date_sent, sent_to,
                                 stage_key, stage_label, status, notes, created_at)
            VALUES (@job, 'FJ-1001', @date, @sent, 'Sample Contact (Agency)', 'preview',
                    'Preview / initial inspection', 'Sent',
                    'Initial inspection and preview report.', @now);
            SELECT last_insert_rowid();
            """, new
        {
            job = j1,
            date = DateTime.Today.AddDays(-38).ToString("yyyy-MM-dd"),
            sent = DateTime.Today.AddDays(-35).ToString("yyyy-MM-dd"),
            now
        });
        c.Execute("INSERT INTO invoice_lines(invoice_id, description, qty, rate, seq) VALUES (@i,'Preview / initial inspection',1,100,0)", new { i = inv1 });

        // ---- Job 2: priority job, further along, one paid invoice + one draft ----
        // status is parameterised (not a string literal) so it always matches
        // whatever Vocab.JobStatuses currently contains — see the M2 bug note below.
        var j2 = c.ExecuteScalar<long>("""
            INSERT INTO jobs(job_number, external_ref1, external_ref2, address_street, address_city, address_zip,
                             client_name, project_type, date_assigned, role_notes, status, notes, created_at, updated_at)
            VALUES ('2026-009', 'REF-1002', 'PRG-1002', '456 Sample Ave', 'Somewhere', '00000',
                    'Sample Client B', 'Priority', @assigned,
                    'Inspection and final walkthrough only. Agency wrote the scope.', @status,
                    'Ramp and bathroom modifications. Client uses a mobility aid; requested a roll-in shower.', @now, @now);
            SELECT last_insert_rowid();
            """, new
        {
            assigned = DateTime.Today.AddDays(-52).ToString("yyyy-MM-dd"),
            status = $"Waiting on {Vocab.AgencyPartyLabel}",
            now
        });

        CreateStagesFor(c, j2);
        SetStage(c, j2, "assignment", "Complete", -52, allChecked: true);
        SetStage(c, j2, "preview", "Complete", -45, allChecked: true);
        SetStage(c, j2, "scope_writing", "N/A", null);
        SetStage(c, j2, "scope_submitted", "Complete", -30, allChecked: true);
        SetStage(c, j2, "work_in_progress", "N/A", null);
        SetStage(c, j2, "final_walkthrough", "In progress", null, checkFirst: 1);

        var inv2 = c.ExecuteScalar<long>("""
            INSERT INTO invoices(job_id, invoice_number, invoice_date, date_sent, sent_to,
                                 stage_key, stage_label, status, notes, created_at)
            VALUES (@job, 'FJ-0994', @date, @sent, 'Sample Contact (Agency)', 'preview',
                    'Preview / initial inspection', 'Paid', 'Initial inspection.', @now);
            SELECT last_insert_rowid();
            """, new
        {
            job = j2,
            date = DateTime.Today.AddDays(-42).ToString("yyyy-MM-dd"),
            sent = DateTime.Today.AddDays(-40).ToString("yyyy-MM-dd"),
            now
        });
        c.Execute("INSERT INTO invoice_lines(invoice_id, description, qty, rate, seq) VALUES (@i,'Preview / initial inspection',1,100,0)", new { i = inv2 });
        c.Execute("""
            INSERT INTO payments(invoice_id, payment_date, amount, method, reference, note)
            VALUES (@i, @d, 100, 'ACH / direct deposit', 'ACH batch 0001-A', 'Payment from Agency')
            """, new { i = inv2, d = DateTime.Today.AddDays(-12).ToString("yyyy-MM-dd") });

        var inv3 = c.ExecuteScalar<long>("""
            INSERT INTO invoices(job_id, invoice_number, invoice_date, stage_key, stage_label, status, notes, created_at)
            VALUES (@job, 'FJ-1007', @date, 'final_walkthrough', 'Final walkthrough', 'Draft',
                    'Final walkthrough — pending completion.', @now);
            SELECT last_insert_rowid();
            """, new { job = j2, date = DateTime.Today.ToString("yyyy-MM-dd"), now });
        c.Execute("INSERT INTO invoice_lines(invoice_id, description, qty, rate, seq) VALUES (@i,'Final walkthrough',1,100,0)", new { i = inv3 });

        SeedActivity(c, j1, j2, inv1, inv2);
    }

    private static void SeedActivity(SqliteConnection c, long j1, long j2, long inv1, long inv2)
    {
        void A(long job, int daysAgo, string kind, string summary) => c.Execute(
            "INSERT INTO activity(job_id, ts, kind, summary) VALUES (@job, @ts, @kind, @summary)",
            new { job, ts = DateTime.Now.AddDays(-daysAgo).ToString("s"), kind, summary });

        A(j2, 52, "job_created", "Job created — 456 Sample Ave");
        A(j2, 45, "stage_completed", "Stage “Preview / initial inspection” completed");
        A(j2, 42, "invoice_created", "Invoice FJ-0994 created ($100) for Preview / initial inspection");
        A(j2, 40, "invoice_sent", "Invoice FJ-0994 marked sent to Sample Contact (Agency)");
        A(j2, 30, "stage_completed", "Stage “Scope submitted to Agency” completed");
        A(j2, 12, "payment_recorded", "Payment $100.00 recorded on FJ-0994 (ACH / direct deposit)");
        A(j1, 24, "job_created", "Job created — 123 Example St");
        A(j1, 17, "stage_completed", "Stage “Preview / initial inspection” completed");
        A(j1, 38, "invoice_created", "Invoice FJ-1001 created ($100) for Preview / initial inspection");
        A(j1, 35, "invoice_sent", "Invoice FJ-1001 marked sent to Sample Contact (Agency)");
        _ = (inv1, inv2);
    }

    private static void CreateStagesFor(SqliteConnection c, long jobId)
    {
        var templates = c.Query<(int seq, string stage_key, string name, long is_optional)>(
            "SELECT seq, stage_key, name, is_optional FROM stage_templates ORDER BY seq").ToList();

        foreach (var t in templates)
        {
            var stageId = c.ExecuteScalar<long>("""
                INSERT INTO stages(job_id, seq, stage_key, name, is_optional, status)
                VALUES (@jobId, @seq, @key, @name, @opt, 'Not started');
                SELECT last_insert_rowid();
                """, new { jobId, seq = t.seq, key = t.stage_key, name = t.name, opt = t.is_optional });

            var items = c.Query<(string text, int seq)>(
                "SELECT text, seq FROM checklist_templates WHERE stage_key=@k ORDER BY seq",
                new { k = t.stage_key }).ToList();
            foreach (var it in items)
                c.Execute("INSERT INTO checklist_items(stage_id, text, is_checked, seq) VALUES (@s,@t,0,@q)",
                    new { s = stageId, t = it.text, q = it.seq });
        }
    }

    private static void SetStage(SqliteConnection c, long jobId, string key, string status,
        int? daysAgo, bool allChecked = false, int checkFirst = 0)
    {
        var date = daysAgo is null ? null : DateTime.Today.AddDays(daysAgo.Value).ToString("yyyy-MM-dd");
        c.Execute("UPDATE stages SET status=@status, date_completed=@date WHERE job_id=@job AND stage_key=@key",
            new { status, date, job = jobId, key });

        var stageId = c.ExecuteScalar<long>("SELECT id FROM stages WHERE job_id=@job AND stage_key=@key",
            new { job = jobId, key });

        if (allChecked)
            c.Execute("UPDATE checklist_items SET is_checked=1 WHERE stage_id=@s", new { s = stageId });
        else if (checkFirst > 0)
            c.Execute("""
                UPDATE checklist_items SET is_checked=1
                WHERE id IN (SELECT id FROM checklist_items WHERE stage_id=@s ORDER BY seq LIMIT @n)
                """, new { s = stageId, n = checkFirst });
    }
}
