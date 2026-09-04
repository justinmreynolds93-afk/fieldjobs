using Dapper;
using FieldJobs.Data;
using FieldJobs.Models;

namespace FieldJobs.Services;

/// <summary>Agency-side contacts used for the "Prepared for" line on reports.</summary>
public sealed class ContactService
{
    public List<Contact> All()
        => Db.Connection.Query<Contact>(
            "SELECT * FROM contacts ORDER BY is_primary DESC, sort_order, name").ToList();

    public Contact? Get(long id)
        => Db.Connection.QuerySingleOrDefault<Contact>("SELECT * FROM contacts WHERE id=@id", new { id });

    public Contact? Primary()
        => Db.Connection.QuerySingleOrDefault<Contact>(
            "SELECT * FROM contacts ORDER BY is_primary DESC, sort_order, name LIMIT 1");

    public void ReplaceAll(IEnumerable<Contact> contacts)
    {
        Db.InTransaction(_ =>
        {
            Db.Connection.Execute("DELETE FROM contacts");
            var i = 0;
            var list = contacts.Where(c => !string.IsNullOrWhiteSpace(c.Name)).ToList();
            // exactly one primary
            if (list.Count > 0 && list.All(c => !c.IsPrimary)) list[0].IsPrimary = true;
            var seenPrimary = false;
            foreach (var c in list)
            {
                var primary = c.IsPrimary && !seenPrimary;
                if (primary) seenPrimary = true;
                Db.Connection.Execute("""
                    INSERT INTO contacts(name, role, email, phone, is_primary, sort_order)
                    VALUES (@Name, @Role, @Email, @Phone, @primary, @sort)
                    """, new { c.Name, c.Role, c.Email, c.Phone, primary = primary ? 1 : 0, sort = i++ });
            }
        });
    }
}
