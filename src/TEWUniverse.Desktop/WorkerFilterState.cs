namespace TEWUniverse.Desktop;

/// <summary>
/// The filter key, not a label or ComboBox item reference, is stable across
/// rebuilding a fresh set of options after a database save.
/// </summary>
internal sealed record WorkerFilterChoice(string Key, string Label)
{
    public override string ToString() => Label;
}

internal sealed record WorkerFilterState(
    string Active, string Picture, string Gender, string Company,
    string Role, string Nationality, string Region, string MinimumAge, string MaximumAge)
{
    public static WorkerFilterChoice? Choose(
        IEnumerable<WorkerFilterChoice> choices, string? previousKey)
    {
        var values = choices.ToList();
        return values.FirstOrDefault(item =>
            string.Equals(item.Key, previousKey, StringComparison.OrdinalIgnoreCase))
            ?? values.FirstOrDefault(item => item.Key.Length == 0)
            ?? values.FirstOrDefault();
    }
}
