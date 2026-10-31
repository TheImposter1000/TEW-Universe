using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed partial class DatabaseSession
{
    public int ApplyEmploymentCsv(EmploymentCsvPreview preview)
    {
        if (preview.RebuildFromSnapshot)
            return RebuildEmploymentCsvFromSnapshot(preview);
        if (preview.Changes.Count == 0) return 0;
        var schema = ValidateWorkerExtraSchema("tblCV", "RecordName", "WorkerUID",
            "FedUID", "Initials", "CVFromDate", "CVUntilDate",
            "Loan", "Trade", "Tour", "Developmental");
        var originals = ReadAllWorkerEmployment();
        var current = originals.ToDictionary(x =>
            (x.WorkerUid, x.RecordName.ToUpperInvariant()), x => x);
        var workerIds = ListWorkers().Select(x => x.Id).ToHashSet();
        var companyIds = ReadEmploymentCompanies().Select(x => x.Id).ToHashSet();
        var sourceKeys = new HashSet<(int, string)>();
        var resulting = originals.ToList();
        var replacements = new List<(EmploymentCsvChange Change, string FinalName)>();
        var deletions = new List<EmploymentCsvChange>();
        int limit = schema.Columns["RecordName"]!.MaxLength;

        foreach (var change in preview.Changes)
        {
            var d = change.Draft;
            int worker = change.WorkerUid;
            if (!workerIds.Contains(worker) || !companyIds.Contains(d.FedUid))
                throw new InvalidOperationException("CSV references a missing worker or company.");
            bool ongoing = StudioDateFormat.IsOngoingEndDate(d.UntilDate);
            if (d.FromDate.Year is < 1800 or > 2100 ||
                (!ongoing && (d.UntilDate.Year is < 1800 or > 2100 ||
                             d.UntilDate < d.FromDate)) ||
                (string.IsNullOrWhiteSpace(d.Initials) &&
                 string.IsNullOrWhiteSpace(d.RequestedRecordName)) ||
                d.Initials.Any(char.IsControl))
                throw new InvalidOperationException("CSV contains invalid employment dates or initials.");
            string newName = string.IsNullOrWhiteSpace(d.RequestedRecordName)
                ? StudioRecordNames.Employment(d.Initials, d.FromDate, d.UntilDate)
                : d.RequestedRecordName.Trim();
            if (newName.Length == 0 || newName.Any(char.IsControl) ||
                limit > 0 && newName.Length > limit ||
                schema.Columns["Initials"]!.MaxLength > 0 &&
                    d.Initials.Length > schema.Columns["Initials"]!.MaxLength)
                throw new InvalidOperationException("CSV employment name or initials exceeds the MDB's limits.");
            if (!change.IsAdd)
            {
                if (!sourceKeys.Add((worker, change.OriginalName.ToUpperInvariant())) ||
                    !current.TryGetValue((worker, change.OriginalName.ToUpperInvariant()), out var original) ||
                    !EmploymentCsvCodec.Fingerprint(original)
                        .Equals(change.ExpectedFingerprint, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "CSV source record is missing, duplicated or stale. Export a new file.");
                resulting.Remove(original);
                if (change.IsDelete)
                {
                    deletions.Add(change);
                    continue;
                }
            }
            else if (change.OriginalName.Length != 0)
                throw new InvalidOperationException("An Add row cannot specify an old RecordName.");

            resulting.Add(new WorkerEmploymentEntry(newName, worker, d.FedUid, d.Initials,
                d.FromDate, d.UntilDate, d.Loan, d.Trade, d.Tour, d.Developmental));
            replacements.Add((change, newName));
        }

        foreach (var group in resulting.GroupBy(x => x.WorkerUid))
        {
            if (group.Select(x => x.RecordName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != group.Count())
                throw new InvalidOperationException(
                    $"Worker {group.Key} has duplicate employment Record Names in the CSV result.");
            if (group.GroupBy(x => (x.FedUid, x.FromDate.Date, x.UntilDate.Date))
                .Any(x => x.Count() > 1))
                throw new InvalidOperationException(
                    $"Worker {group.Key} has duplicate company periods with identical dates.");
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
                    "DELETE FROM [tblCV] WHERE [WorkerUID]=? AND [RecordName]=?",
                    _connection, tx);
                remove.Parameters.Add("worker", OleDbType.Integer).Value = change.WorkerUid;
                remove.Parameters.Add("name", OleDbType.VarWChar).Value = change.OriginalName;
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
                    "UPDATE [tblCV] SET [RecordName]=? WHERE [WorkerUID]=? AND [RecordName]=?",
                    _connection, tx);
                cmd.Parameters.Add("name", OleDbType.VarWChar).Value = temporary;
                cmd.Parameters.Add("worker", OleDbType.Integer).Value = change.WorkerUid;
                cmd.Parameters.Add("old", OleDbType.VarWChar).Value = change.OriginalName;
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

    // Recovery for databases that already contain ambiguous duplicate
    // (WorkerUID, RecordName) values. Rebuild the requested complete snapshot
    // in one transaction; never update or delete by the ambiguous old key.
    private int RebuildEmploymentCsvFromSnapshot(EmploymentCsvPreview preview)
    {
        if (!preview.IsReplacement)
            throw new InvalidOperationException("A complete employment snapshot is required.");
        var schema = ValidateWorkerExtraSchema("tblCV", "RecordName", "WorkerUID",
            "FedUID", "Initials", "CVFromDate", "CVUntilDate",
            "Loan", "Trade", "Tour", "Developmental");
        var originals = ReadAllWorkerEmployment();
        var expected = preview.Changes.Where(x => x.IsDelete)
            .GroupBy(x => x.ExpectedFingerprint, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        var actual = originals.GroupBy(EmploymentCsvCodec.Fingerprint,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.OrdinalIgnoreCase);
        if (expected.Count != actual.Count ||
            expected.Any(x => !actual.TryGetValue(x.Key, out int n) || n != x.Value))
            throw new InvalidOperationException(
                "Stored employment history changed since the CSV preview. Export again.");

        var workerIds = ListWorkers().Select(x => x.Id).ToHashSet();
        var companyIds = ReadEmploymentCompanies().Select(x => x.Id).ToHashSet();
        var target = preview.Changes.Where(x => x.IsAdd).ToList();
        if (target.Count != preview.ExportedRows ||
            preview.Changes.Count != target.Count + originals.Count)
            throw new InvalidOperationException("Employment snapshot counts do not match.");
        var usedNames = new HashSet<(int, string)>();
        var usedPeriods = new HashSet<(int, int, DateTime, DateTime)>();
        int nameLimit = schema.Columns["RecordName"]!.MaxLength;
        int initialsLimit = schema.Columns["Initials"]!.MaxLength;
        foreach (var change in target)
        {
            var d = change.Draft;
            string name = d.RequestedRecordName?.Trim() ?? "";
            if (!workerIds.Contains(change.WorkerUid) || !companyIds.Contains(d.FedUid))
                throw new InvalidOperationException("CSV references a missing worker or company.");
            bool ongoing = StudioDateFormat.IsOngoingEndDate(d.UntilDate);
            if (d.FromDate.Year is < 1800 or > 2100 ||
                (!ongoing && (d.UntilDate.Year is < 1800 or > 2100 ||
                              d.UntilDate < d.FromDate)) ||
                d.Initials.Any(char.IsControl) ||
                initialsLimit > 0 && d.Initials.Length > initialsLimit ||
                name.Length == 0 || name.Any(char.IsControl) ||
                nameLimit > 0 && name.Length > nameLimit)
                throw new InvalidOperationException("Invalid employment dates, names or initials.");
            if (!usedNames.Add((change.WorkerUid, name.ToUpperInvariant())))
                throw new InvalidOperationException(
                    $"Worker {change.WorkerUid} has repeated RecordName '{name}'.");
            if (!usedPeriods.Add((change.WorkerUid, d.FedUid,
                    d.FromDate.Date, d.UntilDate.Date)))
                throw new InvalidOperationException(
                    $"Worker {change.WorkerUid} has duplicate company periods with identical dates.");
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
            using (var clear = new OleDbCommand("DELETE FROM [tblCV]", _connection, tx))
                if (clear.ExecuteNonQuery() != originals.Count)
                    throw new InvalidOperationException(
                        "Employment source changed during replacement.");
            string[] insertColumns = { "RecordName", "WorkerUID", "FedUID",
                "Initials", "CVFromDate", "CVUntilDate",
                "Loan", "Trade", "Tour", "Developmental" };
            string insertSql = "INSERT INTO [tblCV] (" +
                string.Join(",", insertColumns.Select(x => "[" + x + "]")) +
                ") VALUES (" + string.Join(",", insertColumns.Select(_ => "?")) + ")";
            foreach (var change in target)
            {
                using var cmd = new OleDbCommand(insertSql, _connection, tx);
                BindEmploymentCsv(cmd, change, change.Draft.RequestedRecordName!.Trim(),
                    true, schema);
                if (cmd.ExecuteNonQuery() != 1)
                    throw new InvalidOperationException(
                        "An employment snapshot row could not be inserted.");
            }
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return preview.Changes.Count;
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
