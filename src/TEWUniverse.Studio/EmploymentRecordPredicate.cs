using System.Data;
using System.Data.OleDb;

namespace TEWUniverse.Studio;

/// <summary>
/// tblCV does not expose a verified unique row ID in the supported schema.
/// RecordName is a display label, not a key: consecutive handshake deals may
/// share a label. Write using the complete original row and require exactly
/// one affected row. Indistinguishable exact duplicates remain read-only.
/// </summary>
internal sealed partial class DatabaseSession
{
private static class EmploymentRecordPredicate
{
    internal const string Sql =
        "[WorkerUID]=? AND [FedUID]=? AND [CVFromDate]=? AND [CVUntilDate]=? " +
        "AND [RecordName]=? AND [Initials]=? AND [Loan]=? AND [Trade]=? " +
        "AND [Tour]=? AND [Developmental]=?";

    internal static void Bind(OleDbCommand command,
        WorkerEmploymentEntry old, DataTable schema)
    {
        var fields = new (string Name, object Value)[] {
            ("WorkerUID", old.WorkerUid), ("FedUID", old.FedUid),
            ("CVFromDate", old.FromDate), ("CVUntilDate", old.UntilDate),
            ("RecordName", old.RecordName), ("Initials", old.Initials),
            ("Loan", old.Loan), ("Trade", old.Trade),
            ("Tour", old.Tour), ("Developmental", old.Developmental)
        };
        foreach (var (name, value) in fields)
        {
            var col = schema.Columns[name] ??
                throw new InvalidOperationException("Missing tblCV column " + name);
            command.Parameters.Add("source" + name,
                ToOleDbType(col.DataType)).Value = value;
        }
    }

    internal static bool SamePeriod(WorkerEmploymentEntry a,
        WorkerEmploymentEntry b) =>
        a.WorkerUid == b.WorkerUid && a.FedUid == b.FedUid &&
        a.FromDate == b.FromDate && a.UntilDate == b.UntilDate;

    internal static bool SameCompleteRow(WorkerEmploymentEntry a,
        WorkerEmploymentEntry b) =>
        SamePeriod(a, b) &&
        string.Equals(a.RecordName, b.RecordName,
            StringComparison.OrdinalIgnoreCase) &&
        a.Initials == b.Initials && a.Loan == b.Loan &&
        a.Trade == b.Trade && a.Tour == b.Tour &&
        a.Developmental == b.Developmental;
}
}
