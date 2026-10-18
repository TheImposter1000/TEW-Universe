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
for (int iteration = 0; iteration < 40; iteration++)
{
    var pwaHistory = AnchoredHistoryPlanner.GenerateFromYears(
        noPinnedWinners, npcSeeds, firstYear: 2002, lastYear: 2021,
        startMonth: 6);
    if (pwaHistory.Count == 0 || pwaHistory[0].Captured != juneOpening ||
        pwaHistory.Any(reign => reign.Captured < juneOpening || reign.Lost < juneOpening) ||
        pwaHistory[^1].Lost != new DateTime(2021, 12, 31))
        throw new Exception("A June 2002 championship history started before the company opened.");
}

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
    if (pwaHistory[0].Captured != juneOpening ||
        pwaHistory.Any(reign => reign.Captured < juneOpening) ||
        !pwaHistory.Any(reign => reign.Holder1 == 100 &&
            reign.Captured.Year == 2002 &&
            reign.Captured >= new DateTime(2002, 6, 15)) ||
        !pwaHistory.Any(reign => reign.Holder1 == 200 &&
            reign.Captured.Year == 2003))
        throw new Exception("Founding-year pinned wins ignored the start month or winning years.");
}

// Leaving the Starting Month control at its default must preserve the
// previous January 1 behavior, including in NPC-only histories.
var unchangedDefault = AnchoredHistoryPlanner.GenerateFromYears(
    noPinnedWinners, npcSeeds, firstYear: 2002, lastYear: 2002);
if (unchangedDefault[0].Captured != new DateTime(2002, 1, 1))
    throw new Exception("Default championship history no longer begins January 1.");

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

static void Check(int year, int month, int occurrence, DayOfWeek weekday, DateTime expected)
{
    var actual = AnnualTournamentCalendar.ResolveDate(year, month, occurrence, weekday);
    if (actual != expected)
        throw new Exception($"Expected {expected:yyyy-MM-dd}, got {actual:yyyy-MM-dd}.");
}
