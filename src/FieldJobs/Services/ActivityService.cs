using Dapper;
using FieldJobs.Data;
using FieldJobs.Models;

namespace FieldJobs.Services;

/// <summary>
/// Append-only per-job event log. Other services call <see cref="Log"/> after a change;
/// the job detail "Activity" tab and the dashboard feed read it back.
/// </summary>
public sealed class ActivityService
{
    public void Log(long? jobId, string kind, string summary)
    {
        try
        {
            Db.Connection.Execute(
                "INSERT INTO activity(job_id, ts, kind, summary) VALUES (@jobId, @ts, @kind, @summary)",
                new { jobId, ts = DateTime.Now.ToString("s"), kind, summary });
        }
        catch (Exception ex)
        {
            Infra.Log.Error("ActivityService.Log", ex); // never let logging break a real action
        }
    }

    public List<ActivityEntry> ForJob(long jobId)
        => Db.Connection.Query<ActivityEntry>(
            "SELECT * FROM activity WHERE job_id=@jobId ORDER BY ts DESC, id DESC", new { jobId }).ToList();

    public List<ActivityEntry> Recent(int limit = 12)
        => Db.Connection.Query<ActivityEntry>(
            "SELECT * FROM activity ORDER BY ts DESC, id DESC LIMIT @limit", new { limit }).ToList();

    /// <summary>"2h ago", "yesterday", "Aug 5" — friendly relative time for the feed.</summary>
    public static string Ago(string iso)
    {
        if (!DateTime.TryParse(iso, out var t)) return iso;
        var d = DateTime.Now - t;
        return d switch
        {
            { TotalMinutes: < 1 } => "just now",
            { TotalMinutes: < 60 } => $"{(int)d.TotalMinutes}m ago",
            { TotalHours: < 24 } => $"{(int)d.TotalHours}h ago",
            { TotalDays: < 2 } => "yesterday",
            { TotalDays: < 7 } => $"{(int)d.TotalDays}d ago",
            _ => t.ToString("MMM d")
        };
    }
}
