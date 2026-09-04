using Dapper;
using FieldJobs.Data;
using FieldJobs.Models;

namespace FieldJobs.Services;

public sealed class InvoiceService
{
    private readonly SettingsService _settings = new();
    private readonly ActivityService _activity = new();
    private readonly FileService _files = new();

    // ---------------------------------------------------------------- reads

    public Invoice Get(long id)
        => Db.Connection.QuerySingle<Invoice>("SELECT * FROM invoices WHERE id=@id", new { id });

    public List<InvoiceLine> Lines(long invoiceId)
        => Db.Connection.Query<InvoiceLine>(
            "SELECT * FROM invoice_lines WHERE invoice_id=@invoiceId ORDER BY seq, id", new { invoiceId }).ToList();

    public List<Payment> Payments(long invoiceId)
        => Db.Connection.Query<Payment>(
            "SELECT * FROM payments WHERE invoice_id=@invoiceId ORDER BY payment_date, id", new { invoiceId }).ToList();

    public double LineTotal(long invoiceId)
        => Math.Round(Db.Connection.ExecuteScalar<double?>(
            "SELECT COALESCE(SUM(qty*rate),0) FROM invoice_lines WHERE invoice_id=@invoiceId", new { invoiceId }) ?? 0, 2);

    public double PaidTotal(long invoiceId)
        => Math.Round(Db.Connection.ExecuteScalar<double?>(
            "SELECT COALESCE(SUM(amount),0) FROM payments WHERE invoice_id=@invoiceId", new { invoiceId }) ?? 0, 2);

    public List<InvoiceRow> ForJob(long jobId) => RowsFrom("WHERE i.job_id=@jobId", new { jobId });

    public List<InvoiceRow> All() => RowsFrom("", null);

    /// <summary>Days an invoice's balance has been outstanding, measured from date sent (or invoice date).</summary>
    public static int DaysOutstanding(Invoice inv)
    {
        var basis = inv.DateSent ?? inv.InvoiceDate;
        return DateTime.TryParse(basis, out var d) ? Math.Max(0, (int)(DateTime.Today - d.Date).TotalDays) : 0;
    }

    public static string AgeBucket(int days) => days switch
    {
        <= 0 => "Current",
        <= 30 => "1–30",
        <= 60 => "31–60",
        <= 90 => "61–90",
        _ => "90+"
    };

    private List<InvoiceRow> RowsFrom(string where, object? args)
    {
        var invoices = Db.Connection.Query<Invoice>(
            $"SELECT * FROM invoices i {where} ORDER BY i.invoice_date DESC, i.id DESC", args).ToList();
        var rows = new List<InvoiceRow>();
        foreach (var inv in invoices)
        {
            var jobTitle = Db.Connection.ExecuteScalar<string?>(
                "SELECT COALESCE(NULLIF(address_street,''), client_name, 'Job #'||id) FROM jobs WHERE id=@j",
                new { j = inv.JobId }) ?? $"Job #{inv.JobId}";
            var lastPayment = Db.Connection.ExecuteScalar<string?>(
                "SELECT MAX(payment_date) FROM payments WHERE invoice_id=@i", new { i = inv.Id });
            var docs = _files.ForInvoice(inv.Id);

            var billed = inv.Status == Vocab.InvoiceVoid ? 0 : LineTotal(inv.Id);
            var paid = inv.Status == Vocab.InvoiceVoid ? 0 : PaidTotal(inv.Id);
            var days = DaysOutstanding(inv);
            var owes = inv.Status is not (Vocab.InvoiceVoid or Vocab.InvoicePaid) && billed - paid > 0.005;

            rows.Add(new InvoiceRow
            {
                Id = inv.Id,
                JobId = inv.JobId,
                JobTitle = jobTitle,
                InvoiceNumber = inv.InvoiceNumber,
                InvoiceDate = inv.InvoiceDate,
                DateSent = inv.DateSent,
                StageLabel = inv.StageLabel,
                Status = inv.Status,
                Billed = billed,
                Paid = paid,
                PaymentDate = lastPayment,
                DocCount = docs.Count,
                HasPaymentProof = docs.Any(d => d.DocType == Vocab.DocPaymentProof),
                DaysOutstanding = owes ? days : 0,
                IsOverdue = owes && days > Vocab.OverdueDays
            });
        }
        return rows;
    }

    /// <summary>Stage keys on a job that already have a non-void invoice (for the "already paid" hint).</summary>
    public HashSet<string> InvoicedStageKeys(long jobId)
        => Db.Connection.Query<string?>(
            "SELECT stage_key FROM invoices WHERE job_id=@jobId AND status<>'Void' AND stage_key IS NOT NULL",
            new { jobId }).Where(s => s != null).Select(s => s!).ToHashSet();

    // ---------------------------------------------------------------- writes

    public string NextInvoiceNumber()
    {
        var prefix = _settings.InvoicePrefix;
        var maxNum = Db.Connection.Query<string>("SELECT invoice_number FROM invoices")
            .Select(n =>
            {
                var digits = new string(n.Where(char.IsDigit).ToArray());
                return int.TryParse(digits, out var v) ? v : 0;
            })
            .DefaultIfEmpty(1000)
            .Max();
        return $"{prefix}{maxNum + 1}";
    }

    public long CreateForStage(long jobId, string? stageKey, string? stageLabel, string invoiceNumber,
        DateTime date, string description, double amount, string? notes)
    {
        long id = 0;
        Db.InTransaction(_ =>
        {
            id = Db.Connection.ExecuteScalar<long>("""
                INSERT INTO invoices(job_id, invoice_number, invoice_date, stage_key, stage_label, status, notes, created_at)
                VALUES (@jobId,@invoiceNumber,@date,@stageKey,@stageLabel,'Draft',@notes,@now);
                SELECT last_insert_rowid();
                """, new
            {
                jobId, invoiceNumber, date = date.ToString("yyyy-MM-dd"),
                stageKey, stageLabel, notes, now = DateTime.Now.ToString("s")
            });
            Db.Connection.Execute(
                "INSERT INTO invoice_lines(invoice_id, description, qty, rate, seq) VALUES (@id,@description,1,@amount,0)",
                new { id, description, amount });
        });
        _activity.Log(jobId, "invoice_created", $"Invoice {invoiceNumber} created ({amount:C0}) for {stageLabel ?? "job"}");
        return id;
    }

    public long CreateHeader(long jobId, string invoiceNumber, DateTime date, string? stageKey,
        string? stageLabel, string status, string? notes)
    {
        var id = Db.Connection.ExecuteScalar<long>("""
            INSERT INTO invoices(job_id, invoice_number, invoice_date, stage_key, stage_label, status, notes, created_at)
            VALUES (@jobId,@invoiceNumber,@date,@stageKey,@stageLabel,@status,@notes,@now);
            SELECT last_insert_rowid();
            """, new
        {
            jobId, invoiceNumber, date = date.ToString("yyyy-MM-dd"),
            stageKey, stageLabel, status, notes, now = DateTime.Now.ToString("s")
        });
        _activity.Log(jobId, "invoice_created", $"Invoice {invoiceNumber} created");
        return id;
    }

    public void UpdateHeader(Invoice inv)
        => Db.Connection.Execute("""
            UPDATE invoices SET invoice_number=@InvoiceNumber, invoice_date=@InvoiceDate,
                   date_sent=@DateSent, sent_to=@SentTo,
                   stage_key=@StageKey, stage_label=@StageLabel, status=@Status, notes=@Notes
            WHERE id=@Id
            """, inv);

    /// <summary>Stamp the invoice as sent (drives overdue tracking).</summary>
    public void MarkSent(long invoiceId, DateTime date, string? to)
    {
        var inv = Get(invoiceId);
        Db.Connection.Execute(
            "UPDATE invoices SET date_sent=@d, sent_to=@to, status=CASE WHEN status='Draft' THEN 'Sent' ELSE status END WHERE id=@invoiceId",
            new { d = date.ToString("yyyy-MM-dd"), to, invoiceId });
        _activity.Log(inv.JobId, "invoice_sent",
            $"Invoice {inv.InvoiceNumber} marked sent{(string.IsNullOrWhiteSpace(to) ? "" : $" to {to}")}");
    }

    public void ReplaceLines(long invoiceId, IEnumerable<InvoiceLine> lines)
    {
        Db.InTransaction(_ =>
        {
            Db.Connection.Execute("DELETE FROM invoice_lines WHERE invoice_id=@invoiceId", new { invoiceId });
            var seq = 0;
            foreach (var l in lines.Where(l => !string.IsNullOrWhiteSpace(l.Description)))
                Db.Connection.Execute("""
                    INSERT INTO invoice_lines(invoice_id, description, qty, rate, seq)
                    VALUES (@invoiceId,@Description,@Qty,@Rate,@seq)
                    """, new { invoiceId, l.Description, l.Qty, l.Rate, seq = seq++ });
        });
    }

    public void Delete(long invoiceId)
    {
        var inv = Get(invoiceId);
        Db.Connection.Execute("DELETE FROM invoices WHERE id=@invoiceId", new { invoiceId });
        _activity.Log(inv.JobId, "invoice_deleted", $"Invoice {inv.InvoiceNumber} deleted");
    }

    // ---------------------------------------------------------------- payments

    public void AddPayment(long invoiceId, DateTime date, double amount, string? method, string? reference, string? note)
    {
        Db.Connection.Execute("""
            INSERT INTO payments(invoice_id, payment_date, amount, method, reference, note)
            VALUES (@invoiceId,@d,@amount,@method,@reference,@note)
            """, new { invoiceId, d = date.ToString("yyyy-MM-dd"), amount, method, reference, note });
        RecalcStatus(invoiceId);

        var inv = Get(invoiceId);
        _activity.Log(inv.JobId, "payment_recorded",
            $"Payment {amount:C} recorded on {inv.InvoiceNumber}"
            + (string.IsNullOrWhiteSpace(method) ? "" : $" ({method})"));
    }

    public void DeletePayment(long paymentId)
    {
        var invId = Db.Connection.ExecuteScalar<long>(
            "SELECT invoice_id FROM payments WHERE id=@paymentId", new { paymentId });
        Db.Connection.Execute("DELETE FROM payments WHERE id=@paymentId", new { paymentId });
        RecalcStatus(invId);
        var inv = Get(invId);
        _activity.Log(inv.JobId, "payment_removed", $"Payment removed from {inv.InvoiceNumber}");
    }

    /// <summary>Records a payment for the whole outstanding balance and marks the invoice Paid.</summary>
    public void MarkPaidInFull(long invoiceId, DateTime date, string? method, string? reference, string? note)
    {
        var balance = Math.Round(LineTotal(invoiceId) - PaidTotal(invoiceId), 2);
        if (balance > 0.005)
            AddPayment(invoiceId, date, balance, method, reference, note ?? "Paid in full");
        Db.Connection.Execute("UPDATE invoices SET status='Paid' WHERE id=@invoiceId", new { invoiceId });
    }

    /// <summary>Nudge Draft/Sent/Partial/Paid to match what has actually been received. Never touches Void.</summary>
    public void RecalcStatus(long invoiceId)
    {
        var status = Db.Connection.ExecuteScalar<string?>(
            "SELECT status FROM invoices WHERE id=@invoiceId", new { invoiceId }) ?? "Draft";
        if (status == Vocab.InvoiceVoid) return;

        var billed = LineTotal(invoiceId);
        var paid = PaidTotal(invoiceId);

        string next = status;
        if (paid <= 0.005) next = status is "Draft" or "Sent" ? status : "Sent";
        else if (paid + 0.005 >= billed && billed > 0) next = "Paid";
        else next = "Partial";

        if (next != status)
            Db.Connection.Execute("UPDATE invoices SET status=@next WHERE id=@invoiceId", new { next, invoiceId });
    }

    // ---------------------------------------------------------------- global payments ledger

    public List<PaymentLedgerRow> PaymentLedger(int? year = null, int? month = null)
    {
        var rows = Db.Connection.Query<PaymentLedgerRow>("""
            SELECT p.id            AS Id,
                   p.invoice_id    AS InvoiceId,
                   i.job_id        AS JobId,
                   COALESCE(NULLIF(j.address_street,''), j.client_name, 'Job #'||j.id) AS JobTitle,
                   i.invoice_number AS InvoiceNumber,
                   p.payment_date  AS PaymentDate,
                   p.amount        AS Amount,
                   p.method        AS Method,
                   p.reference     AS Reference,
                   p.note          AS Note
            FROM payments p
            JOIN invoices i ON i.id = p.invoice_id
            JOIN jobs j     ON j.id = i.job_id
            ORDER BY p.payment_date DESC, p.id DESC
            """).ToList();

        if (year is { } y)
            rows = rows.Where(r => DateTime.TryParse(r.PaymentDate, out var d)
                                   && d.Year == y && (month is null || d.Month == month)).ToList();
        return rows;
    }

    public double PaymentLedgerTotal(IEnumerable<PaymentLedgerRow> rows)
        => Math.Round(rows.Sum(r => r.Amount), 2);

    /// <summary>Invoices that still owe money — for the "record payment" picker.</summary>
    public List<InvoiceRow> OpenInvoices()
        => All().Where(r => r.Status is not (Vocab.InvoiceVoid or Vocab.InvoicePaid) && r.Balance > 0.005)
                .OrderByDescending(r => r.DaysOutstanding).ToList();
}
