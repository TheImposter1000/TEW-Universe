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
        var sourceCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var duplicateWorkers = new HashSet<int>();
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
                if (matches.Count == 0)
                    throw new InvalidOperationException(
                        $"Worker {worker}, '{change.OriginalName}': the original period " +
                        "is missing or has changed. Re-export from the active MDB.");
                int alreadyUsed = sourceCounts.GetValueOrDefault(change.ExpectedFingerprint);
                if (alreadyUsed >= matches.Count)
                    throw new InvalidOperationException(
                        $"Worker {worker}, '{change.OriginalName}': more changes target " +
                        "this original period than there are matching stored rows.");
                sourceCounts[change.ExpectedFingerprint] = alreadyUsed + 1;
                if (matches.Count > 1)
                    duplicateWorkers.Add(worker);
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

        // A row can be readable from Access but not match the original-row
        // UPDATE predicate (for example, legacy NULL fields normalized for
        // display). Probe with the SAME predicate used for staging and deletion.
        // Reconcile such workers by WorkerUID, preserving the raw MDB values
        // of their unchanged historical rows.
        EnsureUnchanged();
        foreach (var change in preview.Changes.Where(x => !x.IsAdd &&
            !duplicateWorkers.Contains(x.WorkerUid)))
        {
            var original = originals.First(x => x.WorkerUid == change.WorkerUid &&
                EmploymentCsvCodec.Fingerprint(x).Equals(change.ExpectedFingerprint,
                    StringComparison.OrdinalIgnoreCase));
            using var probe = new OleDbCommand(
                "SELECT COUNT(*) FROM [tblCV] WHERE " + EmploymentRecordPredicate.Sql,
                _connection);
            EmploymentRecordPredicate.Bind(probe, original, schema);
            int matches = Convert.ToInt32(probe.ExecuteScalar(), CultureInfo.InvariantCulture);
            if (matches != 1)
                duplicateWorkers.Add(change.WorkerUid);
        }

        // tblCV has no verified unique row UID. An unmatchable original is
        // reconciled by its worker only, inside the same rollback-safe transaction.
        // Refuse this path for layouts with any unverified extra columns.
        if (duplicateWorkers.Count > 0)
        {
            var supported = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                "RecordName", "WorkerUID", "FedUID", "Initials",
                "CVFromDate", "CVUntilDate", "Loan", "Trade", "Tour", "Developmental"
            };
            if (schema.Columns.Cast<DataColumn>().Any(c => !supported.Contains(c.ColumnName)))
                throw new InvalidOperationException(
                    "The database contains additional tblCV fields. Cannot safely " +
                    "rebuild indistinguishable employment rows without preserving them.");
        }

        using var tx = _connection.BeginTransaction();
        try
        {
            // Repair only workers for whom full-row SQL cannot uniquely locate
            // the original. Preserve their unchanged raw database values.
            foreach (int worker in duplicateWorkers)
                ReconcileEmploymentWorker(worker, originals, replacements, deletions, schema, tx);

            // Delete explicitly requested rows first. The original row's
            // fingerprint and worker/name identity were verified above;
            // every delete must affect exactly one record.
            foreach (var change in deletions.Where(x => !duplicateWorkers.Contains(x.WorkerUid)))
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
            foreach (var (change, name) in replacements.Where(x => !x.Change.IsAdd &&
                !duplicateWorkers.Contains(x.Change.WorkerUid)))
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
            foreach (var (change, name) in replacements.Where(x => x.Change.IsAdd &&
                !duplicateWorkers.Contains(x.Change.WorkerUid)))
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

    /// <summary>
    /// tblCV has no verified unique row UID. Reconcile one worker's history
    /// using the WorkerUID predicate, not fragile comparisons on legacy NULLs.
    /// Original rows not edited by the CSV retain their raw MDB values,
    /// including actual NULLs, exact timestamps, and stored Boolean values.
    /// This executes inside the caller's all-or-nothing transaction.
    /// </summary>
    private void ReconcileEmploymentWorker(int worker,
        IReadOnlyList<WorkerEmploymentEntry> snapshot,
        IReadOnlyList<(EmploymentCsvChange Change, string FinalName)> replacements,
        IReadOnlyList<EmploymentCsvChange> deletions,
        DataTable schema, OleDbTransaction tx)
    {
        string[] cols = { "RecordName", "WorkerUID", "FedUID", "Initials",
            "CVFromDate", "CVUntilDate", "Loan", "Trade", "Tour", "Developmental" };
        var existing = new List<(WorkerEmploymentEntry Entry, object[] Values)>();
        using (var select = new OleDbCommand(
            "SELECT " + string.Join(",", cols.Select(x => "[" + x + "]")) +
            " FROM [tblCV] WHERE [WorkerUID]=? ORDER BY [CVFromDate]",
            _connection, tx))
        {
            select.Parameters.Add("worker", OleDbType.Integer).Value = worker;
            using var reader = select.ExecuteReader();
            while (reader?.Read() == true)
            {
                var raw = new object[cols.Length];
                reader.GetValues(raw);
                existing.Add((new WorkerEmploymentEntry(
                    FieldText(raw[0]),
                    Convert.ToInt32(raw[1], CultureInfo.InvariantCulture),
                    Convert.ToInt32(raw[2], CultureInfo.InvariantCulture),
                    FieldText(raw[3]),
                    raw[4] is DateTime start ? start : StudioDateFormat.OngoingEndDate,
                    raw[5] is DateTime end ? end : StudioDateFormat.OngoingEndDate,
                    Flag(raw[6]), Flag(raw[7]), Flag(raw[8]), Flag(raw[9])), raw));
            }
        }

        // Before deleting anything, confirm the worker's current set of
        // original rows is exactly what the CSV plan validated, including
        // multiplicity of physically identical periods.
        var expected = snapshot.Where(x => x.WorkerUid == worker)
            .GroupBy(EmploymentCsvCodec.Fingerprint)
            .ToDictionary(x => x.Key, x => x.Count());
        var actual = existing.GroupBy(x => EmploymentCsvCodec.Fingerprint(x.Entry))
            .ToDictionary(x => x.Key, x => x.Count());
        if (expected.Count != actual.Count ||
            expected.Any(x => actual.GetValueOrDefault(x.Key) != x.Value))
            throw new InvalidOperationException(
                $"Worker {worker}: stored employment history changed after preview. " +
                "Nothing was imported.");

        var remaining = existing.ToList();
        var inserts = new List<object[]>();

        void ConsumeOriginal(EmploymentCsvChange change)
        {
            int index = remaining.FindIndex(row =>
                row.Entry.WorkerUid == change.WorkerUid &&
                string.Equals(row.Entry.RecordName, change.OriginalName,
                    StringComparison.OrdinalIgnoreCase) &&
                EmploymentCsvCodec.Fingerprint(row.Entry).Equals(
                    change.ExpectedFingerprint, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                throw new InvalidOperationException(
                    $"Worker {worker}, '{change.OriginalName}': original period " +
                    "was already consumed or no longer exists. Import cancelled.");
            remaining.RemoveAt(index);
        }

        static object[] NewValues(EmploymentCsvChange change, string name)
        {
            var d = change.Draft;
            return new object[] { name, change.WorkerUid, d.FedUid, d.Initials,
                d.FromDate.Date, d.UntilDate.Date, d.Loan, d.Trade, d.Tour,
                d.Developmental };
        }

        foreach (var change in deletions.Where(x => x.WorkerUid == worker))
            ConsumeOriginal(change);
        foreach (var (change, name) in replacements.Where(x => x.Change.WorkerUid == worker))
        {
            if (!change.IsAdd)
                ConsumeOriginal(change);
            inserts.Add(NewValues(change, name));
        }

        var finalRows = remaining.Select(x => x.Values).Concat(inserts).ToList();
        using (var remove = new OleDbCommand(
            "DELETE FROM [tblCV] WHERE [WorkerUID]=?", _connection, tx))
        {
            remove.Parameters.Add("worker", OleDbType.Integer).Value = worker;
            if (remove.ExecuteNonQuery() != existing.Count)
                throw new InvalidOperationException(
                    $"Worker {worker}: employment periods changed while reconciling.");
        }
        foreach (var values in finalRows)
        {
            using var insert = new OleDbCommand(
                "INSERT INTO [tblCV] (" +
                string.Join(",", cols.Select(x => "[" + x + "]")) +
                ") VALUES (" + string.Join(",", cols.Select(_ => "?")) + ")",
                _connection, tx);
            for (int i = 0; i < cols.Length; i++)
                insert.Parameters.Add(cols[i],
                    ToOleDbType(schema.Columns[cols[i]]!.DataType)).Value = values[i];
            if (insert.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    $"Worker {worker}: an employment period could not be restored. " +
                    "The entire import will be rolled back.");
        }
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
