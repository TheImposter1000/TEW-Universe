namespace TEWUniverse.Studio;

/// <summary>
/// Classify TEW IX files using the Access schema, not their filename.
/// A renamed save game must never be passed to the scenario editor.
/// </summary>
internal enum TewFileKind { Unknown, Scenario, SaveGame }

internal static class TewFileKindDetector
{
    internal static TewFileKind Detect(IEnumerable<string> tableNames)
    {
        var names = new HashSet<string>(tableNames, StringComparer.OrdinalIgnoreCase);
        if (names.Contains("tblGameInfo") &&
            (names.Contains("tblPlayerInfo") || names.Contains("tblGameLog") ||
             names.Contains("tblMatchLog")))
            return TewFileKind.SaveGame;

        if (names.Contains("tblWorker") && names.Contains("tblFed") &&
            names.Contains("tblInfo"))
            return TewFileKind.Scenario;

        return TewFileKind.Unknown;
    }
}
