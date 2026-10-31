namespace TEWUniverse.Studio;

/// <summary>
/// Validates the exact inputs used to generate a historical tblCV RecordName.
/// This is distinct from saving a period: no missing dates are ever guessed.
/// </summary>
internal enum EmploymentNameField { None, Initials, From, Until }

internal sealed record EmploymentNameGenerationResult(
    string? RecordName, string? Error, EmploymentNameField Field)
{
    internal bool IsValid => RecordName != null;
}

internal static class EmploymentNameGeneration
{
    internal static EmploymentNameGenerationResult Validate(
        string? companyName, string? fromText, string? untilText)
    {
        string label = (companyName ?? "").Trim();
        if (label.Length == 0)
            return new(null,
                "Select a company with a name, or enter initials for an unlisted historical company.",
                EmploymentNameField.Initials);
        if (label.Any(char.IsControl))
            return new(null, "Company name cannot contain control characters.",
                EmploymentNameField.Initials);

        string from = (fromText ?? "").Trim();
        if (from.Length == 0)
            return new(null, "Enter the From date before generating a name (for example, 5/12/1998).",
                EmploymentNameField.From);
        if (!StudioDateFormat.TryParse(from, out DateTime start))
            return new(null, "The From date is invalid: '" + from +
                "'. Use 5/12/1998 or May 12th 1998.",
                EmploymentNameField.From);
        if (start.Year is < 1800 or > 2100)
            return new(null, "The From date must be between 1800 and 2100.",
                EmploymentNameField.From);

        string until = (untilText ?? "").Trim();
        if (until.Length == 0)
            return new(null, "Enter the Until date before generating a name (for example, 6/30/2002).",
                EmploymentNameField.Until);
        if (!StudioDateFormat.TryParse(until, out DateTime end))
            return new(null, "The Until date is invalid: '" + until +
                "'. Use 6/30/2002 or June 30th 2002.",
                EmploymentNameField.Until);
        bool ongoing = StudioDateFormat.IsOngoingEndDate(end);
        if (!ongoing && (end.Year is < 1800 or > 2100))
            return new(null,
                "The Until date must be between 1800 and 2100, or 01/01/1666 for still active.",
                EmploymentNameField.Until);
        if (!ongoing && end.Date < start.Date)
            return new(null,
                "The Until date cannot be earlier than the From date, unless it is 01/01/1666 (ongoing).",
                EmploymentNameField.Until);

        return new(StudioRecordNames.Employment(label, start, end),
            null, EmploymentNameField.None);
    }
}
