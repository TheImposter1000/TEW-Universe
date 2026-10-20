namespace TEWUniverse.Studio;

/// <summary>
/// Relationship pair identity is unordered for duplicate protection; changing
/// Worker1/Worker2 order on existing rows is not part of this editor.
/// Individual TEW IX relationship dimensions can coexist.
/// </summary>
internal static class RelationshipRules
{
    public static (int First, int Second) Pair(int worker1, int worker2)
        => worker1 <= worker2 ? (worker1, worker2) : (worker2, worker1);

    public static void ValidatePair(int worker1, int worker2, IReadOnlySet<int> workers)
    {
        if (worker1 < 1 || worker2 < 1 || worker1 == worker2 ||
            !workers.Contains(worker1) || !workers.Contains(worker2))
            throw new InvalidOperationException(
                "Select two distinct, existing database workers.");
    }

    public static void ValidateCodes(int personal, int romantic, int family, bool mentor,
        bool allowEmpty = false)
    {
        if (!TewCodeLabels.Choices("tblRelation", "P_Rel").Any(c => c.Code == personal) ||
            !TewCodeLabels.Choices("tblRelation", "R_Rel").Any(c => c.Code == romantic) ||
            !TewCodeLabels.Choices("tblRelation", "F_Rel").Any(c => c.Code == family))
            throw new InvalidOperationException(
                "An unverified relationship code cannot be assigned to a new record.");
        if (!allowEmpty && personal == 0 && romantic == 0 && family == 0 && !mentor)
            throw new InvalidOperationException(
                "Choose at least one relationship element, or delete an existing record.");
    }
}
