using TEWUniverse.Desktop;
using TEWUniverse.Studio;

// Regression checks: a weekday-based annual tournament must not reuse a
// fixed numbered day between years.
Check(1994, 3, 1, DayOfWeek.Sunday, new DateTime(1994, 3, 6));
Check(1997, 3, 1, DayOfWeek.Sunday, new DateTime(1997, 3, 2));
Check(2026, 3, 5, DayOfWeek.Sunday, new DateTime(2026, 3, 29));
Check(2024, 2, 4, DayOfWeek.Thursday, new DateTime(2024, 2, 22));
Check(2023, 2, 5, DayOfWeek.Monday, new DateTime(2023, 2, 27));

// Every supported combination must resolve inside its requested month and
// to the correct ordinal occurrence (or its last occurrence).
int combinations = 0;
for (int year = 1800; year <= 2100; year++)
for (int month = 1; month <= 12; month++)
for (int occurrence = 1; occurrence <= 5; occurrence++)
for (int day = 0; day <= 6; day++)
{
    var weekday = (DayOfWeek)day;
    var date = AnnualTournamentCalendar.ResolveDate(year, month, occurrence, weekday);
    if (date.Year != year || date.Month != month || date.DayOfWeek != weekday)
        throw new Exception($"Invalid resolved date for {year}, {month}, {occurrence}, {weekday}.");
    if (occurrence == 5)
    {
        if (date.AddDays(7).Month == month)
            throw new Exception("Last occurrence was not last.");
    }
    else if (((date.Day - 1) / 7) + 1 != occurrence)
    {
        throw new Exception("Wrong ordinal occurrence.");
    }
    combinations++;
}

foreach (var invalid in new[] { (month: 0, week: 1), (month: 13, week: 1),
                                (month: 3, week: 0), (month: 3, week: 6) })
{
    bool rejected = false;
    try { AnnualTournamentCalendar.ResolveDate(1994, invalid.month, invalid.week, DayOfWeek.Sunday); }
    catch (InvalidOperationException) { rejected = true; }
    if (!rejected) throw new Exception("Invalid event scheduling input was accepted.");
}
Console.WriteLine($"Passed annual tournament date checks for {combinations:N0} recurring schedules.");

// Historical annual cups should not be dominated by the same tiny group of
// name-only NPC winners. Reproduce the 2005–2021 case with pinned 2014 / 2018.
string?[] pinnedCupYears = Enumerable.Range(2005, 17).Select(year =>
    year == 2014 ? "Tavita Satele" :
    year == 2018 ? "Carson Cade" : null).ToArray();
string[] generatedNames = Enumerable.Range(1, 60)
    .Select(i => $"Generated NPC {i}").ToArray();

for (int seed = 0; seed < 250; seed++)
{
    var winners = AnnualTournamentWinnerPlanner.FillWinners(
        pinnedCupYears, generatedNames, new Random(seed));

    if (winners.Count != 17 || winners[9] != "Tavita Satele" ||
        winners[13] != "Carson Cade")
        throw new Exception("A pinned linked-worker tournament victory was not preserved.");

    var npcWinners = winners.Where((_, i) => pinnedCupYears[i] == null).ToArray();
    var groups = npcWinners.GroupBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
    if (npcWinners.Length != 15 || groups.Length < 12 ||
        groups.Any(g => g.Count() > 2))
        throw new Exception("A long annual cup history recycled too few NPCs.");

    for (int i = 1; i < winners.Count; i++)
        if (winners[i].Equals(winners[i - 1], StringComparison.OrdinalIgnoreCase))
            throw new Exception("The same NPC won consecutive annual tournament editions.");

    for (int i = 2; i < winners.Count; i++)
        if (pinnedCupYears[i] == null &&
            winners[i].Equals(winners[i - 2], StringComparison.OrdinalIgnoreCase))
            throw new Exception("A historical NPC won again only two tournament editions later.");
}

// A narrow generator seed list is a genuine limitation and should not cause
// an exception or fabricate new worker names beyond that source.
var smallSource = AnnualTournamentWinnerPlanner.FillWinners(
    Enumerable.Repeat<string?>(null, 12).ToArray(),
    new[] { "Green Bat", "Fran" }, new Random(19));
if (smallSource.Count != 12 ||
    smallSource.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 2)
    throw new Exception("Limited NPC source names were not handled gracefully.");

// Exclude fixed real-worker aliases even if tblGenerateWorker contains them.
var aliases = AnnualTournamentWinnerPlanner.FillWinners(
    new string?[] { null, "Dragonheart", null, null },
    new[] { "Dragonheart", "Amethyst", "Granite", "Sapphire" },
    new Random(13));
if (aliases[1] != "Dragonheart" ||
    aliases.Where((_, index) => index != 1)
        .Any(name => name.Equals("Dragonheart", StringComparison.OrdinalIgnoreCase)))
    throw new Exception("An NPC was incorrectly assigned a linked superstar's name.");

Console.WriteLine("Passed 250 randomized annual NPC variety histories and constrained-pool checks.");


// Tournament wins constrain the earliest possible championship capture in
// the SAME year, while the scheduled title history still keeps the correct
// real worker UID and the required winning year.
for (int i = 0; i < 150; i++)
{
    var cupWin = new DateTime(2020, 5, 3);
    var scheduled = ChampionshipWinScheduler.Schedule(new[]
    {
        new ChampionshipWinScheduler.Win(4975, "Dragonheart", 2020, cupWin.AddDays(1))
    });
    if (scheduled.Count != 1 || scheduled[0].WorkerId != 4975 ||
        scheduled[0].Date <= cupWin || scheduled[0].Date.Year != 2020)
        throw new Exception("2020 Dragonheart title win was incorrectly placed before the May cup.");
}

// January tournament winners are also scheduled after their dated victory.
var january = ChampionshipWinScheduler.Schedule(new[]
{
    new ChampionshipWinScheduler.Win(150, "Colby", 2020, new DateTime(2020, 1, 13))
});
if (january[0].Date < new DateTime(2020, 1, 13))
    throw new Exception("Colby's title was awarded before his January tournament victory.");

// Multiple constrained champions in one year must be chronological and
// allow the minimum spacing used by the history planner.
for (int i = 0; i < 150; i++)
{
    var multiple = ChampionshipWinScheduler.Schedule(new[]
    {
        new ChampionshipWinScheduler.Win(5, "First Winner", 2020, new DateTime(2020, 4, 1)),
        new ChampionshipWinScheduler.Win(6, "Second Winner", 2020, new DateTime(2020, 5, 4)),
        new ChampionshipWinScheduler.Win(7, "Third Winner", 2020, new DateTime(2020, 6, 1))
    });
    if (multiple.Count != 3 || multiple[0].Date < new DateTime(2020, 4, 1) ||
        multiple[1].Date < new DateTime(2020, 5, 4) ||
        multiple[2].Date < new DateTime(2020, 6, 1) ||
        multiple[1].Date < multiple[0].Date.AddDays(7) ||
        multiple[2].Date < multiple[1].Date.AddDays(7))
        throw new Exception("Same-year chronology bounds or minimum transition spacing failed.");
}

// When a tournament is too late for another same-year title win, the
// generator must reject the contradictory chronology instead of ignoring it.
bool impossibleRejected = false;
try
{
    ChampionshipWinScheduler.Schedule(new[]
    {
        new ChampionshipWinScheduler.Win(4975, "Dragonheart", 2020, new DateTime(2020, 12, 31)),
        new ChampionshipWinScheduler.Win(150, "Colby", 2020, new DateTime(2020, 12, 31))
    });
}
catch (InvalidOperationException) { impossibleRejected = true; }
if (!impossibleRejected)
    throw new Exception("Impossible year-end tournament chronology was silently accepted.");
Console.WriteLine("Passed 452 championship chronology regression checks.");

// Opening-month boundaries for titles founded midyear: the first champion
// must not start before the promotion's first month of existence.
var npcSeeds = Enumerable.Range(1, 80)
    .Select(i => new NpcNameSeed($"Historical Worker {i}", null, null)).ToArray();
var noPinnedWinners = Array.Empty<AnchoredHistoryPlanner.Anchor>();
var juneOpening = new DateTime(2002, 6, 1);
var variedJuneStarts = new HashSet<DateTime>();
for (int iteration = 0; iteration < 80; iteration++)
{
    var pwaHistory = AnchoredHistoryPlanner.GenerateFromYears(
        noPinnedWinners, npcSeeds, firstYear: 2002, lastYear: 2021,
        startMonth: 6);
    if (pwaHistory.Count == 0 ||
        pwaHistory[0].Captured.Year != 2002 ||
        pwaHistory[0].Captured.Month != 6 ||
        pwaHistory[0].Captured.Day <= 1 ||
        pwaHistory.Any(reign => reign.Captured < juneOpening || reign.Lost < juneOpening) ||
        pwaHistory[^1].Lost != new DateTime(2021, 12, 31))
        throw new Exception("An initial June 2002 reign did not start on a random valid June day.");
    variedJuneStarts.Add(pwaHistory[0].Captured);
}
if (variedJuneStarts.Count < 5)
    throw new Exception("The generated first championship date was not sufficiently varied.");

var pinnedFoundingYear = new[]
{
    new AnchoredHistoryPlanner.Anchor("Founding Champion", 100, 2002, 2002, 2021,
        new DateTime(2002, 6, 15)),
    new AnchoredHistoryPlanner.Anchor("Future Champion", 200, 2003, 2002, 2021)
};
for (int iteration = 0; iteration < 40; iteration++)
{
    var pwaHistory = AnchoredHistoryPlanner.GenerateFromYears(
        pinnedFoundingYear, npcSeeds, firstYear: 2002, lastYear: 2003,
        startMonth: 6);
    if (pwaHistory[0].Captured.Month != 6 ||
        pwaHistory[0].Captured.Day <= 1 ||
        pwaHistory.Any(reign => reign.Captured < juneOpening) ||
        !pwaHistory.Any(reign => reign.Holder1 == 100 &&
            reign.Captured.Year == 2002 &&
            reign.Captured >= new DateTime(2002, 6, 15)) ||
        !pwaHistory.Any(reign => reign.Holder1 == 200 &&
            reign.Captured.Year == 2003))
        throw new Exception("Founding-year pinned wins ignored the start month or winning years.");
}

// Default (January) still means the first eligible month is January,
// but the inaugural reign must no longer be hardcoded to January 1.
var variedJanuaryStarts = new HashSet<DateTime>();
for (int iteration = 0; iteration < 50; iteration++)
{
    var defaultStart = AnchoredHistoryPlanner.GenerateFromYears(
        noPinnedWinners, npcSeeds, firstYear: 2002, lastYear: 2002);
    if (defaultStart[0].Captured.Year != 2002 ||
        defaultStart[0].Captured.Month != 1 ||
        defaultStart[0].Captured.Day <= 1)
        throw new Exception("Default championship history must begin on a random January day.");
    variedJanuaryStarts.Add(defaultStart[0].Captured);
}
if (variedJanuaryStarts.Count < 5)
    throw new Exception("Default January opening dates do not vary across previews.");

// Fixed wins during the founding month bound the random inaugural capture.
// A following real title win must never be preceded by a later NPC start.
var earlyPinned = new[]
{
    new AnchoredHistoryPlanner.Anchor("Early Champion", 541, 2002,
        2002, 2003, new DateTime(2002, 6, 2))
};
for (int iteration = 0; iteration < 50; iteration++)
{
    var earlyHistory = AnchoredHistoryPlanner.GenerateFromYears(
        earlyPinned, npcSeeds, firstYear: 2002, lastYear: 2003,
        startMonth: 6);
    var real = earlyHistory.Single(r => r.Holder1 == 541);
    if (earlyHistory[0].Captured.Month != 6 ||
        earlyHistory[0].Captured.Day <= 1 ||
        earlyHistory[0].Captured > real.Captured)
        throw new Exception("Random first capture was scheduled after an early pinned June winner.");
}

// The start-month setting is deliberately separate from annual tournament
// recurring event dates: a Cup edition still uses its chosen event week/day.
var annualCup = AnchoredHistoryPlanner.GenerateFromYears(
    noPinnedWinners, npcSeeds, firstYear: 2002, lastYear: 2003,
    annualTournament: true, eventMonth: 8, eventWeek: 4,
    eventWeekday: DayOfWeek.Saturday, startMonth: 6);
if (annualCup.Count != 2 ||
    annualCup[0].Captured != AnnualTournamentCalendar.ResolveDate(
        2002, 8, 4, DayOfWeek.Saturday) ||
    annualCup[1].Captured != AnnualTournamentCalendar.ResolveDate(
        2003, 8, 4, DayOfWeek.Saturday))
    throw new Exception("The championship starting month incorrectly changed Cup dates.");

foreach (var badMonth in new[] { 0, 13, -1 })
{
    bool rejected = false;
    try
    {
        AnchoredHistoryPlanner.GenerateFromYears(
            noPinnedWinners, npcSeeds, 2002, 2002, startMonth: badMonth);
    }
    catch (InvalidOperationException) { rejected = true; }
    if (!rejected)
        throw new Exception("An invalid championship history starting month was accepted.");
}

var multiPins = new[]
{
    new ChampionshipWinScheduler.Win(1, "Winner A", 2002),
    new ChampionshipWinScheduler.Win(2, "Winner B", 2002)
};
for (int iteration = 0; iteration < 40; iteration++)
{
    var scheduled = ChampionshipWinScheduler.Schedule(multiPins, juneOpening);
    if (scheduled.Count != 2 ||
        scheduled[0].Date < juneOpening || scheduled[1].Date < juneOpening ||
        scheduled[1].Date < scheduled[0].Date.AddDays(7))
        throw new Exception("Same-year fixed wins violated the opening month.");
}
bool impossibleOpeningRejected = false;
try
{
    ChampionshipWinScheduler.Schedule(multiPins, new DateTime(2002, 12, 29));
}
catch (InvalidOperationException) { impossibleOpeningRejected = true; }
if (!impossibleOpeningRejected)
    throw new Exception("A logically impossible December opening constraint was ignored.");

Console.WriteLine("Passed optional midyear championship opening-month regression checks.");

// Exact linked superstar reigns must not be interrupted by a randomly
// generated NPC, and the preview/import record uses inclusive days held.
// The two-year anniversary is a calendar rule, not a hardcoded 730 days.
for (int seed = 0; seed < 100; seed++)
{
    var superstars = new[]
    {
        new AnchoredHistoryPlanner.Anchor(
            "Tavitia", 4982, 2016, 2014, 2022, ReignYears: 2),
        new AnchoredHistoryPlanner.Anchor(
            "Zara Ellis", 4966, 2020, 2014, 2022, ReignDays: 719)
    };
    var history = AnchoredHistoryPlanner.GenerateFromYears(
        superstars, npcSeeds, firstYear: 2014, lastYear: 2024);
    var tavitia = history.Single(r => r.Holder1 == 4982 &&
        r.HolderName1 == "Tavitia" && r.Captured.Year == 2016);
    var zara = history.Single(r => r.Holder1 == 4966 &&
        r.HolderName1 == "Zara Ellis" && r.Captured.Year == 2020);
    if (tavitia.Lost != tavitia.Captured.AddYears(2).AddDays(-1) ||
        zara.Lost != zara.Captured.AddDays(718) ||
        (zara.Lost - zara.Captured).Days + 1 != 719)
        throw new Exception("An exact superstar reign was cut short or counted incorrectly.");

    var chron = history.OrderBy(r => r.Captured).ToArray();
    for (int j = 1; j < chron.Length; j++)
        if (chron[j].Captured != chron[j - 1].Lost.AddDays(1))
            throw new Exception("Generated dates have a gap or overlapping reign.");
    if (chron[^1].Lost != new DateTime(2024, 12, 31))
        throw new Exception("Generated title history stopped before the chosen end date.");

    var afterTavitia = chron.Single(r => r.Captured == tavitia.Lost.AddDays(1));
    var afterZara = chron.Single(r => r.Captured == zara.Lost.AddDays(1));
    if (afterTavitia.Holder1 == tavitia.Holder1 ||
        afterZara.Holder1 == zara.Holder1)
        throw new Exception("A fixed-length holder was never succeeded by a different champion.");
}

// Leap day/year boundaries: the calendar anniversary determines the end of
// a two-year streak (not a fixed 730-day approximation).
var leap = ChampionshipWinScheduler.Schedule(new[]
{
    new ChampionshipWinScheduler.Win(7, "Leap Champion", 2020, ReignYears: 2)
}, historyStart: new DateTime(2020, 1, 1),
    historyEnd: new DateTime(2023, 12, 31));
if (leap.Count != 1 ||
    ChampionshipWinScheduler.RequiredNextCaptureDate(leap[0]) !=
    leap[0].Date.AddYears(2))
    throw new Exception("Two calendar years were not scheduled using AddYears.");

// Within-year schedule must move the capture EARLIER if the exact reign would
// otherwise run beyond the last day of the user-defined history.
for (int i = 0; i < 100; i++)
{
    var single = AnchoredHistoryPlanner.GenerateFromYears(new[]
    {
        new AnchoredHistoryPlanner.Anchor("Zara Ellis", 4966, 2020,
            2020, 2022, ReignDays: 719)
    }, npcSeeds, firstYear: 2020, lastYear: 2022);
    var zara = single.Single(x => x.Holder1 == 4966);
    if (zara.Captured.Year != 2020 || (zara.Lost - zara.Captured).Days + 1 != 719)
        throw new Exception("The fixed 719-day reign moved out of 2020 or was shortened.");
}

// Infeasible chronology must never quietly edit a fixed winning year or
// reduce a requested exact reign. A 2018 two-year reign conflicts with a
// second wrestler's mandatory 2019 title capture.
bool overlappingPinnedRejected = false;
try
{
    AnchoredHistoryPlanner.GenerateFromYears(new[]
    {
        new AnchoredHistoryPlanner.Anchor("Tavitia", 4982, 2018,
            2018, 2020, ReignYears: 2),
        new AnchoredHistoryPlanner.Anchor("Another Champion", 503, 2019,
            2018, 2020)
    }, npcSeeds, firstYear: 2018, lastYear: 2020);
}
catch (InvalidOperationException) { overlappingPinnedRejected = true; }
if (!overlappingPinnedRejected)
    throw new Exception("A conflicting 2019 pinned victory shortened a two-year reign.");

bool beyondHistoryRejected = false;
try
{
    AnchoredHistoryPlanner.GenerateFromYears(new[]
    {
        new AnchoredHistoryPlanner.Anchor("Zara Ellis", 4966, 2021,
            2020, 2021, ReignDays: 719)
    }, npcSeeds, firstYear: 2020, lastYear: 2021);
}
catch (InvalidOperationException) { beyondHistoryRejected = true; }
if (!beyondHistoryRejected)
    throw new Exception("A late-2021 start was incorrectly allowed to run past the timeline end.");

// Cross-title Cup victories still restrict when an exact reign may start.
// Impossible lower and upper bounds must reject conflicting requirements.
bool tournamentDateConflictRejected = false;
try
{
    ChampionshipWinScheduler.Schedule(new[]
    {
        new ChampionshipWinScheduler.Win(4966, "Zara Ellis", 2020,
            NotBefore: new DateTime(2020, 12, 20), ReignDays: 719)
    }, historyStart: new DateTime(2020, 1, 1),
        historyEnd: new DateTime(2022, 1, 1));
}
catch (InvalidOperationException) { tournamentDateConflictRejected = true; }
if (!tournamentDateConflictRejected)
    throw new Exception("A prior Cup victory was ignored to force an exact duration.");

bool annualLengthRejected = false;
try
{
    AnchoredHistoryPlanner.GenerateFromYears(new[]
    {
        new AnchoredHistoryPlanner.Anchor("Cup Winner", 400, 2020,
            2020, 2020, ReignDays: 719)
    }, npcSeeds, firstYear: 2020, lastYear: 2020,
        annualTournament: true, eventMonth: 5, eventWeek: 1,
        eventWeekday: DayOfWeek.Sunday);
}
catch (InvalidOperationException) { annualLengthRejected = true; }
if (!annualLengthRejected)
    throw new Exception("Annual Cup accepted a continuing championship reign length.");

Console.WriteLine("Passed 200 randomized exact superstar reigns, continuity and conflict checks.");

static void Check(int year, int month, int occurrence, DayOfWeek weekday, DateTime expected)
{
    var actual = AnnualTournamentCalendar.ResolveDate(year, month, occurrence, weekday);
    if (actual != expected)
        throw new Exception($"Expected {expected:yyyy-MM-dd}, got {actual:yyyy-MM-dd}.");
}
