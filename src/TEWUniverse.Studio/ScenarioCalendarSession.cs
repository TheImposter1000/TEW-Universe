using System.Data;
using System.Data.OleDb;
using System.Globalization;

namespace TEWUniverse.Studio;

internal sealed partial class DatabaseSession
{
    public DateTime ReadScenarioStart()
    {
        using var cmd = new OleDbCommand(
            "SELECT [StartYear], [StartMonth] FROM [tblInfo]", _connection);
        using var reader = cmd.ExecuteReader();
        if (reader == null || !reader.Read())
            throw new InvalidOperationException(
                "tblInfo is missing its scenario start record.");
        int year = Convert.ToInt32(reader["StartYear"], CultureInfo.InvariantCulture);
        int month = Convert.ToInt32(reader["StartMonth"], CultureInfo.InvariantCulture);
        if (reader.Read())
            throw new InvalidOperationException(
                "tblInfo contains multiple rows; the scenario start is ambiguous.");
        return ScenarioDateBounds.MonthStart(year, month);
    }

    public ScenarioChronology ReadTitleScenarioChronology(int beltId,
        int? selectedCompany = null, bool includeCompany = true)
    {
        DateTime scenarioStart = ReadScenarioStart();
        if (!includeCompany)
            return new ScenarioChronology(scenarioStart, null,
                "Company date override", null, null);
        int? companyId = selectedCompany is > 0
            ? selectedCompany : ReadTitleOriginalCompany(beltId);
        if (!companyId.HasValue)
            return new ScenarioChronology(
                scenarioStart, null, "No company linked to this title", null, null);

        var company = ReadSection("tblFed", companyId.Value);
        if (!company.Exists || company.Data.Rows.Count != 1)
            throw new InvalidOperationException(
                "The championship's company UID " + companyId +
                " does not exist in tblFed.");
        DataRow row = company.Data.Rows[0];
        var (opened, closed) = ScenarioDateBounds.ReadCompanyDates(row);
        if (!row.Table.Columns.Contains("CompanyOpening") &&
            !row.Table.Columns.Contains("OpenYear"))
            throw new InvalidOperationException(
                "This company has no recognized opening-date field.");
        if (!row.Table.Columns.Contains("CompanyClosing") &&
            !row.Table.Columns.Contains("ClosedYear"))
            throw new InvalidOperationException(
                "This company has no recognized closing-date field.");
        string companyName = company.Data.Columns.Contains("Name")
            ? Display(row["Name"]) : "Company UID " + companyId;
        return new ScenarioChronology(scenarioStart, companyId, companyName, opened, closed);
    }

    public void UpdateScenarioStart(int year, int month)
    {
        DateTime next = ScenarioDateBounds.MonthStart(year, month);
        DateTime original = ReadScenarioStart();
        if (next == original) return;
        EnsureUnchanged();
        using var tx = _connection.BeginTransaction();
        try
        {
            using var cmd = new OleDbCommand(
                "UPDATE [tblInfo] SET [StartYear]=?, [StartMonth]=? " +
                "WHERE [StartYear]=? AND [StartMonth]=?", _connection, tx);
            cmd.Parameters.Add("newYear", OleDbType.Integer).Value = year;
            cmd.Parameters.Add("newMonth", OleDbType.Integer).Value = month;
            cmd.Parameters.Add("oldYear", OleDbType.Integer).Value = original.Year;
            cmd.Parameters.Add("oldMonth", OleDbType.Integer).Value = original.Month;
            if (cmd.ExecuteNonQuery() != 1)
                throw new InvalidOperationException(
                    "The scenario start changed in tblInfo. No changes committed.");
            tx.Commit();
            _openedStamp = GetStamp(PathOnDisk);
        }
        catch
        {
            try { tx.Rollback(); } catch { }
            throw;
        }
    }
}
