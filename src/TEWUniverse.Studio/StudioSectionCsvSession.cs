using System.Data;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed partial class DatabaseSession
{
    // Called by all ExistingEditors-backed modules. Registering a new section
    // in ExistingEditors automatically gives it the same CSV capability.
    public IReadOnlyList<StudioCsvField> ReadSectionCsvSnapshot(
        string section, int recordUid)
    {
        if (section == "Workers" || !ExistingEditors.TryGet(section, out var spec))
            throw new InvalidOperationException(
                "The selected section has no verified CSV record adapter.");
        if (section == "Companies")
            return ReadCompanyCsvSnapshot(recordUid).Select(f => new StudioCsvField(
                section, recordUid, f.CompanyName, f.Table, f.RecordKey,
                f.RecordId, f.Field, f.Type, f.OriginalValue, f.OriginalValue,
                f.Editable)).ToArray();

        var record = ReadExistingRecord(section, recordUid);
        if (!record.Exists || record.Data.Rows.Count != 1)
            throw new InvalidOperationException("Selected record is no longer available.");
        var row = record.Data.Rows[0];
        string label = record.Data.Columns.Contains("Name")
            ? Display(row["Name"]) : "Record UID " + recordUid;
        return record.Data.Columns.Cast<DataColumn>()
            .Where(col => CompanyFieldRules.IsScalar(col.DataType))
            .Select(col =>
            {
                bool basic = IsEditable(col.DataType) &&
                    !col.AutoIncrement && !col.ReadOnly &&
                    string.IsNullOrWhiteSpace(col.Expression);
                bool allowed = section == "Titles"
                    ? CanEditTitleColumn(col)
                    : spec.EditableColumns.Contains(col.ColumnName,
                        StringComparer.OrdinalIgnoreCase) && basic &&
                        !col.ColumnName.Equals("UID", StringComparison.OrdinalIgnoreCase) &&
                        !col.ColumnName.Equals("Recordname", StringComparison.OrdinalIgnoreCase);
                string value = Display(row[col]);
                return new StudioCsvField(section, recordUid, label,
                    spec.Table, "UID", recordUid, col.ColumnName,
                    col.DataType.Name, value, value, allowed);
            }).ToArray();
    }

    public IReadOnlyList<StudioCsvField> ReadAwardCsvSnapshot(bool companySpecific,
        int awardUid)
    {
        string section = companySpecific ? "Company Year-End Awards" :
            "World Year-End Awards";
        var record = ReadAwards(companySpecific).SingleOrDefault(a => a.Uid == awardUid)
            ?? throw new InvalidOperationException("Award year no longer exists.");
        string table = companySpecific ? AwardsRules.CompanyTable : AwardsRules.GlobalTable;
        return record.Fields.Select(field => new StudioCsvField(
            section, awardUid, record.Label, table, "UID", awardUid,
            field.Name, field.Type.Name, field.StoredValue, field.StoredValue,
            field.Editable)).ToArray();
    }

    public IReadOnlyList<StudioCsvField> ReadRelationshipCsvSnapshot(int relationUid)
    {
        const string section = "Relationships";
        var record = ReadRelationships().SingleOrDefault(r => r.Uid == relationUid)
            ?? throw new InvalidOperationException("Selected relationship no longer exists.");
        string label = record.RecordName;
        StudioCsvField Cell(string name, string type, string value, bool edit) =>
            new(section, relationUid, label, "tblRelation", "UID", relationUid,
                name, type, value, value, edit);
        return new[] {
            Cell("UID", "Int32", record.Uid.ToString(CultureInfo.InvariantCulture), false),
            Cell("RecordName", "String", record.RecordName, false),
            Cell("Worker1", "Int32", record.Worker1.ToString(CultureInfo.InvariantCulture), false),
            Cell("Worker2", "Int32", record.Worker2.ToString(CultureInfo.InvariantCulture), false),
            Cell("P_Rel", "Int32", record.Personal.ToString(CultureInfo.InvariantCulture), true),
            Cell("R_Rel", "Int32", record.Romantic.ToString(CultureInfo.InvariantCulture), true),
            Cell("F_Rel", "Int32", record.Family.ToString(CultureInfo.InvariantCulture), true),
            Cell("Mentor", "Boolean", record.Mentor.ToString(), true)
        };
    }
}
