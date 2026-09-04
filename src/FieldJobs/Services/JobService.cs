using Dapper;
using FieldJobs.Data;
using FieldJobs.Models;

namespace FieldJobs.Services;

public sealed class JobService
{
    private readonly StageService _stages = new();
    private readonly ActivityService _activity = new();

    public Job Get(long id)
        => Db.Connection.QuerySingle<Job>("SELECT * FROM jobs WHERE id=@id", new { id });

    public List<Job> All()
        => Db.Connection.Query<Job>("SELECT * FROM jobs ORDER BY updated_at DESC").ToList();

    /// <summary>Create a job and stamp out its eight stages + checklists from the templates.</summary>
    public long Create(Job job)
    {
        var now = DateTime.Now.ToString("s");
        long id = 0;
        Db.InTransaction(_ =>
        {
            id = Db.Connection.ExecuteScalar<long>("""
                INSERT INTO jobs(job_number, external_ref1, external_ref2, address_street, address_city, address_zip,
                                 client_name, project_type, date_assigned, role_notes, status, notes,
                                 created_at, updated_at)
                VALUES (@JobNumber,@ExternalRef1,@ExternalRef2,@AddressStreet,@AddressCity,@AddressZip,
                        @ClientName,@ProjectType,@DateAssigned,@RoleNotes,@Status,@Notes,@now,@now);
                SELECT last_insert_rowid();
                """, new
            {
                job.JobNumber, job.ExternalRef1, job.ExternalRef2, job.AddressStreet, job.AddressCity,
                job.AddressZip, job.ClientName, job.ProjectType, job.DateAssigned, job.RoleNotes,
                job.Status, job.Notes, now
            });

            var templates = Db.Connection.Query<(int seq, string stage_key, string name, long is_optional)>(
                "SELECT seq, stage_key, name, is_optional FROM stage_templates ORDER BY seq").ToList();

            foreach (var t in templates)
            {
                var stageId = Db.Connection.ExecuteScalar<long>("""
                    INSERT INTO stages(job_id, seq, stage_key, name, is_optional, status)
                    VALUES (@id,@seq,@key,@name,@opt,'Not started');
                    SELECT last_insert_rowid();
                    """, new { id, seq = t.seq, key = t.stage_key, name = t.name, opt = t.is_optional });

                var items = Db.Connection.Query<(string text, int seq)>(
                    "SELECT text, seq FROM checklist_templates WHERE stage_key=@k ORDER BY seq",
                    new { k = t.stage_key }).ToList();
                foreach (var it in items)
                    Db.Connection.Execute(
                        "INSERT INTO checklist_items(stage_id, text, is_checked, seq) VALUES (@s,@t,0,@q)",
                        new { s = stageId, t = it.text, q = it.seq });
            }
        });
        _activity.Log(id, "job_created",
            $"Job created — {(string.IsNullOrWhiteSpace(job.AddressStreet) ? job.ClientName : job.AddressStreet)}");
        return id;
    }

    public void Update(Job job)
    {
        var before = Get(job.Id);
        Db.Connection.Execute("""
            UPDATE jobs SET
                job_number=@JobNumber, external_ref1=@ExternalRef1, external_ref2=@ExternalRef2,
                address_street=@AddressStreet, address_city=@AddressCity, address_zip=@AddressZip,
                client_name=@ClientName, project_type=@ProjectType, date_assigned=@DateAssigned,
                role_notes=@RoleNotes, status=@Status, notes=@Notes, updated_at=@now
            WHERE id=@Id
            """, new
        {
            job.Id, job.JobNumber, job.ExternalRef1, job.ExternalRef2, job.AddressStreet, job.AddressCity,
            job.AddressZip, job.ClientName, job.ProjectType, job.DateAssigned, job.RoleNotes,
            job.Status, job.Notes, now = DateTime.Now.ToString("s")
        });
        if (before.Status != job.Status)
            _activity.Log(job.Id, "job_status_changed", $"Status: {before.Status} → {job.Status}");
    }

    public void Touch(long jobId)
        => Db.Connection.Execute("UPDATE jobs SET updated_at=@now WHERE id=@jobId",
            new { now = DateTime.Now.ToString("s"), jobId });

    public void Delete(long jobId)
        => Db.Connection.Execute("DELETE FROM jobs WHERE id=@jobId", new { jobId });

    // ---------------------------------------------------------------- list + search

    /// <summary>Search matches address, client, job number, reference number, or status.</summary>
    public List<JobListRow> Search(string? term)
    {
        var jobs = Db.Connection.Query<Job>("SELECT * FROM jobs ORDER BY updated_at DESC").ToList();
        var rows = new List<JobListRow>();

        foreach (var j in jobs)
        {
            var stages = _stages.ForJob(j.Id);
            var totals = TotalsFor(j.Id);
            rows.Add(new JobListRow
            {
                Id = j.Id,
                JobNumber = j.JobNumber,
                ExternalRef1 = j.ExternalRef1,
                Address = j.DisplayAddress,
                ClientName = j.ClientName,
                ProjectType = j.ProjectType,
                Status = j.Status,
                CurrentStage = _stages.CurrentStageName(stages),
                PercentComplete = _stages.PercentComplete(stages),
                Billed = totals.Billed,
                Paid = totals.Paid,
                DateAssigned = j.DateAssigned
            });
        }

        if (string.IsNullOrWhiteSpace(term)) return rows;

        var t = term.Trim();
        return rows.Where(r =>
            Contains(r.Address, t) ||
            Contains(r.ClientName, t) ||
            Contains(r.JobNumber, t) ||
            Contains(r.ExternalRef1, t) ||
            Contains(r.Status, t) ||
            Contains(r.ProjectType, t) ||
            Contains(r.CurrentStage, t)).ToList();
    }

    private static bool Contains(string? haystack, string needle)
        => !string.IsNullOrEmpty(haystack) &&
           haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- money

    public JobTotals TotalsFor(long jobId)
    {
        var billed = Db.Connection.ExecuteScalar<double?>("""
            SELECT COALESCE(SUM(l.qty * l.rate), 0)
            FROM invoice_lines l
            JOIN invoices i ON i.id = l.invoice_id
            WHERE i.job_id = @jobId AND i.status <> 'Void'
            """, new { jobId }) ?? 0;

        var paid = Db.Connection.ExecuteScalar<double?>("""
            SELECT COALESCE(SUM(p.amount), 0)
            FROM payments p
            JOIN invoices i ON i.id = p.invoice_id
            WHERE i.job_id = @jobId AND i.status <> 'Void'
            """, new { jobId }) ?? 0;

        return new JobTotals { Billed = Math.Round(billed, 2), Paid = Math.Round(paid, 2) };
    }
}
