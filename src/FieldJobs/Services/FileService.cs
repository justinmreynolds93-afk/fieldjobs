using System.Diagnostics;
using System.IO;
using Dapper;
using FieldJobs.Data;
using FieldJobs.Models;

namespace FieldJobs.Services;

/// <summary>
/// Copies attachments into the app data folder, organised as
/// Files\{jobId}-{address}\{stage or "Invoices\<number>"}\{filename}. Originals are never moved.
/// </summary>
public sealed class FileService
{
    private readonly ActivityService _activity = new();

    public List<JobFile> ForJob(long jobId)
        => Db.Connection.Query<JobFile>(
            "SELECT * FROM job_files WHERE job_id=@jobId ORDER BY date_added DESC, id DESC", new { jobId }).ToList();

    public List<JobFile> ForStage(long stageId)
        => Db.Connection.Query<JobFile>(
            "SELECT * FROM job_files WHERE stage_id=@stageId ORDER BY date_added DESC, id DESC", new { stageId }).ToList();

    public List<JobFile> ForInvoice(long invoiceId)
        => Db.Connection.Query<JobFile>(
            "SELECT * FROM job_files WHERE invoice_id=@invoiceId ORDER BY date_added DESC, id DESC", new { invoiceId }).ToList();

    public bool InvoiceHasProof(long invoiceId)
        => Db.Connection.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM job_files WHERE invoice_id=@invoiceId AND doc_type='payment_proof'",
            new { invoiceId }) > 0;

    /// <summary>Attach a file to a job, optionally scoped to a stage.</summary>
    public JobFile Attach(long jobId, long? stageId, string sourcePath, string? note = null)
    {
        string? stageKey = null, subFolder = null;
        if (stageId is { } sid)
        {
            var s = Db.Connection.QuerySingle<Stage>("SELECT * FROM stages WHERE id=@sid", new { sid });
            stageKey = s.StageKey;
            subFolder = s.Name;
        }
        return Insert(jobId, stageId, stageKey, null, Vocab.DocFile, subFolder, sourcePath, note);
    }

    /// <summary>Attach the sent invoice or a proof of payment to an invoice.</summary>
    public JobFile AttachToInvoice(long jobId, long invoiceId, string docType, string sourcePath, string? note = null)
    {
        var number = Db.Connection.ExecuteScalar<string>(
            "SELECT invoice_number FROM invoices WHERE id=@invoiceId", new { invoiceId }) ?? invoiceId.ToString();
        return Insert(jobId, null, null, invoiceId, docType, Path.Combine("Invoices", number), sourcePath, note);
    }

    private JobFile Insert(long jobId, long? stageId, string? stageKey, long? invoiceId, string docType,
        string? subFolder, string sourcePath, string? note)
    {
        var job = Db.Connection.QuerySingle<Job>("SELECT * FROM jobs WHERE id=@jobId", new { jobId });

        var targetDir = Path.Combine(AppPaths.FilesRoot, AppPaths.SafeFolderName($"{jobId}-{job.Title}"));
        if (!string.IsNullOrEmpty(subFolder))
            foreach (var part in subFolder.Split(Path.DirectorySeparatorChar, '/', '\\'))
                targetDir = Path.Combine(targetDir, AppPaths.SafeFolderName(part));
        Directory.CreateDirectory(targetDir);

        var name = Path.GetFileName(sourcePath);
        var dest = MakeUnique(Path.Combine(targetDir, name));
        File.Copy(sourcePath, dest, overwrite: false);
        var info = new FileInfo(dest);

        var id = Db.Connection.ExecuteScalar<long>("""
            INSERT INTO job_files(job_id, stage_id, stage_key, invoice_id, doc_type,
                                  original_name, stored_path, size_bytes, date_added, note)
            VALUES (@jobId,@stageId,@stageKey,@invoiceId,@docType,@name,@dest,@size,@now,@note);
            SELECT last_insert_rowid();
            """, new
        {
            jobId, stageId, stageKey, invoiceId, docType, name, dest,
            size = info.Length, now = DateTime.Now.ToString("s"), note
        });

        var label = docType switch
        {
            Vocab.DocInvoiceSent => "Sent invoice attached",
            Vocab.DocPaymentProof => "Proof of payment attached",
            _ => "File added"
        };
        _activity.Log(jobId, "file_added", $"{label}: {name}");

        return Db.Connection.QuerySingle<JobFile>("SELECT * FROM job_files WHERE id=@id", new { id });
    }

    public void UpdateNote(long fileId, string? note)
        => Db.Connection.Execute("UPDATE job_files SET note=@note WHERE id=@fileId", new { note, fileId });

    /// <summary>Removes the DB record and deletes the stored copy (the app's copy only).</summary>
    public void Remove(long fileId)
    {
        var path = Db.Connection.ExecuteScalar<string?>(
            "SELECT stored_path FROM job_files WHERE id=@fileId", new { fileId });
        Db.Connection.Execute("DELETE FROM job_files WHERE id=@fileId", new { fileId });
        try
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
        }
        catch { /* leaving an orphan file is better than crashing */ }
    }

    public void OpenWithDefaultApp(string storedPath)
    {
        if (!File.Exists(storedPath))
            throw new FileNotFoundException("The stored copy of this file is missing.", storedPath);
        Process.Start(new ProcessStartInfo(storedPath) { UseShellExecute = true });
    }

    public void RevealInExplorer(string storedPath)
    {
        if (File.Exists(storedPath))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{storedPath}\"") { UseShellExecute = true });
    }

    private static string MakeUnique(string path)
    {
        if (!File.Exists(path)) return path;
        var dir = Path.GetDirectoryName(path)!;
        var stem = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
