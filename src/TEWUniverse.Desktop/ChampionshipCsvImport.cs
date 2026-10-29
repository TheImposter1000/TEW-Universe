using System.Globalization;
using System.Text;
using TEWUniverse.Studio;

namespace TEWUniverse.Desktop;

internal sealed record ChampionshipCsvDraft(
    bool Targeted,
    IReadOnlyList<GeneratedTitleReign> Reigns,
    IReadOnlyList<TitleSlotPatch> Changes);

/// <summary>
/// Imports only the three CSV shapes exported by Studio's championship tools.
/// Never silently reassigns a title or substitutes missing worker records.
/// </summary>
internal static class ChampionshipCsvImport
{
    private static readonly string[] Core = {
        "BeltUID", "BeltOrder", "Holder1", "Holder_Name1",
        "Holder2", "Holder_Name2", "BeltCaptured", "BeltLost", "Defences"
    };
    private static readonly string[] Third = { "Holder3", "Holder_Name3" };

    internal static ChampionshipCsvDraft Parse(
        string csv, int titleId, int members, bool annual,
        IReadOnlySet<int> validWorkerIds,
        IReadOnlyList<ExistingTitleSlot> existing)
    {
        if (titleId <= 0 || members is < 1 or > 3)
            throw new InvalidOperationException("Select a valid championship and format.");
        if (csv.Length is < 1 or > 2_000_000)
            throw new InvalidOperationException("CSV must contain data and be no larger than 2 MB.");
        var rows = ReadCsv(csv);
        if (rows.Count < 2 || rows.Count > 501)
            throw new InvalidOperationException("CSV needs a header and 1 to 500 reign records.");

        var header = rows[0].Select(s => s.Trim().TrimStart('\uFEFF')).ToArray();
        if (header.Distinct(StringComparer.OrdinalIgnoreCase).Count() != header.Length)
            throw new InvalidOperationException("Duplicate CSV column names.");
        var columns = header.Select((name, i) => (name, i))
            .ToDictionary(x => x.name, x => x.i, StringComparer.OrdinalIgnoreCase);
        bool targeted = columns.ContainsKey("UID");
        bool hasThird = columns.ContainsKey("Holder3") &&
                        columns.ContainsKey("Holder_Name3");
        var expected = new HashSet<string>(Core, StringComparer.OrdinalIgnoreCase);
        if (hasThird) expected.UnionWith(Third);
        if (targeted) expected.Add("UID");
        if (columns.Count != expected.Count || expected.Any(x => !columns.ContainsKey(x)))
            throw new InvalidOperationException(
                "Unrecognized championship CSV columns. Import an unmodified Studio " +
                "title-lineage draft, advanced lineage, or targeted-holder CSV. " +
                "All columns must match exactly.");
        if (members == 3 && !hasThird)
            throw new InvalidOperationException("Trios history requires Holder3 and Holder_Name3.");
        if (columns.ContainsKey("Holder3") != columns.ContainsKey("Holder_Name3"))
            throw new InvalidOperationException("Third-holder columns must appear together.");

        var history = existing.ToDictionary(x => x.Uid);
        var seenUid = new HashSet<int>();
        var reigns = new List<GeneratedTitleReign>();
        var patches = new List<TitleSlotPatch>();
        DateTime previousCaptured = DateTime.MinValue;
        DateTime previousLost = DateTime.MinValue;
        int previousOrder = 0;

        for (int n = 1; n < rows.Count; n++)
        {
            var cells = rows[n];
            if (cells.Count != header.Length)
                throw new InvalidOperationException(
                    $"CSV record {n}: expected {header.Length} values, received {cells.Count}.");
            string Cell(string name) => cells[columns[name]].Trim();
            int Number(string name) => int.TryParse(Cell(name), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int value) ? value :
                throw new InvalidOperationException($"CSV record {n}: invalid {name}.");
            short Defense()
            {
                if (!short.TryParse(Cell("Defences"), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out short value))
                    throw new InvalidOperationException($"CSV record {n}: invalid Defences.");
                return value;
            }
            DateTime Date(string name)
            {
                if (!StudioDateFormat.TryParse(Cell(name), out DateTime value))
                    throw new InvalidOperationException(
                        $"CSV record {n}: {name} needs a date like 5/12/1998 or May 12th 1998.");
                return value;
            }

            if (Number("BeltUID") != titleId)
                throw new InvalidOperationException(
                    $"CSV record {n} belongs to a different championship. " +
                    $"Every BeltUID must equal the selected title's ID {titleId}.");
            int order = Number("BeltOrder");
            if (!targeted && order != n)
                throw new InvalidOperationException(
                    $"CSV record {n}: full-history BeltOrder must be consecutive from 1.");
            if (targeted && order <= previousOrder)
                throw new InvalidOperationException(
                    "Targeted CSV must be sorted in strictly increasing BeltOrder.");
            previousOrder = order;

            int[] ids = { Number("Holder1"), Number("Holder2"),
                hasThird ? Number("Holder3") : -1 };
            string[] names = { Cell("Holder_Name1"), Cell("Holder_Name2"),
                hasThird ? Cell("Holder_Name3") : "" };
            for (int i = members; i < 3; i++)
            {
                if (ids[i] == 0 && names[i].Length == 0) ids[i] = -1;
            }
            HistoricalReignRules.ValidateHolders(ids, names, members, validWorkerIds);
            DateTime captured = Date("BeltCaptured");
            DateTime lost = Date("BeltLost");
            short defenses = Defense();
            if (captured.Year is < 1800 or > 2100 ||
                (annual
                    ? lost != new DateTime(1666, 1, 1) || defenses != -1
                    : lost.Year is < 1800 or > 2100 ||
                      lost < captured || defenses < 0))
                throw new InvalidOperationException(
                    $"CSV record {n}: incompatible reign dates or defenses.");
            if (n > 1 && (captured <= previousCaptured ||
                !targeted && !annual && captured <= previousLost))
                throw new InvalidOperationException(
                    $"CSV record {n}: capture dates overlap or are out of order.");
            previousCaptured = captured;
            previousLost = lost;

            if (targeted)
            {
                int uid = Number("UID");
                if (!seenUid.Add(uid) || !history.TryGetValue(uid, out var slot))
                    throw new InvalidOperationException(
                        $"CSV record {n}: UID {uid} is duplicated or not in this title's history.");
                if (!slot.IsNameOnly)
                    throw new InvalidOperationException(
                        $"CSV record {n}: UID {uid} already has linked holders. " +
                        "Targeted imports may modify name-only placeholders only.");
                if (slot.Order != order || slot.Captured != captured ||
                    slot.Lost != lost || slot.Defences != defenses)
                    throw new InvalidOperationException(
                        $"CSV record {n}: the saved reign UID {uid} has changed. " +
                        "Targeted import cannot alter its order, dates or defenses.");
                if (!ids.Take(members).Any(id => id > 0))
                    throw new InvalidOperationException(
                        $"CSV record {n}: targeted replacement needs a linked worker.");
                patches.Add(new TitleSlotPatch(uid, order, captured, lost, defenses,
                    ids[0], names[0], ids[1], names[1], ids[2], names[2]));
            }
            else
                reigns.Add(new GeneratedTitleReign(order, ids[0], names[0],
                    ids[1], names[1], captured, lost, defenses,
                    ids[2], names[2]));
        }
        if (!targeted && annual &&
            reigns.GroupBy(x => x.Captured.Year).Any(g => g.Count() != 1))
            throw new InvalidOperationException(
                "Annual Tournament CSV must have exactly one edition per year.");
        return new ChampionshipCsvDraft(targeted, reigns, patches);
    }

    // RFC-style quoted CSV, including escaped quotes, embedded commas, and
    // embedded newlines. No dependence on an installed Excel provider.
    private static List<List<string>> ReadCsv(string text)
    {
        var result = new List<List<string>>();
        var record = new List<string>();
        var field = new StringBuilder();
        bool quoted = false, closedQuote = false, started = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"')
                { field.Append('"'); i++; }
                else if (ch == '"') { quoted = false; closedQuote = true; }
                else field.Append(ch);
                continue;
            }
            if (ch == '"' && !started && !closedQuote)
            { quoted = true; started = true; }
            else if (ch == ',')
            {
                record.Add(field.ToString());
                field.Clear(); started = false; closedQuote = false;
            }
            else if (ch is '\r' or '\n')
            {
                record.Add(field.ToString());
                field.Clear();
                if (record.Any(value => value.Length > 0))
                    result.Add(record);
                record = new List<string>();
                started = false; closedQuote = false;
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            }
            else if (closedQuote || ch == '"')
                throw new InvalidOperationException(
                    "Malformed CSV quoting. Double quotes inside quoted values.");
            else { field.Append(ch); started = true; }
        }
        if (quoted) throw new InvalidOperationException("Unclosed CSV quoted field.");
        record.Add(field.ToString());
        if (record.Any(value => value.Length > 0)) result.Add(record);
        return result;
    }
}
