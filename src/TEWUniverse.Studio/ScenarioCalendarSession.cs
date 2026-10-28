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
        foreach (string name in new[] { "OpenYear", "OpenMonth", "ClosedYear", "ClosedMonth" })
            if (!company.Data.Columns.Contains(name))
                throw new InvalidOperationException(
                    "The company has no verified " + name +
                    " column; automatic historical limits are unavailable.");

        static int Value(DataRow record, string name) =>
            record[name] is DBNull ? 0 :
            Convert.ToInt32(record[name], CultureInfo.InvariantCulture);

        int openYear = Value(row, "OpenYear"), openMonth = Value(row, "OpenMonth");
        int closeYear = Value(row, "ClosedYear"), closeMonth = Value(row, "ClosedMonth");
        DateTime? opened = openYear > 0
            ? ScenarioDateBounds.MonthStart(openYear, openMonth) : null;
        DateTime? closed = closeYear > 0
            ? ScenarioDateBounds.MonthEnd(closeYear, closeMonth) : null;
        if (opened.HasValue && closed.HasValue && closed.Value < opened.Value)
            throw new InvalidOperationException(
                "Company opening and closure dates conflict in tblFed. " +
                "Correct them in Companies before generating.");

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
