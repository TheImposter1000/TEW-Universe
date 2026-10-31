using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// Explicit migration for ALREADY-RECORDED title and CV RecordName values.
/// All writes are one transaction. Only the identifier text changes: belt
/// order, reign holders, dates, defenses, worker/company UIDs, and other
/// scenario fields are not altered.
/// </summary>
internal sealed partial class DatabaseSession
{
    public RecordNameRepairPlan PreviewTitleRecordNameRepairs(int? selectedBeltUid)
    {
        if (selectedBeltUid.HasValue)
        {
            var selected = ReadSection("tblBelt", selectedBeltUid.Value);
            if (!selected.Exists)
                throw new InvalidOperationException("The selected championship does not exist.");
        }
        ValidateWorkerExtraSchema("tblBelt", "UID", "Name");
        ValidateWorkerExtraSchema("tblBeltPreHistory",
            "UID", "BeltUID", "BeltOrder", "Recordname");

        var titleNames = new Dictionary<int, string>();
        using (var command = new OleDbCommand(
            "SELECT [UID],[Name] FROM [tblBelt]", _connection))
        using (var reader = command.ExecuteReader())
        {
            while (reader?.Read() == true)
            {
                int id = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
                titleNames.Add(id, reader.IsDBNull(1) ? "" :
                    Convert.ToString(reader.GetValue(1), CultureInfo.InvariantCulture) ?? "");
            }
        }
        var history = new List<TitleRecordNameRow>();
        using (var command = new OleDbCommand(
            "SELECT [UID],[BeltUID],[BeltOrder],[Recordname] " +
            "FROM [tblBeltPreHistory]", _connection))
        using (var reader = command.ExecuteReader())
        {
            while (reader?.Read() == true)
            {
                int uid = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
                int titleId = Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture);
                int order = Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture);
                string oldName = reader.IsDBNull(3) ? "" :
                    Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? "";
                titleNames.TryGetValue(titleId, out string? title);
                history.Add(new TitleRecordNameRow(uid, titleId, order, title ?? "", oldName));
            }
        }
        return RecordNameRepairPlanner.Titles(history, selectedBeltUid);
    }

    public RecordNameRepairPlan PreviewEmploymentRecordNameRepairs(int? selectedWorkerUid)
    {
        if (selectedWorkerUid.HasValue) CheckLinkedWorker(selectedWorkerUid.Value);
        ValidateWorkerExtraSchema("tblCV", "RecordName", "WorkerUID", "FedUID",
            "Initials", "CVFromDate", "CVUntilDate");
        var companyNames = ReadEmploymentCompanies().ToDictionary(c => c.Id, c => c.Name);
        var history = new List<EmploymentRecordNameRow>();
        using (var command = new OleDbCommand(
            "SELECT [RecordName],[WorkerUID],[FedUID],[Initials]," +
            "[CVFromDate],[CVUntilDate] FROM [tblCV]", _connection))
        using (var reader = command.ExecuteReader())
        {
            while (reader?.Read() == true)
                history.Add(new EmploymentRecordNameRow(
                    Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
                    Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture),
                    reader.IsDBNull(3) ? "" :
                        Convert.ToString(reader.GetValue(3), CultureInfo.InvariantCulture) ?? "",
                    companyNames.GetValueOrDefault(
                        Convert.ToInt32(reader.GetValue(2), CultureInfo.InvariantCulture)) ?? "",
                    reader.GetValue(4) is DateTime start ? start : new DateTime(1666, 1, 1),
                    reader.GetValue(5) is DateTime end ? end : new DateTime(1666, 1, 1),
                    reader.IsDBNull(0) ? "" :
                        Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture) ?? ""));
        }
        return RecordNameRepairPlanner.Employment(history, selectedWorkerUid);
    }

    public int ApplyRecordNameRepairs(RecordNameRepairPlan preview)
    {
        if (preview.Table is not ("tblBeltPreHistory" or "tblCV") ||
            preview.Changes.Count == 0)
            throw new InvalidOperationException("No supported record-name changes were previewed.");
        bool employment = preview.Table == "tblCV";
        bool duplicateSource = employment
            ? preview.Changes.GroupBy(c => (
                c.OwnerUid, c.ReferenceUid, c.From, c.Until,
                Name: c.OldName.ToUpperInvariant())).Any(g => g.Count() > 1)
            : preview.Changes.Select(c => c.OldName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != preview.Changes.Count;
        if (preview.Changes.Any(change => change.Table != preview.Table) ||
            duplicateSource ||
            (!employment && preview.Changes.Select(c => c.NewName)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() != preview.Changes.Count))
            throw new InvalidOperationException(
                "The preview contains ambiguous original records. No changes made.");

        // The app can edit a full title history or a worker's CV records.
        // Re-run the plan on the currently loaded data; the exact preview must
        // still match. A stale, reordered or externally changed database is
        // never silently migrated.
        var actual = preview.Table == "tblCV"
            ? PreviewEmploymentRecordNameRepairs(preview.SelectedOwnerUid)
            : PreviewTitleRecordNameRepairs(preview.SelectedOwnerUid);
        if (actual.ScannedCount != preview.ScannedCount ||
            !actual.Changes.SequenceEqual(preview.Changes))
            throw new InvalidOperationException(
                "Historical records have changed since the preview. Review a fresh preview before applying.");

        var schema = ValidateWorkerExtraSchema(preview.Table,
            preview.Table == "tblCV" ? "RecordName" : "Recordname");
        string nameField = preview.Table == "tblCV" ? "RecordName" : "Recordname";
        var col = schema.Columns[nameField]!;
        if (col.DataType != typeof(string))
            throw new InvalidOperationException("The RecordName schema is not a text column.");
        int limit = col.MaxLength;
        foreach (var change in preview.Changes)
            if (string.IsNullOrWhiteSpace(change.OldName) ||
                string.IsNullOrWhiteSpace(change.NewName) ||
                change.NewName.Any(char.IsControl) ||
                limit > 0 && change.NewName.Length > limit)
                throw new InvalidOperationException(
                    $"Record '{change.OldName}' cannot be renamed to " +
                    $"'{change.NewName}' within this MDB's name limit.");

        EnsureUnchanged();
        using var transaction = _connection.BeginTransaction();
        try
        {
            // A tblCV RecordName is not unique. Only title history needs
            // table-wide name collision prevention. Employment writes use
            // worker, company, exact date range and old name together.
            var allNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var command = new OleDbCommand(
                $"SELECT [{nameField}] FROM [{preview.Table}]", _connection, transaction))
            using (var reader = command.ExecuteReader())
            {
                while (reader?.Read() == true)
                    if (!reader.IsDBNull(0))
                        allNames.Add(Convert.ToString(reader.GetValue(0),
                            CultureInfo.InvariantCulture) ?? "");
            }
            if (!employment)
            {
                var changing = preview.Changes.Select(c => c.OldName)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var result in preview.Changes)
                    if (allNames.Contains(result.NewName) &&
                        !changing.Contains(result.NewName))
                        throw new InvalidOperationException(
                            $"Title RecordName '{result.NewName}' already belongs to " +
                            "another record. No changes made.");
            }

            // Renames may be a permutation of existing names. Stage all changed
            // keys under unique temporary names first, then assign final names.
            // This also handles Access unique indexes on RecordName.
            var staged = new List<(RecordNameRepair Change, string Temporary)>();
            foreach (var change in preview.Changes)
            {
                string temporary;
                do
                {
                    temporary = "~RN" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                }
                while (!allNames.Add(temporary));
                if (limit > 0 && temporary.Length > limit)
                    throw new InvalidOperationException(
                        "The MDB record-name field is too short for a safe atomic migration.");
                UpdateName(change, change.OldName, temporary, transaction);
                staged.Add((change, temporary));
            }
            foreach (var (change, temporary) in staged)
                UpdateName(change, temporary, change.NewName, transaction);

            transaction.Commit();
            _openedStamp = GetStamp(PathOnDisk);
            return preview.Changes.Count;
        }
        catch
        {
            try { transaction.Rollback(); } catch { }
            throw;
        }
    }

    private void UpdateName(RecordNameRepair change, string from, string to,
        OleDbTransaction transaction)
    {
        bool title = change.Table == "tblBeltPreHistory";
        string sql = title
            ? "UPDATE [tblBeltPreHistory] SET [Recordname]=? WHERE " +
              "[UID]=? AND [BeltUID]=? AND [BeltOrder]=? AND [Recordname]=?"
            : "UPDATE [tblCV] SET [RecordName]=? WHERE " +
              "[WorkerUID]=? AND [FedUID]=? AND [CVFromDate]=? " +
              "AND [CVUntilDate]=? AND [RecordName]=?";
        using var command = new OleDbCommand(sql, _connection, transaction);
        command.Parameters.Add("new", OleDbType.VarWChar).Value = to;
        if (title)
        {
            if (change.Uid is not int uid || change.Order is not int order)
                throw new InvalidOperationException("Missing verified title history identity.");
            command.Parameters.Add("uid", OleDbType.Integer).Value = uid;
            command.Parameters.Add("belt", OleDbType.Integer).Value = change.OwnerUid;
            command.Parameters.Add("order", OleDbType.SmallInt).Value = order;
        }
        else
        {
            if (change.ReferenceUid is not int fed ||
                change.From is not DateTime start ||
                change.Until is not DateTime end)
                throw new InvalidOperationException("Missing verified employment history identity.");
            command.Parameters.Add("worker", OleDbType.Integer).Value = change.OwnerUid;
            command.Parameters.Add("fed", OleDbType.Integer).Value = fed;
            command.Parameters.Add("from", OleDbType.DBTimeStamp).Value = start;
            command.Parameters.Add("until", OleDbType.DBTimeStamp).Value = end;
        }
        command.Parameters.Add("old", OleDbType.VarWChar).Value = from;
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException(
                $"Record '{from}' did not match exactly one original row. " +
                "All changes were rolled back.");
    }
}
