using Dapper;
using FieldJobs.Data;
using FieldJobs.Models;

namespace FieldJobs.Services;

public sealed class StageService
{
    private readonly ActivityService _activity = new();

    public List<Stage> ForJob(long jobId)
        => Db.Connection.Query<Stage>(
            "SELECT * FROM stages WHERE job_id=@jobId ORDER BY seq", new { jobId }).ToList();

    public Stage Get(long stageId)
        => Db.Connection.QuerySingle<Stage>("SELECT * FROM stages WHERE id=@stageId", new { stageId });

    public List<ChecklistItem> Checklist(long stageId)
        => Db.Connection.Query<ChecklistItem>(
            "SELECT * FROM checklist_items WHERE stage_id=@stageId ORDER BY seq, id", new { stageId }).ToList();

    // ---------------------------------------------------------------- gating

    /// <summary>
    /// A stage is unlocked when the nearest earlier stage that is NOT "N/A" is "Complete".
    /// If every earlier stage is N/A (or there are none) the stage is unlocked.
    /// </summary>
    public bool IsUnlocked(IReadOnlyList<Stage> orderedStages, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var prev = orderedStages[i];
            if (prev.Status == Vocab.StageNa) continue;
            return prev.Status == Vocab.StageComplete;
        }
        return true;
    }

    public string LockReason(IReadOnlyList<Stage> orderedStages, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            var prev = orderedStages[i];
            if (prev.Status == Vocab.StageNa) continue;
            return prev.Status == Vocab.StageComplete
                ? ""
                : $"Locked until “{prev.Name}” is marked Complete.";
        }
        return "";
    }

    // ---------------------------------------------------------------- percent

    /// <summary>Percent complete, based on required stages only (per the spec).</summary>
    public int PercentComplete(IEnumerable<Stage> stages)
    {
        var required = stages.Where(s => !s.IsOptional).ToList();
        if (required.Count == 0) return 0;
        var done = required.Count(s => s.Status == Vocab.StageComplete);
        return (int)Math.Round(100.0 * done / required.Count);
    }

    /// <summary>Name of the first stage that is not Complete/N-A — the job's "current" step.</summary>
    public string CurrentStageName(IEnumerable<Stage> stages)
    {
        var next = stages.OrderBy(s => s.Seq)
            .FirstOrDefault(s => s.Status is not (Vocab.StageComplete or Vocab.StageNa));
        return next?.Name ?? "Job closeout";
    }

    // ---------------------------------------------------------------- mutations

    public void SetStatus(long stageId, string status)
    {
        string? date = status == Vocab.StageComplete
            ? DateTime.Today.ToString("yyyy-MM-dd")
            : null;

        // Keep an existing completion date if it was already set and we're staying Complete.
        if (status == Vocab.StageComplete)
        {
            var existing = Db.Connection.ExecuteScalar<string?>(
                "SELECT date_completed FROM stages WHERE id=@stageId", new { stageId });
            if (!string.IsNullOrWhiteSpace(existing)) date = existing;
        }

        var stage = Get(stageId);
        if (stage.Status == status) return;

        Db.Connection.Execute(
            "UPDATE stages SET status=@status, date_completed=@date WHERE id=@stageId",
            new { status, date, stageId });

        var verb = status switch
        {
            Vocab.StageComplete => "completed",
            Vocab.StageNa => "marked N/A",
            Vocab.StageInProgress => "started",
            _ => "reopened"
        };
        _activity.Log(stage.JobId, "stage_" + verb.Replace(' ', '_'), $"Stage “{stage.Name}” {verb}");
    }

    public void SetDateCompleted(long stageId, DateTime? date)
        => Db.Connection.Execute("UPDATE stages SET date_completed=@d WHERE id=@stageId",
            new { d = date?.ToString("yyyy-MM-dd"), stageId });

    public void SetNotes(long stageId, string? notes)
        => Db.Connection.Execute("UPDATE stages SET notes=@notes WHERE id=@stageId", new { notes, stageId });

    public void SetChecklistItem(long itemId, bool isChecked)
        => Db.Connection.Execute("UPDATE checklist_items SET is_checked=@c WHERE id=@itemId",
            new { c = isChecked ? 1 : 0, itemId });

    public long AddChecklistItem(long stageId, string text)
    {
        var seq = Db.Connection.ExecuteScalar<int>(
            "SELECT COALESCE(MAX(seq),-1)+1 FROM checklist_items WHERE stage_id=@stageId", new { stageId });
        return Db.Connection.ExecuteScalar<long>("""
            INSERT INTO checklist_items(stage_id, text, is_checked, seq) VALUES (@stageId,@text,0,@seq);
            SELECT last_insert_rowid();
            """, new { stageId, text, seq });
    }

    public void RenameChecklistItem(long itemId, string text)
        => Db.Connection.Execute("UPDATE checklist_items SET text=@text WHERE id=@itemId", new { text, itemId });

    public void DeleteChecklistItem(long itemId)
        => Db.Connection.Execute("DELETE FROM checklist_items WHERE id=@itemId", new { itemId });

    public bool HasFiles(long stageId)
        => Db.Connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM job_files WHERE stage_id=@stageId", new { stageId }) > 0;

    // ---------------------------------------------------------------- templates (Settings screen)

    public List<StageTemplate> StageTemplates()
        => Db.Connection.Query<StageTemplate>(
            "SELECT * FROM stage_templates ORDER BY seq").ToList();

    public List<ChecklistTemplate> ChecklistTemplates(string stageKey)
        => Db.Connection.Query<ChecklistTemplate>(
            "SELECT * FROM checklist_templates WHERE stage_key=@stageKey ORDER BY seq, id",
            new { stageKey }).ToList();

    public void ReplaceChecklistTemplate(string stageKey, IEnumerable<string> lines)
    {
        Db.InTransaction(_ =>
        {
            Db.Connection.Execute("DELETE FROM checklist_templates WHERE stage_key=@stageKey", new { stageKey });
            var i = 0;
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
                Db.Connection.Execute(
                    "INSERT INTO checklist_templates(stage_key, text, seq) VALUES (@stageKey,@line,@seq)",
                    new { stageKey, line = line.Trim(), seq = i++ });
        });
    }
}
