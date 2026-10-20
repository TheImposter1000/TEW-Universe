namespace TEWUniverse.Desktop;

/// <summary>
/// A partner text field has two meanings:
/// - No selected roster worker: REQUIRED historical NPC name (stored UID -1).
/// - Selected roster worker: OPTIONAL ring-name alias for that worker (positive UID).
/// The worker selection always decides the persisted identity.
/// </summary>
internal static class HistoricalPartnerInput
{
    internal sealed record Choice(int WorkerId, string DisplayName, bool NameOnly);

    internal static Choice Resolve(int? selectedWorkerId, string? selectedWorkerName,
        string? typedName, int position, bool allowNameOnly)
    {
        if (position is < 2 or > 3)
            throw new ArgumentOutOfRangeException(nameof(position));
        string entered = typedName?.Trim() ?? "";
        if (entered.Length > 30 || entered.Any(char.IsControl))
            throw new InvalidOperationException(
                $"Partner {position} name or ring-name alias must be at most 30 characters.");
        if (selectedWorkerId is int id)
        {
            if (id <= 0 || string.IsNullOrWhiteSpace(selectedWorkerName))
                throw new InvalidOperationException(
                    $"Partner {position} is not a valid existing wrestler.");
            string name = entered.Length > 0 ? entered : selectedWorkerName.Trim();
            if (name.Length > 30 || name.Length == 0 || name.Any(char.IsControl))
                throw new InvalidOperationException(
                    $"Partner {position} must have a valid display name (1–30 characters).");
            return new Choice(id, name, false);
        }
        if (!allowNameOnly)
            throw new InvalidOperationException(
                $"Partner {position} must be a selected existing wrestler in " +
                "safe existing-slot replacement mode. Name-only NPCs require Build new full history.");
        if (entered.Length == 0)
            throw new InvalidOperationException(
                $"Partner {position} needs either an existing wrestler selected " +
                "OR a name-only NPC typed in the adjacent text box.");
        return new Choice(-1, entered, true);
    }
}
