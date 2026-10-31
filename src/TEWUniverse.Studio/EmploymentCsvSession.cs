using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed partial class DatabaseSession
{
    public int ApplyEmploymentCsv(EmploymentCsvPreview preview)
    {
        if (preview.Changes.Count == 0) return 0;
        var schema = ValidateWorkerExtraSchema("tblCV", "RecordName", "WorkerUID",
            "FedUID", "Initials", "CVFromDate", "CVUntilDate",
            "Loan", "Trade", "Tour", "Developmental");
        var originals = ReadAllWorkerEmployment();
        // The stored RecordName is not unique, even within one worker.
        // Changes refer to complete original rows, including full dates.
        var workerIds = ListWorkers().Select(x => x.Id).ToHashSet();
        var companyIds = ReadEmploymentCompanies().Select(x => x.Id).ToHashSet();
        var sourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resulting = originals.ToList();
        var replacements = new List<(EmploymentCsvChange Change, string FinalName)>();
        var deletions = new List<EmploymentCsvChange>();
        int limit = schema.Columns["RecordName"]!.MaxLength;

        foreach (var change in preview.Changes)
        {
            var d = change.Draft;
            int worker = change.WorkerUid;
            // Deletions never need replacement values: a legacy original
            // can have missing initials or an out-of-range historical date.
            // Only source fingerprint and full-row identity govern deletion.
            string? newName = EmploymentCsvCodec.ValidateForWrite(
                change, workerIds, companyIds, limit,
                schema.Columns["Initials"]!.MaxLength);
            if (!change.IsAdd)
            {
                var matches = originals.Where(row =>
                    row.WorkerUid == worker &&
                    EmploymentCsvCodec.Fingerprint(row).Equals(
                        change.ExpectedFingerprint, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(row.RecordName, change.OriginalName,
                        StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count != 1 ||
                    !sourceKeys.Add(change.ExpectedFingerprint))
                    throw new InvalidOperationException(
                        $"Worker {worker}, '{change.OriginalName}': the original period " +
                        "is missing, was changed, or is completely identical to another " +
                        "stored row. Individual changes to indistinguishable exact " +
                        "duplicates are not safe. Refresh and review the CSV.");
                var original = matches[0];
                if (!resulting.Remove(original))
                    throw new InvalidOperationException(
                        "The same original employment period was targeted twice.");
                if (change.IsDelete)
                {
                    deletions.Add(change);
                    continue;
                }
            }
            else if (change.OriginalName.Length != 0)
                throw new InvalidOperationException("An Add row cannot specify an old RecordName.");

            resulting.Add(new WorkerEmploymentEntry(newName!, worker, d.FedUid, d.Initials,
                d.FromDate, d.UntilDate, d.Loan, d.Trade, d.Tour, d.Developmental));
            replacements.Add((change, newName!));
        }

        // Existing indistinguishable rows can remain untouched. Never
        // introduce MORE copies of a physically identical row, because they
        // could not be individually selected for a future edit/delete.
        foreach (var group in resulting.GroupBy(EmploymentCsvCodec.Fingerprint))
        {
            if (group.Count() <= 1) continue;
            int originalCount = originals.Count(x =>
                EmploymentCsvCodec.Fingerprint(x) == group.Key);
            if (group.Count() > originalCount)
                throw new InvalidOperationException(
                    $"Worker {group.First().WorkerUid}: this import would create " +
                    "an identical duplicate of an entire employment period. " +
                    "Give separate stints distinct From/Until dates.");
        }

        foreach (DataColumn col in schema.Columns)
            if (col.ColumnName != "RecordName" &&
                col.ColumnName != "WorkerUID" && col.ColumnName != "FedUID" &&
                col.ColumnName != "Initials" && col.ColumnName != "CVFromDate" &&
                col.ColumnName != "CVUntilDate" && col.ColumnName != "Loan" &&
                col.ColumnName != "Trade" && col.ColumnName != "Tour" &&
                col.ColumnName != "Developmental" && !col.AutoIncrement &&
                !col.AllowDBNull && col.DefaultValue is DBNull)
                throw new InvalidOperationException(
                    "Unsupported required tblCV field: " + col.ColumnName);

        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            // Delete explicitly requested rows first. The original row's
            // fingerprint and worker/name identity were verified above;
            // every delete must affect exactly one record.
            foreach (var change in deletions)
            {
                using var remove = new OleDbCommand(
                    "DELETE FROM [tblCV] WHERE " + EmploymentRecordPredicate.Sql,
                    _connection, tx);
                EmploymentRecordPredicate.Bind(remove,
                    originals.Single(x => x.WorkerUid == change.WorkerUid &&
                        EmploymentCsvCodec.Fingerprint(x).Equals(change.ExpectedFingerprint,
                            StringComparison.OrdinalIgnoreCase)), schema);
                if (remove.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        $"Employment record '{change.OriginalName}' for worker " +
                        change.WorkerUid + " could not be deleted. Import rolled back.");
            }

            // A renamed row is temporarily moved aside, preventing swaps and
            // same-worker collisions from failing halfway through the import.
            var staged = new List<(EmploymentCsvChange Change, string Temporary, string Final)>();
            foreach (var (change, name) in replacements.Where(x => !x.Change.IsAdd))
            {
                string temporary = "~CV" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                if (limit > 0 && temporary.Length > limit)
                    throw new InvalidOperationException("RecordName limit is too small to stage updates.");
                using var cmd = new OleDbCommand(
                    "UPDATE [tblCV] SET [RecordName]=? WHERE " +
                    EmploymentRecordPredicate.Sql, _connection, tx);
                cmd.Parameters.Add("name", OleDbType.VarWChar).Value = temporary;
                EmploymentRecordPredicate.Bind(cmd,
                    originals.Single(x => x.WorkerUid == change.WorkerUid &&
                        EmploymentCsvCodec.Fingerprint(x).Equals(change.ExpectedFingerprint,
                            StringComparison.OrdinalIgnoreCase)), schema);
                if (cmd.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("An imported record changed while staging.");
                staged.Add((change, temporary, name));
            }

            string[] insertColumns = { "RecordName", "WorkerUID", "FedUID", "Initials",
                "CVFromDate", "CVUntilDate", "Loan", "Trade", "Tour", "Developmental" };
            string[] updateColumns = { "RecordName", "FedUID", "Initials",
                "CVFromDate", "CVUntilDate", "Loan", "Trade", "Tour", "Developmental" };
            foreach (var (change, name) in replacements.Where(x => x.Change.IsAdd))
            {
                using var cmd = new OleDbCommand(
                    "INSERT INTO [tblCV] (" + string.Join(",", insertColumns.Select(x => "[" + x + "]")) +
                    ") VALUES (" + string.Join(",", insertColumns.Select(_ => "?")) + ")",
                    _connection, tx);
                BindEmploymentCsv(cmd, change, name, true, schema);
                if (cmd.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("An imported employment row could not be inserted.");
            }
            foreach (var (change, temporary, name) in staged)
            {
                using var cmd = new OleDbCommand(
                    "UPDATE [tblCV] SET " +
                    string.Join(",", updateColumns.Select(x => "[" + x + "]=?")) +
                    " WHERE [WorkerUID]=? AND [RecordName]=?", _connection, tx);
                BindEmploymentCsv(cmd, change, name, false, schema);
                cmd.Parameters.Add("workerKey", OleDbType.Integer).Value = change.WorkerUid;
                cmd.Parameters.Add("oldKey", OleDbType.VarWChar).Value = temporary;
                if (cmd.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException("An imported employment row could not be updated.");
            }
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return replacements.Count + deletions.Count;
        }
        catch { try { tx.Rollback(); } catch { } throw; }
    }

    private static void BindEmploymentCsv(OleDbCommand cmd,
        EmploymentCsvChange change, string name, bool insert, DataTable schema)
    {
        var d = change.Draft;
        var values = new Dictionary<string, object> {
            ["RecordName"] = name, ["WorkerUID"] = change.WorkerUid,
            ["FedUID"] = d.FedUid, ["Initials"] = d.Initials,
            ["CVFromDate"] = d.FromDate.Date, ["CVUntilDate"] = d.UntilDate.Date,
            ["Loan"] = d.Loan, ["Trade"] = d.Trade,
            ["Tour"] = d.Tour, ["Developmental"] = d.Developmental
        };
        string[] columns = insert
            ? new[] { "RecordName", "WorkerUID", "FedUID", "Initials",
                "CVFromDate", "CVUntilDate", "Loan", "Trade", "Tour", "Developmental" }
            : new[] { "RecordName", "FedUID", "Initials",
                "CVFromDate", "CVUntilDate", "Loan", "Trade", "Tour", "Developmental" };
        foreach (string col in columns)
            cmd.Parameters.Add(col, ToOleDbType(schema.Columns[col]!.DataType))
                .Value = values[col];
    }
}
