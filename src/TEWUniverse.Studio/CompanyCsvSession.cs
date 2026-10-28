using System.Data;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed partial class DatabaseSession
{
    // The CSV covers the selected company and any verified one-row related
    // sections. Multi-row relationships (brands, tours, etc.) are not safely
    // reversible through this single-company scalar-field format.
    public IReadOnlyList<CompanyCsvCell> ReadCompanyCsvSnapshot(int companyUid)
    {
        var primary = ReadSection("tblFed", companyUid);
        if (!primary.Exists || primary.Data.Rows.Count != 1)
            throw new InvalidOperationException("Select a valid existing company.");

        var companyRow = primary.Data.Rows[0];
        string name = primary.Data.Columns.Contains("Name")
            ? Display(companyRow["Name"]) : "Company UID " + companyUid;
        var cells = new List<CompanyCsvCell>();

        void Append(DataTable table, DataRow row, string recordTable,
            string recordKey, int recordId)
        {
            foreach (DataColumn col in table.Columns)
            {
                if (!CompanyFieldRules.IsScalar(col.DataType)) continue;
                string original = Display(row[col]);
                cells.Add(new CompanyCsvCell(companyUid, name, recordTable,
                    recordKey, recordId, col.ColumnName, col.DataType.Name,
                    original, original, CompanyFieldRules.CanEdit(col)));
            }
        }

        Append(primary.Data, companyRow, "tblFed", "UID", companyUid);
        foreach (var section in ReadCompanyRelated(companyUid))
        {
            if (section.Truncated || section.Rows.Rows.Count != 1) continue;
            DataRow row = section.Rows.Rows[0];
            var table = section.Rows;
            string key;
            int recordId;
            if (table.Columns.Contains("UID") &&
                row["UID"] is not DBNull &&
                int.TryParse(Display(row["UID"]), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int uid) && uid > 0)
            {
                key = "UID";
                recordId = uid;
            }
            else if (table.Columns.Contains("FedUID") &&
                row["FedUID"] is not DBNull &&
                int.TryParse(Display(row["FedUID"]), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int relatedCompany) &&
                relatedCompany == companyUid)
            {
                key = "FedUID";
                recordId = companyUid;
            }
            else continue;

            Append(table, row, section.Table, key, recordId);
        }
        return cells;
    }
}
