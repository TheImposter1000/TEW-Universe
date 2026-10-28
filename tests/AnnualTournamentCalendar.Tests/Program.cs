using TEWUniverse.Desktop;
using TEWUniverse.Studio;

// Championship defense totals scale with days held and booking cadence,
// not fixed counts assigned to broad duration brackets.
if (TitleDefencePlanner.Resolve("USPW Television Championship",
        TitleDefenceFrequency.Automatic) != TitleDefenceFrequency.Television ||
    TitleDefencePlanner.Resolve("DNA TV Title",
        TitleDefenceFrequency.Automatic) != TitleDefenceFrequency.Television ||
    TitleDefencePlanner.Resolve("World Heavyweight Championship",
        TitleDefenceFrequency.Automatic) != TitleDefenceFrequency.Touring ||
    TitleDefencePlanner.Resolve("Women's Championship",
        TitleDefenceFrequency.Automatic) != TitleDefenceFrequency.Regular ||
    TitleDefencePlanner.Resolve("Television Championship",
        TitleDefenceFrequency.Occasional) != TitleDefenceFrequency.Occasional ||
    TitleDefencePlanner.Estimate(1, "TV Championship") != 0)
    throw new Exception("Championship defense cadence auto-detection failed.");

var tvSample = Enumerable.Range(1, 250)
    .Select(seed => TitleDefencePlanner.Estimate(438, "Television Championship",
        TitleDefenceFrequency.Automatic, new Random(seed))).ToArray();
var touringSample = Enumerable.Range(1, 250)
    .Select(seed => TitleDefencePlanner.Estimate(434, "WWE Championship",
        TitleDefenceFrequency.Touring, new Random(seed))).ToArray();
var regularSample = Enumerable.Range(1, 250)
    .Select(seed => TitleDefencePlanner.Estimate(438, "Regular Championship",
        TitleDefenceFrequency.Regular, new Random(seed))).ToArray();
var attractionSample = Enumerable.Range(1, 250)
    .Select(seed => TitleDefencePlanner.Estimate(438, "Special Championship",
        TitleDefenceFrequency.SpecialAttraction, new Random(seed))).ToArray();
if (tvSample.Any(x => x < 40 || x > 200) ||
    tvSample.Distinct().Count() < 15 ||
    touringSample.Average(x => x) <= tvSample.Average(x => x) ||
    tvSample.Average(x => x) <= regularSample.Average(x => x) ||
    regularSample.Average(x => x) <= attractionSample.Average(x => x) ||
    TitleDefencePlanner.Estimate(4, "Television Championship",
        TitleDefenceFrequency.Television, new Random(5)) > 3)
    throw new Exception("History defense totals are implausible, uniform, or not paced by championship type.");
Console.WriteLine("Passed title-specific defense cadence and 438-day television reign checks.");

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
    if (pwaHistory.Count == 0 || pwaHistory[0].Captured.Month != 6 ||
        pwaHistory[0].Captured.Year != 2002 ||
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
    if (pwaHistory[0].Captured.Month != 6 ||
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
if (unchangedDefault[0].Captured.Month != 1 || unchangedDefault[0].Captured.Year != 2002)
    throw new Exception("Default championship history no longer begins within January.");

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
    int tavitiaOverrun = (tavitia.Lost.AddDays(1) -
        tavitia.Captured.AddYears(2)).Days;
    if (tavitiaOverrun is < 0 or > 90 ||
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
    ChampionshipWinScheduler.RequiredNextCaptureDate(leap[0]) is not DateTime leapEnd ||
    leapEnd < leap[0].Date.AddYears(2) ||
    leapEnd > leap[0].Date.AddYears(2).AddDays(90))
    throw new Exception("Approximate two-calendar-year reign did not honor its date window.");

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

// A promotion that began in June can debut its inaugural champion any
// calendar day in June, rather than always using June 1.
var openingDays = new HashSet<int>();
for (int attempt = 0; attempt < 100; attempt++)
{
    var history = AnchoredHistoryPlanner.GenerateFromYears(
        noPinnedWinners, npcSeeds, firstYear: 2002, lastYear: 2003,
        startMonth: 6);
    var initial = history[0].Captured;
    if (initial.Year != 2002 || initial.Month != 6)
        throw new Exception("The first championship reign escaped its opening month.");
    openingDays.Add(initial.Day);
}
if (openingDays.Count < 8)
    throw new Exception("Inaugural date appears hardcoded rather than randomized.");

// Fourteen reigns within a shorter active range may legitimately contain
// repeated winning years, e.g. a hot-potato rivalry in 2016.
var fourteen = SuperstarReignInputPlanner.Expand(new[]
{
    new SuperstarReignInputPlanner.Request("Charlotte Flair", 100, 2013, 2020,
        14, "2016, 2016", "")
}, 2013, 2020, random: new Random(42));
if (fourteen.Count != 14 || fourteen[0].WinYear != 2016 ||
    fourteen[1].WinYear != 2016 || fourteen.Any(x =>
        x.WinYear is < 2013 or > 2020) ||
    fourteen.Zip(fourteen.Skip(1)).Any(p => p.First.WinYear > p.Second.WinYear))
    throw new Exception("Multiple same-year title wins were rejected or placed outside the active window.");

// The same actual worker can win, lose, and regain a title twice in one
// calendar year; the history must contain two DISTINCT reigns with an NPC
// or a different superstar between them.
for (int attempt = 0; attempt < 80; attempt++)
{
    var repeated = AnchoredHistoryPlanner.GenerateFromYears(new[]
    {
        new AnchoredHistoryPlanner.Anchor("Charlotte Flair", 100, 2016,
            2014, 2018, SourceOrder: 0),
        new AnchoredHistoryPlanner.Anchor("Charlotte Flair", 100, 2016,
            2014, 2018, SourceOrder: 1)
    }, npcSeeds, 2014, 2018);
    var matches = repeated.Select((v, index) => (v, index))
        .Where(x => x.v.Holder1 == 100 && x.v.Captured.Year == 2016).ToArray();
    if (matches.Length != 2 || matches[1].index <= matches[0].index + 1)
        throw new Exception("Same worker's repeated 2016 reigns were merged.");
}

// Two women exchanging the title repeatedly in the same year should
// alternate naturally instead of scheduling all one wrestler's wins first.
for (int i = 0; i < 60; i++)
{
    var feud = ChampionshipWinScheduler.Schedule(new[]
    {
        new ChampionshipWinScheduler.Win(10, "Charlotte Flair", 2016, SourceOrder: 0),
        new ChampionshipWinScheduler.Win(10, "Charlotte Flair", 2016, SourceOrder: 1),
        new ChampionshipWinScheduler.Win(20, "Sasha Banks", 2016, SourceOrder: 2),
        new ChampionshipWinScheduler.Win(20, "Sasha Banks", 2016, SourceOrder: 3)
    });
    if (feud.Count != 4 ||
        !feud.Select(x => x.WorkerId).SequenceEqual(new[] { 10, 20, 10, 20 }) ||
        feud.Any(x => x.Date.Year != 2016))
        throw new Exception("Same-year title-trading feud failed to alternate winners.");
}

// All durations are OPTIONAL independently. A six-time champion can have
// one historical 2011 title win lasting ~2y and five freely generated years
// and lengths. Explicit d values remain exact.
var partial = SuperstarReignInputPlanner.Expand(new[]
{
    new SuperstarReignInputPlanner.Request("Tavitia", 200, 2011, 2020,
        6, "2011", "2y")
}, 2005, 2024, random: new Random(21));
if (partial.Count != 6 || partial[0].WinYear != 2011 ||
    partial[0].ReignYears != 2 || partial.Any(x =>
        x.WinYear < 2011 || x.WinYear > 2020) ||
    partial.Skip(1).Any(x => x.ReignDays.HasValue ||
        x.ReignYears.HasValue || x.ReignMonths.HasValue))
    throw new Exception("Partial reign-length or automatic-year mapping failed.");

var positionals = SuperstarReignInputPlanner.Expand(new[]
{
    new SuperstarReignInputPlanner.Request("Tavitia", 200, 2008, 2018,
        6, "-, -, 2011", "-, -, 2y")
}, 2008, 2018, random: new Random(9));
if (positionals.Count != 6 || positionals[2].WinYear != 2011 ||
    positionals[2].ReignYears != 2 ||
    positionals.Where((x, index) => index != 2).Any(x =>
        x.ReignDays.HasValue || x.ReignYears.HasValue || x.ReignMonths.HasValue))
    throw new Exception("Later fixed winning year / reign length changed its position.");

var zaraSpec = SuperstarReignInputPlanner.Expand(new[]
{
    new SuperstarReignInputPlanner.Request("Zara Ellis", 501, 2017, 2023,
        1, "2019", "719d")
}, 2017, 2023);
if (zaraSpec.Single().ReignDays != 719)
    throw new Exception("The exact 719-day requirement was not carried through.");

// A six-month approximate reign preserves at least six calendar months and
// can extend up to thirty extra days, subject to available scheduling space.
bool sawMonthOverrun = false;
for (int i = 0; i < 70; i++)
{
    var monthHistory = AnchoredHistoryPlanner.GenerateFromYears(new[]
    {
        new AnchoredHistoryPlanner.Anchor("Half Year Champion", 300, 2017,
            2017, 2019, ReignMonths: 6)
    }, npcSeeds, 2016, 2019);
    var champion = monthHistory.Single(x => x.Holder1 == 300);
    var overrun = (champion.Lost.AddDays(1) -
        champion.Captured.AddMonths(6)).Days;
    if (overrun is < 0 or > 30)
        throw new Exception("Six-month reign exceeded its allowed approximate range.");
    if (overrun > 0) sawMonthOverrun = true;
}
if (!sawMonthOverrun)
    throw new Exception("Six-month reigns never vary beyond the exact anniversary.");

// Linked superstar annual Cup editions still cannot contain two winners
// in the same year.
bool annualRepeatRejected = false;
try
{
    AnchoredHistoryPlanner.GenerateFromYears(new[]
    {
        new AnchoredHistoryPlanner.Anchor("Cup Winner", 400, 2016, 2016, 2016),
        new AnchoredHistoryPlanner.Anchor("Cup Winner", 400, 2016, 2016, 2016)
    }, npcSeeds, 2016, 2016, true, 8, 4, DayOfWeek.Saturday);
}
catch (InvalidOperationException) { annualRepeatRejected = true; }
if (!annualRepeatRejected)
    throw new Exception("Annual Cup mode accepted two winners in one edition.");

Console.WriteLine("Passed repeat-year, partial-length, approximate-month and randomized-start checks.");

// All user-facing dates (worker fields, employment history, title previews,
// exports, prompts) use month/day/year. Do not silently accept ISO year-first
// dates as manually entered values.
if (StudioDateFormat.Format(new DateTime(2002, 6, 9)) != "06/09/2002" ||
    StudioDateFormat.Format(new DateTime(2020, 2, 29)) != "02/29/2020" ||
    !StudioDateFormat.TryParse("06/09/2002", out var parsed) ||
    parsed != new DateTime(2002, 6, 9))
    throw new Exception("MM/DD/YYYY display and input do not round-trip.");

if (StudioDateFormat.TryParse("2002-06-09", out _) ||
    StudioDateFormat.TryParse("09/06/2002", out var ambiguous) &&
        ambiguous != new DateTime(2002, 9, 6) ||
    StudioDateFormat.TryParse("6/9/2002", out _) ||
    StudioDateFormat.TryParse("02/29/2019", out _) ||
    StudioDateFormat.TryParse("13/01/2002", out _))
    throw new Exception("Year-first, invalid, or non-MM/DD/YYYY date input was accepted.");

var moreDates = new[] {
    new DateTime(1666, 1, 1), // TEW annual tournament no-reign sentinel
    new DateTime(1800, 12, 31),
    new DateTime(2002, 6, 1),
    new DateTime(2021, 12, 12),
    new DateTime(2024, 2, 29)
};
foreach (var date in moreDates)
    if (!StudioDateFormat.TryParse(StudioDateFormat.Format(date), out var again) ||
        again != date)
        throw new Exception("MM/DD/YYYY round-trip failed for historical or leap-year dates.");
Console.WriteLine("Passed MM/DD/YYYY date formatting, round-trip and strict input checks.");

// Completed title histories are not to be deleted just to link a real
// champion. Carson's FOUR National titles replace only four matching
// name-only placeholders, leaving every existing linked champion untouched.
var existingReigns = Enumerable.Range(0, 11).Select(i =>
    new ExistingTitleSlot(6000 + i, i + 1,
        new DateTime(2010 + i, 5, 1), new DateTime(2011 + i, 4, 30),
        (short)(i + 1), -1, $"Historical NPC {i}", -1, "", -1, "")).ToArray();
existingReigns[5] = existingReigns[5] with {
    Holder1 = 800, Name1 = "Already linked champion"
};
var carson = new TitleSlotRequest("Carson Cade",
    new[] { 4966 }, new[] { "Carson Cade" }, 2010, 2020, 4, "", "");
var carsonChanges = ExistingTitleSlotPlanner.Plan(
    existingReigns, new[] { carson }, TitleCompetitorFormat.Singles);
if (carsonChanges.Count != 4 ||
    carsonChanges.Any(p => p.Uid == existingReigns[5].Uid || p.Holder1 != 4966 ||
        p.Captured != existingReigns.Single(s => s.Uid == p.Uid).Captured ||
        p.Lost != existingReigns.Single(s => s.Uid == p.Uid).Lost ||
        p.Defences != existingReigns.Single(s => s.Uid == p.Uid).Defences))
    throw new Exception("Targeted Carson assignment overwrote a linked reign or changed history dates.");

// If a previous historical author already wrote "Carson Cade" as a
// name-only champion, link that existing name before replacing strangers.
var namedCarson = existingReigns.ToArray();
namedCarson[7] = namedCarson[7] with { Name1 = "Carson Cade" };
var namedMatch = ExistingTitleSlotPlanner.Plan(namedCarson,
    new[] { carson with { Reigns = 1 } }, TitleCompetitorFormat.Singles);
if (namedMatch.Count != 1 || namedMatch[0].Uid != namedCarson[7].Uid)
    throw new Exception("A pre-existing name-only Carson reign was not prioritized for linking.");

// Existing Carson wins count toward the requested total of four.
var alreadyHasTwo = existingReigns.ToArray();
alreadyHasTwo[1] = alreadyHasTwo[1] with { Holder1 = 4966, Name1 = "Carson Cade" };
alreadyHasTwo[3] = alreadyHasTwo[3] with { Holder1 = 4966, Name1 = "Carson Cade" };
var remaining = ExistingTitleSlotPlanner.Plan(
    alreadyHasTwo, new[] { carson }, TitleCompetitorFormat.Singles);
if (remaining.Count != 2 ||
    remaining.Any(x => x.Uid is 6001 or 6003 or 6005))
    throw new Exception("The generator duplicated Carson's already-linked reigns.");

// Known victory year must target an eligible name-only slot in that year,
// while random years fit anywhere inside the worker's company years.
var knownYear = ExistingTitleSlotPlanner.Plan(existingReigns,
    new[] { carson with { YearsWon = "2012" } }, TitleCompetitorFormat.Singles);
if (knownYear.Count != 4 ||
    !knownYear.Any(p => p.Captured.Year == 2012))
    throw new Exception("A fixed known winning year was not honored.");

bool missingYearRejected = false;
try
{
    ExistingTitleSlotPlanner.Plan(existingReigns,
        new[] { carson with { YearsWon = "2009" } },
        TitleCompetitorFormat.Singles);
}
catch (InvalidOperationException) { missingYearRejected = true; }
if (!missingYearRejected)
    throw new Exception("A nonexistent eligible capture year was silently changed.");

// Partial-year rules preserve chronology: when 2016 is the FIRST
// specified victory, later unpinned reigns cannot be assigned in 2010.
var laterCarson = ExistingTitleSlotPlanner.Plan(existingReigns,
    new[] { carson with { YearsWon = "2016" } },
    TitleCompetitorFormat.Singles);
if (laterCarson.Count != 4 ||
    laterCarson.Any(p => p.Captured.Year < 2016))
    throw new Exception("Unknown later victories were dated before Carson's known 2016 win.");

// Strict duration filtering protects pre-recorded history dates. An exact
// 719-day requirement cannot be fabricated by rewriting a 365-day NPC slot.
bool incompatibleLengthRejected = false;
try
{
    ExistingTitleSlotPlanner.Plan(existingReigns,
        new[] { carson with { ReignLengths = "719d" } },
        TitleCompetitorFormat.Singles);
}
catch (InvalidOperationException) { incompatibleLengthRejected = true; }
if (!incompatibleLengthRejected)
    throw new Exception("The existing slot matcher ignored exact days held.");

// Singles, Tag Team and Trios must target the corresponding number of real
// worker UID fields without silently replacing linked real workers.
var nameOnlyTeamSlots = existingReigns.Select(x =>
    x with { Holder1 = -1, Name1 = "Unlinked team" }).ToArray();
var teamRequest = new TitleSlotRequest("Pair",
    new[] { 10, 20 }, new[] { "Worker A", "Worker B" }, 2010, 2020,
    1, "2011", "");
var teamPlan = ExistingTitleSlotPlanner.Plan(nameOnlyTeamSlots,
    new[] { teamRequest }, TitleCompetitorFormat.TagTeam);
if (teamPlan.Count != 1 || teamPlan[0].Holder1 != 10 ||
    teamPlan[0].Holder2 != 20 || teamPlan[0].Holder3 != -1)
    throw new Exception("Tag-team history substitution did not write two linked members.");

var trio = new TitleSlotRequest("Trio",
    new[] { 10, 20, 30 }, new[] { "One", "Two", "Three" },
    2010, 2020, 1, "2011", "");
var trioPlan = ExistingTitleSlotPlanner.Plan(nameOnlyTeamSlots,
    new[] { trio }, TitleCompetitorFormat.Trios);
if (trioPlan.Count != 1 || trioPlan[0].Holder3 != 30)
    throw new Exception("Trio history substitution did not write three linked members.");

bool incompatibleExistingFormatRejected = false;
try
{
    ExistingTitleSlotPlanner.Plan(existingReigns,
        new[] { teamRequest }, TitleCompetitorFormat.TagTeam);
}
catch (InvalidOperationException) { incompatibleExistingFormatRejected = true; }
if (!incompatibleExistingFormatRejected)
    throw new Exception("A linked singles reign was overwritten during team substitution.");

bool invalidFormatRejected = false;
try
{
    ExistingTitleSlotPlanner.Plan(existingReigns,
        new[] { teamRequest }, TitleCompetitorFormat.Singles);
}
catch (InvalidOperationException) { invalidFormatRejected = true; }
if (!invalidFormatRejected)
    throw new Exception("A tag team was permitted to replace singles history.");

// Employment is tested for the entire inclusive reign. Contiguous records
// can cover a tenure; wrong-company, missing and sentinel CV periods are
// flagged rather than falsely treated as verified.
var employment = new[] {
    new EmploymentWindow(50, new DateTime(2014, 1, 1), new DateTime(2014, 12, 31)),
    new EmploymentWindow(50, new DateTime(2015, 1, 1), new DateTime(2015, 12, 31))
};
if (TitleEmploymentValidator.Check(50, new DateTime(2014, 9, 1),
        new DateTime(2015, 5, 1), employment) != EmploymentVerification.Covered ||
    TitleEmploymentValidator.Check(51, new DateTime(2014, 9, 1),
        new DateTime(2015, 5, 1), employment) != EmploymentVerification.Missing ||
    TitleEmploymentValidator.Check(null, new DateTime(2014, 9, 1),
        new DateTime(2015, 5, 1), employment) != EmploymentVerification.CompanyUnknown ||
    TitleEmploymentValidator.Check(50, new DateTime(2016, 1, 1),
        new DateTime(2016, 2, 1), new[] {
            new EmploymentWindow(50, new DateTime(2015, 1, 1), new DateTime(1666, 1, 1))
        }) != EmploymentVerification.Unverified ||
    TitleEmploymentValidator.Check(50, new DateTime(2014, 9, 1),
        new DateTime(1666, 1, 1), employment) != EmploymentVerification.Unverified)
    throw new Exception("Title employment verification produced an unsafe result.");

Console.WriteLine("Passed title-format, protected-placeholder and employment coverage checks.");

// Human-readable TEW IX record names must remain deterministic for a new
// championship lineage and for historical worker/company CV stints.
if (StudioRecordNames.TitleHistory("BCW Championship", 1) != "BCW Championship001" ||
    StudioRecordNames.TitleHistory("BCW Championship", 17) != "BCW Championship017" ||
    StudioRecordNames.TitleHistory("BCW Championship", 1000) != "BCW Championship1000" ||
    StudioRecordNames.Employment("pwa", new DateTime(2015, 6, 20),
        new DateTime(2021, 12, 12)) != "PWA (2015-2021)" ||
    StudioRecordNames.Employment(" NCW ", new DateTime(2008, 10, 25),
        new DateTime(2012, 8, 18)) != "NCW (2008-2012)" ||
    StudioRecordNames.WithCollisionSuffix("PWA (2015-2021)", 2)
        != "PWA (2015-2021) #2")
    throw new Exception("Championship or employment record-name format regressed.");

bool invalidRecordNameRejected = false;
try
{
    StudioRecordNames.Employment("PWA", new DateTime(2021, 1, 1),
        new DateTime(2015, 1, 1));
}
catch (InvalidOperationException) { invalidRecordNameRejected = true; }
if (!invalidRecordNameRejected)
    throw new Exception("An impossible employment name date range was accepted.");

Console.WriteLine("Passed TEW-style title and employment RecordName conventions.");

// Migration of ALREADY-SAVED history must not recreate title holders,
// change capture dates, or rewrite unrelated titles. The plan is entirely
// separate from destructive full lineage generation.
var oldHistory = new[]
{
    new TitleRecordNameRow(101, 10, 1, "BCW Championship", "TUS00010_00001"),
    new TitleRecordNameRow(102, 10, 2, "BCW Championship", "TUS00010_00002"),
    new TitleRecordNameRow(103, 10, 3, "BCW Championship", "BCW Championship003"),
    new TitleRecordNameRow(201, 20, 1, "UWE National Championship", "TUS00020_00001")
};
var oneTitle = RecordNameRepairPlanner.Titles(oldHistory, 10);
if (oneTitle.ScannedCount != 3 || oneTitle.Changes.Count != 2 ||
    oneTitle.Changes[0].NewName != "BCW Championship001" ||
    oneTitle.Changes[1].NewName != "BCW Championship002" ||
    oneTitle.Changes.Any(x => x.OwnerUid != 10 || x.Uid is not (101 or 102)))
    throw new Exception("Repair of one existing title did not preserve correct existing names.");
var everyTitle = RecordNameRepairPlanner.Titles(oldHistory, null);
if (everyTitle.Changes.Count != 3 ||
    everyTitle.Changes.Single(x => x.OwnerUid == 20).NewName !=
        "UWE National Championship001")
    throw new Exception("Database-wide existing title name migration was not scoped safely.");

// Two old records exchanging existing names is legal because database writes
// stage all changes with temporary identifiers inside a transaction.
var swapped = RecordNameRepairPlanner.Titles(new[]
{
    oldHistory[0] with { OldName = "BCW Championship002" },
    oldHistory[1] with { OldName = "BCW Championship001" }
}, 10);
if (swapped.Changes.Count != 2 ||
    swapped.Changes[0].NewName != "BCW Championship001" ||
    swapped.Changes[1].NewName != "BCW Championship002")
    throw new Exception("Swapped historical record names were not planned correctly.");

// Identical belt names for different UID ownership must not collide, even in
// a selected-only migration where another title's name is reserved.
var titleConflict = RecordNameRepairPlanner.Titles(new[]
{
    new TitleRecordNameRow(1, 5, 1, "BCW Championship", "BCW Championship001"),
    new TitleRecordNameRow(2, 6, 1, "BCW Championship", "TUS00006_00001")
}, 6);
if (titleConflict.Changes.Count != 1 ||
    titleConflict.Changes[0].NewName != "BCW Championship002")
    throw new Exception("Existing title record name collision was not prevented.");

var cv = new[]
{
    new EmploymentRecordNameRow(4963, 11, "PWA",
        new DateTime(2015, 6, 20), new DateTime(2021, 12, 12), "TUSCV0001"),
    new EmploymentRecordNameRow(4963, 22, "NCW",
        new DateTime(2008, 10, 25), new DateTime(2012, 8, 18), "TUSCV0002"),
    new EmploymentRecordNameRow(77, 11, "PWA",
        new DateTime(2015, 1, 1), new DateTime(2021, 12, 12), "PWA (2015-2021)"),
    new EmploymentRecordNameRow(4963, 11, "PWA",
        new DateTime(2015, 4, 1), new DateTime(2021, 11, 1), "TUSCV0003")
};
var brendanCv = RecordNameRepairPlanner.Employment(cv, 4963);
if (brendanCv.ScannedCount != 3 || brendanCv.Changes.Count != 3 ||
    brendanCv.Changes.Any(x => x.OwnerUid != 4963) ||
    !brendanCv.Changes.Any(x => x.NewName == "NCW (2008-2012)") ||
    !brendanCv.Changes.Any(x => x.NewName == "PWA (2015-2021) #2") ||
    !brendanCv.Changes.Any(x => x.NewName == "PWA (2015-2021) #3"))
    throw new Exception("Existing Brendan employment names were not migrated or collision-safe.");
var wholeCv = RecordNameRepairPlanner.Employment(cv, null);
if (wholeCv.ScannedCount != 4 || wholeCv.Changes.Count != 3 ||
    wholeCv.Changes.Select(x => x.NewName)
        .Distinct(StringComparer.OrdinalIgnoreCase).Count() != wholeCv.Changes.Count ||
    wholeCv.Changes.Any(x => x.OwnerUid == 77))
    throw new Exception("Database-wide employment record names were not made unique.");

// Special TEW 1666 sentinel / damaged historical dates cannot be guessed.
// Such rows should be shown as skipped rather than silently moved.
var unknown = RecordNameRepairPlanner.Employment(new[]
{
    new EmploymentRecordNameRow(90, 11, "PWA",
        new DateTime(1666, 1, 1), new DateTime(2021, 1, 1), "UnknownCV")
}, 90);
if (unknown.Changes.Count != 0 || unknown.Warnings.Count != 1)
    throw new Exception("An incomplete employment period was renamed by guessing its years.");

bool duplicateKeysRejected = false;
try
{
    RecordNameRepairPlanner.Titles(new[]
    {
        oldHistory[0], oldHistory[1] with { OldName = oldHistory[0].OldName }
    }, null);
}
catch (InvalidOperationException) { duplicateKeysRejected = true; }
if (!duplicateKeysRejected)
    throw new Exception("Ambiguous duplicate old title RecordName values were accepted.");

Console.WriteLine("Passed selected and global existing-record name migration checks.");

// Roster filters combine through AND without writing to the MDB. A single
// worker can have multiple current tblContract companies and more than one
// verified role; search, sorting and filtering must remain independent.
var rosterAsOf = new DateTime(2026, 10, 18);
var roster = new[]
{
    new WorkerFilterRecord(1, "Brendan Walsh", true,
        new DateTime(1987, 10, 22), "Code 1", "Code 3", "Code 22",
        new[] { "Wrestler", "Manager" }, new[] { 100, 200 }, true),
    new WorkerFilterRecord(2, "Brenda Walsh", true,
        new DateTime(1995, 1, 8), "Code 2", "Code 3", "Code 22",
        new[] { "Wrestler" }, new[] { 200 }, false),
    new WorkerFilterRecord(3, "Retired Veteran", false,
        new DateTime(1970, 2, 14), "Code 1", "Code 4", "Code 24",
        new[] { "Personality" }, Array.Empty<int>(), false),
    new WorkerFilterRecord(4, "No Birth Date", null, null,
        "", "", "", Array.Empty<string>(), Array.Empty<int>(), true)
};
int[] MatchIds(WorkerFilterCriteria criteria) => roster
    .Where(w => WorkerRosterFilterEngine.Matches(w, criteria, rosterAsOf))
    .Select(w => w.Id).ToArray();
var combinedFilters = new WorkerFilterCriteria(
    Search: "Brendan", Active: true, Gender: "Code 1", MinAge: 38,
    MaxAge: 39, CompanyUid: 100, Role: "Wrestler",
    Nationality: "Code 3", Region: "Code 22", HasPicture: true);
if (!MatchIds(combinedFilters).SequenceEqual(new[] { 1 }))
    throw new Exception("All eight roster filters did not combine correctly.");
if (!MatchIds(new WorkerFilterCriteria(CompanyUid: 200))
        .SequenceEqual(new[] { 1, 2 }) ||
    !MatchIds(new WorkerFilterCriteria(UnemployedOnly: true))
        .SequenceEqual(new[] { 3, 4 }) ||
    !MatchIds(new WorkerFilterCriteria(Role: "Manager"))
        .SequenceEqual(new[] { 1 }) ||
    !MatchIds(new WorkerFilterCriteria(HasPicture: false))
        .SequenceEqual(new[] { 2, 3 }) ||
    !MatchIds(new WorkerFilterCriteria(Active: false))
        .SequenceEqual(new[] { 3 }))
    throw new Exception("Company, role, pictures or active-status roster filtering failed.");
// A worker with a contract whose federation link is blank is NOT unemployed.
var unknownFedContract = new WorkerFilterRecord(5, "Unknown company contract",
    true, null, "", "", "", Array.Empty<string>(), Array.Empty<int>(),
    HasCurrentContract: true);
if (WorkerRosterFilterEngine.Matches(unknownFedContract,
        new WorkerFilterCriteria(UnemployedOnly: true), rosterAsOf))
    throw new Exception("Worker with a companyless active contract was misclassified as unemployed.");

if (WorkerRosterFilterEngine.AgeAt(new DateTime(1987, 10, 22), rosterAsOf) != 38 ||
    WorkerRosterFilterEngine.AgeAt(new DateTime(1987, 10, 22),
        new DateTime(2026, 10, 22)) != 39 ||
    MatchIds(new WorkerFilterCriteria(MinAge: 0)).Contains(4) ||
    MatchIds(new WorkerFilterCriteria(Region: "Code 24")).Length != 1 ||
    !MatchIds(new WorkerFilterCriteria(Search: "3")).SequenceEqual(new[] { 3 }))
    throw new Exception("Age boundary, unknown birthday, region or ID search filter failed.");
Console.WriteLine("Passed combined worker roster status, gender, age, company, role, region, nationality and picture filters.");

var genderLabels = new[]
{
    "Male", "Transgender Male", "Non-binary AMAB", "Other Male",
    "Female", "Transgender Female", "Non-binary AFAB", "Other Female"
};
var teamTypeLabels = new[]
{
    "Permanent Unit", "Unit", "Individuals", "Inactive Permanent Unit",
    "Inactive Unit", "Inactive Individuals"
};
for (int i = 0; i < genderLabels.Length; i++)
{
    string stored = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    if (TewCodeLabels.Display("tblWorker", "Gender", stored) != genderLabels[i] ||
        TewCodeLabels.Choices("tblWorker", "Gender")[i].Code != i + 1)
        throw new Exception($"Gender code {stored} did not map to {genderLabels[i]}.");
}
for (int i = 0; i < teamTypeLabels.Length; i++)
{
    string stored = (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
    if (TewCodeLabels.Display("tblTeam", "Type", stored) != teamTypeLabels[i] ||
        TewCodeLabels.Choices("tblTeam", "Type")[i].Code != i + 1)
        throw new Exception($"Tag team code {stored} did not map to {teamTypeLabels[i]}.");
}
bool parsedCode = TewCodeLabels.TryGetCode("Code 5", out int genderCode);
if (TewCodeLabels.Display("tblWorker", "Gender", "Code 5") != "Female" ||
    TewCodeLabels.Display("tblWorker", "Gender", "999") != "Code 999" ||
    TewCodeLabels.Display("tblWorker", "Gender", "") != "Not recorded" ||
    TewCodeLabels.Display("tblTeam", "Gender", "5") != "Code 5" ||
    TewCodeLabels.Display("tblTeam", "Type", "7") != "Code 7" ||
    !parsedCode || genderCode != 5)
    throw new Exception("Verified-code presentation, raw code parsing or unmapped fallbacks failed.");
Console.WriteLine("Passed all 14 verified gender/team-type label mappings and numeric code fallbacks.");

CheckMappedCategory("tblWorker", "Gender", 8);
CheckMappedCategory("tblTeam", "Type", 6);
CheckMappedCategory("tblWorker", "Sexuality", 5);
CheckMappedCategory("tblWorker", "Pronouns", 5);
CheckMappedCategory("tblWorker", "Race", 9);
CheckMappedCategory("tblWorker", "Based_In", 65);
CheckMappedCategory("tblWorker", "Nationality", 200);
CheckMappedCategory("tblWorker", "Style", 16);
CheckMappedCategory("tblWorker", "CompetesAgainst", 5);
CheckMappedCategory("tblWorker", "Outsiderel", 4);
CheckMappedCategory("tblRelation", "P_Rel", 9, 0);
CheckMappedCategory("tblRelation", "R_Rel", 7, 0);
CheckMappedCategory("tblRelation", "F_Rel", 7, 0);

CheckCode("tblWorker", "Sexuality", 1, "Heterosexual");
CheckCode("tblWorker", "Sexuality", 5, "Pansexual");
CheckCode("tblWorker", "Pronouns", 2, "She-Her");
CheckCode("tblWorker", "Pronouns", 5, "She-Them");
CheckCode("tblWorker", "Race", 7, "South Asian");
CheckCode("tblWorker", "Race", 9, "Other");
CheckCode("tblWorker", "Based_In", 1, "USA");
CheckCode("tblWorker", "Based_In", 22, "Manitoba");
CheckCode("tblWorker", "Based_In", 26, "The Maritimes");
CheckCode("tblWorker", "Based_In", 43, "Northern Europe");
CheckCode("tblWorker", "Based_In", 47, "Chubu");
CheckCode("tblWorker", "Based_In", 65, "Western Australia");
CheckCode("tblWorker", "Nationality", 1, "Afghan");
CheckCode("tblWorker", "Nationality", 4, "American");
CheckCode("tblWorker", "Nationality", 36, "Canadian");
CheckCode("tblWorker", "Nationality", 94, "Japanese");
CheckCode("tblWorker", "Nationality", 119, "Mexican");
CheckCode("tblWorker", "Nationality", 146, "Puerto Rican");
CheckCode("tblWorker", "Nationality", 197, "Welsh");
CheckCode("tblWorker", "Nationality", 200, "Zimbabwean");
CheckCode("tblWorker", "Style", 1, "Regular");
CheckCode("tblWorker", "Style", 13, "Technician Flyer");
CheckCode("tblWorker", "Style", 16, "MMA Crossover");
CheckCode("tblWorker", "CompetesAgainst", 1, "Any");
CheckCode("tblWorker", "CompetesAgainst", 2, "Men's Division Only");
CheckCode("tblWorker", "CompetesAgainst", 3, "Women's Division Only");
CheckCode("tblWorker", "CompetesAgainst", 4, "Primarily Men's Division");
CheckCode("tblWorker", "CompetesAgainst", 5, "Primary Women's Division");
CheckCode("tblWorker", "Outsiderel", 1, "No");
CheckCode("tblWorker", "Outsiderel", 2, "Dating");
CheckCode("tblWorker", "Outsiderel", 3, "Engaged");
CheckCode("tblWorker", "Outsiderel", 4, "Married");
var verifiedRelationshipValues = new[] {
    (Field: "P_Rel", Labels: new[] {
        "None", "Mutual Respect", "Friendship", "Deep Friendship", "Like Family",
        "Strong Dislike", "Open Hostility", "Hatred", "Mutual Loathing"
    }),
    (Field: "R_Rel", Labels: new[] {
        "None", "Dating", "Engaged", "Married", "Broken Up",
        "Permanent Split", "Divorced"
    }),
    (Field: "F_Rel", Labels: new[] {
        "None", "Sibling", "Parent", "Cousin", "Grandparent",
        "Aunt / Uncle", "Distant Relative"
    })
};
foreach (var category in verifiedRelationshipValues)
{
    for (int code = 0; code < category.Labels.Length; code++)
        CheckCode("tblRelation", category.Field, code, category.Labels[code]);
    if (TewCodeLabels.Display("tblRelation", category.Field, "999") != "Code 999" ||
        TewCodeLabels.Display("tblWorker", category.Field, "0") != "Code 0" ||
        TewCodeLabels.Choices("tblRelation", category.Field).First().Code != 0)
        throw new Exception("Zero-based relation label fallback or category isolation failed.");
}
// Exhaustive, sparse numeric TEW IX worker Attribute codes. These four
// groups occupy ONE MDB field: tblAttribute.Attribute. Preserve the five
// explicit "Unassigned" personality slots without letting them be assigned.
var attributeGroups = new[] {
    (Start: 1, Category: "Personality", Labels: new[] {
        "Professional", "Quiet", "Stalwart", "Relaxed", "Lively", "Unassigned",
        "Party Animal", "People Person", "Irrepressible", "Free Spirit",
        "Driven", "Intense", "Unassigned", "Loner", "Prickly", "Selfish",
        "Unassigned", "Insecure", "Needy", "Unassigned", "Troublemaker",
        "Agitator", "Unassigned", "Unpredictable", "Grifter", "Bully",
        "Wild", "Scumbag"
    }),
    (Start: 310, Category: "Alignment", Labels: new[] {
        "100% Babyface", "Better As Babyface", "Better As Heel", "100% Heel",
        "Amazing Babyface", "Amazing Heel"
    }),
    (Start: 320, Category: "Gimmick", Labels: new[] {
        "Plays Dominant Well", "Plays Comedy Well", "Plays Swagger Well",
        "Plays Gimmicky / Cartoonish Well", "Plays Bad Ass Well",
        "Plays Legitimate Well", "Plays Weasely / Underdog Well",
        "Plays Realistic Well", "Plays Mysterious / Occult Well",
        "Plays Offbeat / Unstable Well", "Can't Play Dominant",
        "Can't Play Comedy", "Can't Play Swagger",
        "Can't Play Gimmicky / Cartoonish", "Can't Play Bad Ass",
        "Can't Play Legitimate", "Can't Play Weasely / Underdog",
        "Can't Play Realistic", "Can't Play Mysterious / Occult",
        "Can't Play Offbeat / Unstable"
    }),
    (Start: 374, Category: "Other Performance", Labels: new[] {
        "No Sense of Humor", "Big Match Specialist", "Mr. Finale"
    })
};
int attributeCount = 0;
foreach (var group in attributeGroups)
{
    for (int i = 0; i < group.Labels.Length; i++)
    {
        int code = group.Start + i;
        CheckCode("tblAttribute", "Attribute", code, group.Labels[i]);
        string raw = code.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string display = WorkerAttributeValue.Label(raw, typeof(int));
        if (!display.StartsWith(group.Category + ": " + group.Labels[i] + " (Code " + code,
            StringComparison.Ordinal) ||
            WorkerAttributeValue.IsAssignable(raw) == (group.Labels[i] == "Unassigned"))
            throw new Exception("Wrong attribute category/assigned-state for code " + code);
        attributeCount++;
    }
}
if (attributeCount != 57 ||
    TewCodeLabels.Choices("tblAttribute", "Attribute").Count != 57 ||
    TewCodeLabels.Choices("tblWorker", "Attribute").Count != 0 ||
    TewCodeLabels.Display("tblAttribute", "Attribute", "309") != "Code 309" ||
    TewCodeLabels.Display("tblAttribute", "Attribute", "340") != "Code 340")
    throw new Exception("Expected 57 sparse attribute codes and unmodified fallback.");
var reserveCodes = new[] { 6, 13, 17, 20, 23 };
foreach (int code in reserveCodes)
{
    string raw = code.ToString(System.Globalization.CultureInfo.InvariantCulture);
    if (!WorkerAttributeValue.IsReserved(raw) ||
        WorkerAttributeValue.MaySaveCode(raw, null) ||
        !WorkerAttributeValue.MaySaveCode(raw, raw))
        throw new Exception("Reserved Unassigned code improperly created/changed: " + code);
}
if (!WorkerAttributeValue.MaySaveCode("310", null) ||
    !WorkerAttributeValue.MaySaveCode("376", null) ||
    WorkerAttributeValue.MaySaveCode("999", null) ||
    !WorkerAttributeValue.MaySaveCode("999", "999") ||
    WorkerAttributeValue.MaySaveCode("999", "310"))
    throw new Exception("Attribute assignment/preservation guard failed.");

var attrOptions = WorkerAttributeValue.Options(new[] { "1", "310", "999" });
if (attrOptions.Count != 58 ||
    !attrOptions.Any(o => o.StoredCode == "310" && o.Assignable &&
        o.DisplayText.Contains("100% Babyface")) ||
    !attrOptions.Any(o => o.StoredCode == "6" && !o.Assignable && o.Verified) ||
    !attrOptions.Any(o => o.StoredCode == "999" && !o.Assignable && !o.Verified))
    throw new Exception("Verified attribute picker must include unseen named codes " +
        "and preserve existing unverified codes without assigning them.");
Console.WriteLine("Passed 57 exact attribute mappings, 5 reserved codes, categories and picker protections.");

if (TewCodeLabels.Choices("tblRelation", "Mentor").Count != 0)
    throw new Exception("Boolean Mentor should never be a numeric translation.");
var knownRelationWorkers = new HashSet<int> { 11, 22, 33 };
RelationshipRules.ValidatePair(11, 22, knownRelationWorkers);
if (RelationshipRules.Pair(11, 22) != RelationshipRules.Pair(22, 11))
    throw new Exception("Relationship duplicate-pair checking should ignore worker order.");
RelationshipRules.ValidateCodes(1, 2, 3, true);
RelationshipRules.ValidateCodes(0, 0, 0, true);
bool rejectedEmpty = false;
try { RelationshipRules.ValidateCodes(0, 0, 0, false); }
catch (InvalidOperationException) { rejectedEmpty = true; }
if (!rejectedEmpty) throw new Exception("Blank relationship records were accepted.");
bool rejectedCode = false;
try { RelationshipRules.ValidateCodes(9, 0, 0, false); }
catch (InvalidOperationException) { rejectedCode = true; }
if (!rejectedCode) throw new Exception("Unverified Personal code was accepted.");
bool rejectedSameWorker = false;
try { RelationshipRules.ValidatePair(11, 11, knownRelationWorkers); }
catch (InvalidOperationException) { rejectedSameWorker = true; }
if (!rejectedSameWorker) throw new Exception("Self relationship pair was accepted.");
bool rejectedMissingWorker = false;
try { RelationshipRules.ValidatePair(11, 99, knownRelationWorkers); }
catch (InvalidOperationException) { rejectedMissingWorker = true; }
if (!rejectedMissingWorker) throw new Exception("Missing relation worker was accepted.");
Console.WriteLine("Passed all 23 verified zero-based relationship codes, coexistence and worker-pair checks.");

if (TewCodeLabels.Choices("tblWorker", "Type").Count != 0 ||
    TewCodeLabels.Choices("tblTeam", "Gender").Count != 0 ||
    !TewCodeLabels.IsMappedField("tblWorker", "Pronouns") ||
    !TewCodeLabels.IsMappedField("tblWorker", "Based_In") ||
    TewCodeLabels.Display("tblWorker", "Nationality", "999") != "Code 999" ||
    TewCodeLabels.Display("tblWorker", "Pronouns", "0") != "Code 0" ||
    TewCodeLabels.Display("tblWorker", "CompetesAgainst", "6") != "Code 6" ||
    TewCodeLabels.Display("tblWorker", "Outsiderel", "0") != "Code 0" ||
    TewCodeLabels.Display("tblTeam", "Outsiderel", "4") != "Code 4")
    throw new Exception("Category isolation or fallback for unknown codes failed.");
Console.WriteLine("Passed all 403 mapped codes across fourteen fields, boundaries, and fallback checks.");

static void CheckMappedCategory(string table, string column, int expectedCount,
    int firstCode = 1)
{
    var labels = TewCodeLabels.Choices(table, column);
    if (labels.Count != expectedCount)
        throw new Exception($"{table}.{column} expected {expectedCount} values, got {labels.Count}.");
    for (int i = 0; i < labels.Count; i++)
    {
        var value = labels[i];
        string code = (i + firstCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (value.Code != i + firstCode || string.IsNullOrWhiteSpace(value.Label) ||
            TewCodeLabels.Display(table, column, code) != value.Label ||
            TewCodeLabels.Display(table, column, "Code " + code) != value.Label)
            throw new Exception($"{table}.{column} failed code round-trip at {code}.");
    }
}

static void CheckCode(string table, string column, int code, string expected)
{
    string stored = code.ToString(System.Globalization.CultureInfo.InvariantCulture);
    if (TewCodeLabels.Display(table, column, stored) != expected)
        throw new Exception($"{table}.{column} code {code}: expected '{expected}'.");
}



// tblAttribute.Attribute may be numeric, not a string. Round-trip the REAL
// numeric storage values, without pretending the code has a verified label.
foreach (var (type, raw, expected) in new (Type Type, string Raw, object Expected)[] {
    (typeof(byte), "7", (byte)7),
    (typeof(short), "255", (short)255),
    (typeof(int), "1042", 1042),
    (typeof(long), "65000", 65000L),
    (typeof(string), "Excellent Personality", "Excellent Personality")
})
{
    if (!Equals(WorkerAttributeValue.Parse(raw, type), expected))
        throw new Exception("Attribute value round-trip failed for " + type.Name);
}
if (WorkerAttributeValue.Label("7", typeof(byte)) != "Personality: Party Animal (Code 7)" ||
    WorkerAttributeValue.Label("Excellent Personality", typeof(string)) !=
        "Excellent Personality")
    throw new Exception("Raw attribute code should never be presented as a verified name.");
if (!Equals(WorkerAttributeValue.BooleanFlag(true, typeof(bool)), true) ||
    !Equals(WorkerAttributeValue.BooleanFlag(true, typeof(byte)), (byte)1) ||
    !Equals(WorkerAttributeValue.BooleanFlag(false, typeof(short)), (short)0))
    throw new Exception("Attribute Hidden/Permanent flag type coercion failed.");
bool rejectedNumericText = false;
try { WorkerAttributeValue.Parse("Mutual Respect", typeof(byte)); }
catch (InvalidOperationException) { rejectedNumericText = true; }
if (!rejectedNumericText)
    throw new Exception("An invented string label was accepted as a numeric attribute.");
bool rejectedOutsideByte = false;
try { WorkerAttributeValue.Parse("300", typeof(byte)); }
catch (InvalidOperationException) { rejectedOutsideByte = true; }
if (!rejectedOutsideByte)
    throw new Exception("Attribute code overflow was not rejected.");
bool rejectedNegativeNumeric = false;
try { WorkerAttributeValue.Parse("-1", typeof(short)); }
catch (InvalidOperationException) { rejectedNegativeNumeric = true; }
if (!rejectedNegativeNumeric)
    throw new Exception("Negative attribute codes were accepted.");
Console.WriteLine("Passed numeric tblAttribute value/flag parsing and unsupported code protections.");

// Legacy Studio-only accomplishments remain readable on disk for existing
// customers, but the new Accomplishments tab displays actual linked MDB
// championship and award histories automatically and has no edit controls.
// Accomplishments live OUTSIDE the Access MDB in a JSON file keyed by
// scenario path and worker UID. Never alter or create the scenario itself.
string tempAccomplishments = Path.Combine(Path.GetTempPath(),
    "TEW-Accomplishment-Test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempAccomplishments);
try
{
    var store = new WorkerAccomplishmentStore(tempAccomplishments);
    string dbOne = Path.Combine(tempAccomplishments, "Scenario A.mdb");
    string dbTwo = Path.Combine(tempAccomplishments, "Scenario B.mdb");
    File.WriteAllBytes(dbOne, new byte[] { 11, 22, 33, 44 });
    byte[] originalBytes = File.ReadAllBytes(dbOne);
    var first = store.Add(dbOne, 101, "Championship", "BCW Championship",
        "2022", "Two reigns with Carson");
    store.Add(dbOne, 202, "Award", "Wrestler of the Year", "2021", "Different worker");
    store.Add(dbTwo, 101, "Tournament", "World Cup", "2020", "Different scenario");
    if (store.List(dbOne, 101).Count != 1 ||
        store.List(dbOne, 202).Count != 1 ||
        store.List(dbTwo, 101).Count != 1 ||
        store.FilePath(dbOne) == store.FilePath(dbTwo) ||
        !File.ReadAllBytes(dbOne).SequenceEqual(originalBytes))
        throw new Exception("Studio accomplishments escaped their worker/scenario storage boundary.");
    var changed = store.Update(dbOne, 101, first.Id,
        "Hall of Fame", "BCW Hall of Fame", "2024", "Inducted");
    if (changed.Title != "BCW Hall of Fame" ||
        store.List(dbOne, 101).Single().Category != "Hall of Fame")
        throw new Exception("Studio accomplishment update did not round-trip.");
    bool rejected = false;
    try { store.Update(dbOne, 202, first.Id, "Award", "Wrong owner", "", ""); }
    catch (InvalidOperationException) { rejected = true; }
    if (!rejected) throw new Exception("Cross-worker accomplishment edit was allowed.");
    rejected = false;
    try { store.Add(dbOne, 101, "Championship", "No date", "1700", ""); }
    catch (InvalidOperationException) { rejected = true; }
    if (!rejected) throw new Exception("Invalid accomplishment year was accepted.");
    store.Remove(dbOne, 101, first.Id);
    if (store.List(dbOne, 101).Count != 0 ||
        store.List(dbOne, 202).Count != 1 ||
        store.List(dbTwo, 101).Count != 1 ||
        !File.ReadAllBytes(dbOne).SequenceEqual(originalBytes))
        throw new Exception("Accomplishment delete changed another worker or the MDB.");
}
finally { Directory.Delete(tempAccomplishments, recursive: true); }
Console.WriteLine("Passed local accomplishments CRUD, scenario/worker isolation, and untouched-MDB checks.");

// Cross-record Data Check: structured references are certain, but natural
// language is a manual-review hint only (never an automatic "correction").
var knownWorkerIds = new HashSet<int> { 101, 202 };
if (DataCheckRules.MissingLinkedId(101, knownWorkerIds) ||
    !DataCheckRules.MissingLinkedId(999, knownWorkerIds) ||
    DataCheckRules.MissingLinkedId(-1, knownWorkerIds) ||
    !DataCheckRules.IsNameOnlyHolder(-1) ||
    DataCheckRules.IsNameOnlyHolder(0) ||
    DataCheckRules.ClaimedReigns("3-time BCW Championship champion",
        "BCW Championship") != 3 ||
    DataCheckRules.ClaimedReigns("BCW Championship winner", "BCW Championship") != null ||
    DataCheckRules.ClaimedReigns("2-time Other Title champion", "BCW Championship") != null ||
    !DataCheckRules.MentionsTeam("Former member of The Apex Alliance.",
        "The Apex Alliance") ||
    DataCheckRules.MentionsTeam("The Apex Alliance", "Team") ||
    DataCheckRules.MentionsTeam("Embracing both the family mask and legacy",
        "The Family") ||
    DataCheckRules.MentionsTeam("DNA's attention, leading to his signing " +
        "as part of the Elite Division", "The Elite") ||
    DataCheckRules.MentionsTeam("the family mask", "The Family") ||
    DataCheckRules.MentionsTeam("the Elite Division", "The Elite") ||
    !DataCheckRules.MentionsTeam("He was a member of The Elite.",
        "The Elite") ||
    !DataCheckRules.MentionsTeam("Joined tag team The Family.",
        "The Family") ||
    DataCheckRules.Csv("A, \"B\"\nC") != "\"A, \"\"B\"\" C\"")
    throw new Exception("Cross-record validation, bio claim handling, or CSV escaping failed.");
// Avoid false-positive championship and team history findings.
if (DataCheckRules.ClaimedReigns(
        "A 2-time Other Championship holder who later won the BCW Championship.",
        "BCW Championship") != null ||
    DataCheckRules.ClaimedReigns(
        "Won the BCW Championship (3 times).",
        "BCW Championship") != 3 ||
    DataCheckRules.ClaimedReigns(
        "3-time BCW Championship winner; 4-time BCW Championship winner.",
        "BCW Championship") != null ||
    DataCheckRules.InvalidNegativeId(-1) ||
    !DataCheckRules.InvalidNegativeId(-2) ||
    DataCheckRules.DuplicateTagWinners(17, 22) ||
    DataCheckRules.DuplicateTagWinners(0, 0) ||
    !DataCheckRules.DuplicateTagWinners(17, 17) ||
    !DataCheckRules.ValidAwardYear(2022) ||
    DataCheckRules.ValidAwardYear(0) ||
    DataCheckRules.ValidAwardYear(2200))
    throw new Exception("Data Check produced false positive or missed an award integrity issue.");
Console.WriteLine("Passed read-only cross-record link checks and cautious biography parsing.");

// Awards live in two distinct Access tables; never accept a guessed
// numeric winner column, table, or record creation operation.
if (!AwardsRules.IsAwardTable("tblEOYAward") ||
    !AwardsRules.IsAwardTable("tblEOYFedAward") ||
    AwardsRules.IsAwardTable("tblStoryline") ||
    !AwardsRules.CanEdit("tblEOYAward", "Year", typeof(int)) ||
    !AwardsRules.CanEdit("tblEOYFedAward", "Year", typeof(short)) ||
    !AwardsRules.CanEdit("tblEOYAward", "Worker", typeof(int)) ||
    !AwardsRules.CanEdit("tblEOYAward", "Tag2", typeof(int)) ||
    !AwardsRules.CanEdit("tblEOYAward", "Fed", typeof(int)) ||
    !AwardsRules.CanEdit("tblEOYFedAward", "Fed", typeof(int)) ||
    !AwardsRules.CanEdit("tblEOYFedAward", "Worker", typeof(int)) ||
    !AwardsRules.CanEdit("tblEOYFedAward", "WinnerUID", typeof(int)) ||
    AwardsRules.CanEdit("tblEOYFedAward", "Award", typeof(int)) ||
    AwardsRules.CanEdit("tblEOYAward", "Match_At", typeof(int)) ||
    AwardsRules.CanEdit("tblEOYAward", "Match_Rating", typeof(int)) ||
    AwardsRules.CanEdit("tblEOYAward", "UID", typeof(int)) ||
    AwardsRules.CanEdit("tblEOYAward", "Worker", typeof(string)))
    throw new Exception("Awards editor exposed undocumented identifiers or unsafe fields.");
if (AwardsRules.ValidateYear("1997") != 1997 ||
    AwardsRules.ValidateYear("2100") != 2100 ||
    AwardsRules.ValidateLinkedId("123", "Worker") != 123)
    throw new Exception("Awards verified year and worker input rejected.");
foreach (string invalidYear in new[] { "1799", "2101", "-1", "not-a-year", "" })
{
    bool rejected = false;
    try { AwardsRules.ValidateYear(invalidYear); }
    catch (InvalidOperationException) { rejected = true; }
    if (!rejected) throw new Exception("Awards year must fail closed: " + invalidYear);
}
foreach (string invalidUid in new[] { "0", "-1", "not-a-worker", "" })
{
    bool rejected = false;
    try { AwardsRules.ValidateLinkedId(invalidUid, "Worker"); }
    catch (InvalidOperationException) { rejected = true; }
    if (!rejected) throw new Exception("Unverified award winner UID was accepted: " + invalidUid);
}
// Actual TEW IX award tables use MaleOverall, FemaleTag2 and other
// category-specific winner slots. They must be editable as real worker IDs
// without exposing match/show metadata as worker IDs.
foreach (var table in new[] { AwardsRules.GlobalTable, AwardsRules.CompanyTable })
{
    foreach (var workerColumn in new[] {
        "MaleOverall", "FemaleOverall", "MaleYoung", "FemaleYoung",
        "MaleVeteran", "FemaleVeteran", "MaleTag1", "MaleTag2",
        "FemaleTag1", "FemaleTag2", "MaleManager", "FemaleReferee"
    })
        if (!AwardsRules.IsWorkerField(table, workerColumn, typeof(int)) ||
            !AwardsRules.CanEdit(table, workerColumn, typeof(int)))
            throw new Exception("Missing TEW IX award recipient: " + table + "." + workerColumn);
    foreach (var nonWorker in new[] { "Match", "Match_At", "Match_Rating",
                                      "Card", "Card_Rating", "UID" })
        if (AwardsRules.IsWorkerField(table, nonWorker, typeof(int)))
            throw new Exception("Match, card or UID mistaken for award recipient.");
}
if (AwardsRules.CareerName("MaleTag1") != AwardsRules.CareerName("MaleTag2") ||
    AwardsRules.FriendlyName("MaleOverall") != "Men's Wrestler of the Year" ||
    AwardsRules.ValidateWinnerId("0", "MaleOverall") != 0 ||
    !AwardsRules.CanBeUnassigned(AwardsRules.CompanyTable, "FemaleTag2", typeof(int)) ||
    AwardsRules.CanBeUnassigned(AwardsRules.CompanyTable, "FedUID", typeof(int)))
    throw new Exception("Award category labels or optional winner validation are incorrect.");
Console.WriteLine("Passed Awards table routing, conservative editable-field allowlist, and validation.");

// Contract form changes must be recognized by typed value rather than
// spelling differences or a stale Save Changes toolbar state.
foreach (var (before, after, type, same) in new (string, string, Type, bool)[] {
    ("False", "True", typeof(bool), false),
    ("False", "0", typeof(bool), true),
    ("True", "1", typeof(bool), true),
    ("12", "13", typeof(int), false),
    ("012", "12", typeof(int), true),
    ("25.00", "25", typeof(decimal), true),
    ("25.00", "26.00", typeof(decimal), false),
    ("01/02/2022", "1/2/2022", typeof(DateTime), true),
    ("01/02/2022", "01/03/2022", typeof(DateTime), false),
    ("Name A", "Name B", typeof(string), false),
    ("Name A", "Name A", typeof(string), true)
})
    if (WorkerContractChangeRules.Equivalent(before, after, type) != same)
        throw new Exception("Contract change detection misclassified " + type.Name +
            " values " + before + " -> " + after);

var restoredFilterChoices = new[] {
    new WorkerFilterChoice("", "All companies"),
    new WorkerFilterChoice("100", "Company One"),
    new WorkerFilterChoice("200", "Company Two")
};
var prior = new WorkerFilterState("active", "yes", "5", "200", "Wrestler",
    "12", "24", "19", "42");
if (WorkerFilterState.Choose(restoredFilterChoices, prior.Company)?.Key != "200" ||
    WorkerFilterState.Choose(restoredFilterChoices, "999")?.Key != "" ||
    WorkerFilterState.Choose(restoredFilterChoices, "")?.Key != "" ||
    prior.MinimumAge != "19" || prior.MaximumAge != "42" ||
    prior.Gender != "5" || prior.Active != "active" || prior.Picture != "yes")
    throw new Exception("Roster filters did not preserve stored keys or age ranges after refresh.");
Console.WriteLine("Passed contract typed-difference checks and persistent roster-filter selection.");

// Dual-purpose partner text boxes: optional aliases for LINKED workers,
 // required NPC names when no real worker is selected.
var linkedAliased = HistoricalPartnerInput.Resolve(
    4964, "Brooke Mercer", "Chelsea Morgan", 2, allowNameOnly: false);
if (linkedAliased.WorkerId != 4964 ||
    linkedAliased.DisplayName != "Chelsea Morgan" || linkedAliased.NameOnly)
    throw new Exception("Partner 2 ring alias must preserve the linked worker UID.");
var linkedBlank = HistoricalPartnerInput.Resolve(
    4964, "Brooke Mercer", "", 2, allowNameOnly: false);
if (linkedBlank.WorkerId != 4964 || linkedBlank.DisplayName != "Brooke Mercer")
    throw new Exception("Blank optional linked-worker alias should use original name.");
var linkedTriosAlias = HistoricalPartnerInput.Resolve(
    503, "Partner Three", "Different Ring Name", 3, allowNameOnly: true);
if (linkedTriosAlias.WorkerId != 503 ||
    linkedTriosAlias.DisplayName != "Different Ring Name")
    throw new Exception("Partner 3 linked alias did not override its display name.");
var historicalNpc = HistoricalPartnerInput.Resolve(
    null, null, "Grant Cade", 2, allowNameOnly: true);
if (historicalNpc.WorkerId != -1 ||
    historicalNpc.DisplayName != "Grant Cade" || !historicalNpc.NameOnly)
    throw new Exception("Unlinked Partner 2 name was not mapped to NPC UID -1.");
bool missingPartnerRejected = false;
try { HistoricalPartnerInput.Resolve(null, null, "", 2, allowNameOnly: true); }
catch (InvalidOperationException) { missingPartnerRejected = true; }
if (!missingPartnerRejected)
    throw new Exception("Unselected and unnamed partner incorrectly accepted.");
bool safeNpcRejected = false;
try { HistoricalPartnerInput.Resolve(null, null, "Grant Cade", 2, allowNameOnly: false); }
catch (InvalidOperationException) { safeNpcRejected = true; }
if (!safeNpcRejected)
    throw new Exception("Safe existing-slot mode accepted an unlinked NPC partner.");
bool longAliasRejected = false;
try { HistoricalPartnerInput.Resolve(4964, "Brooke Mercer",
    new string('X', 31), 2, allowNameOnly: true); }
catch (InvalidOperationException) { longAliasRejected = true; }
if (!longAliasRejected)
    throw new Exception("Linked ring-name alias exceeded the 30-character limit.");
Console.WriteLine("Passed dual-purpose partner names: linked aliases and unlinked NPC teammates.");

// Historical name import must accept manually typed champion names without
// linked tblWorker rows, including full lineups for tag team and trios titles.
var singleNames = NameOnlyChampionImport.Parse(
    "Grant Cade\nDamien Ward, Another Historic Name", 1);
if (singleNames.Count != 3 || singleNames[0].Names[0] != "Grant Cade" ||
    singleNames[1].Names[0] != "Damien Ward")
    throw new Exception("Typed historical singles list import failed.");
var tagNames = NameOnlyChampionImport.Parse(
    "Carson Cade / Grant Cade\nTag Partner A, Tag Partner B", 2);
if (tagNames.Count != 2 || tagNames[0].Names[1] != "Grant Cade" ||
    tagNames[1].Names[1] != "Tag Partner B")
    throw new Exception("Complete historic tag-team list import failed.");
var triosNames = NameOnlyChampionImport.Parse("A / B / C\nD,E,F", 3);
if (triosNames.Count != 2 || triosNames[1].Names[2] != "F")
    throw new Exception("Complete historic trios list import failed.");

bool invalidIncompleteImport = false;
try { NameOnlyChampionImport.Parse("Carson Cade", 2); }
catch (InvalidOperationException) { invalidIncompleteImport = true; }
if (!invalidIncompleteImport)
    throw new Exception("Incomplete historical team import was accepted.");

var nameOnlyInput = SuperstarReignInputPlanner.Expand(
    new[] { new SuperstarReignInputPlanner.Request(
        "Grant Cade", -2, 2014, 2014, 1, "2014", "") },
    2013, 2015);
if (nameOnlyInput.Count != 1 || nameOnlyInput[0].WorkerId != -2 ||
    nameOnlyInput[0].Name != "Grant Cade")
    throw new Exception("A name-only historical winner was rejected by the year planner.");
var nameOnlySchedule = AnchoredHistoryPlanner.GenerateFromYears(
    nameOnlyInput, new[] { new NpcNameSeed("Background NPC", null, null),
        new NpcNameSeed("Another NPC", null, null) },
    2014, 2014, startMonth: 1);
if (!nameOnlySchedule.Any(r => r.Holder1 == -2 &&
    r.HolderName1 == "Grant Cade" && r.Captured.Year == 2014))
    throw new Exception("Name-only champion was lost by the scheduler.");

var mixedNameOnlyAnchors = new[] {
    new AnchoredHistoryPlanner.Anchor("Grant Cade", -2, 2014, 2014, 2014,
        SourceOrder: 0)
};
var nameOnlyTagReigns = new[] {
    new GeneratedTitleReign(1, -2, "Grant Cade", -1, "",
        new DateTime(2014, 1, 1), new DateTime(2014, 12, 31), 8)
};
var nameOnlyTagTeams = new Dictionary<int, TeamHistoryPlanner.LinkedTeam> {
    [0] = new(new[] { -1, 501 }, new[] { "Grant Cade", "Carson Cade" })
};
var nameOnlyTagResult = TeamHistoryPlanner.Populate(
    nameOnlyTagReigns, mixedNameOnlyAnchors, nameOnlyTagTeams,
    new[] { new NpcNameSeed("Background", null, null),
        new NpcNameSeed("Other Background", null, null) },
    2, new Random(1));
if (nameOnlyTagResult.Count != 1 ||
    nameOnlyTagResult[0].Holder1 != -1 || nameOnlyTagResult[0].Holder2 != 501 ||
    nameOnlyTagResult[0].HolderName1 != "Grant Cade" ||
    nameOnlyTagResult[0].HolderName2 != "Carson Cade")
    throw new Exception("Named NPC primary champion was not linked to real teammate.");
HistoricalReignRules.ValidateHolders(
    new[] { nameOnlyTagResult[0].Holder1, nameOnlyTagResult[0].Holder2, -1 },
    new[] { nameOnlyTagResult[0].HolderName1, nameOnlyTagResult[0].HolderName2, "" },
    2, new HashSet<int> { 501 });

Console.WriteLine("Passed typed historical name imports and name-only pinned winner generation.");

// Full tag-team history: fixed real linked champions, NPC teams before and
// after them, and dates/defenses preserved exactly from the singles scheduler.
var tagDate1 = new DateTime(2012, 1, 1);
var tagDate2 = new DateTime(2014, 4, 5);
var tagDate3 = new DateTime(2016, 1, 1);
var generatedTeamSingles = new[] {
    new GeneratedTitleReign(1, -1, "NPC 1", -1, "", tagDate1,
        new DateTime(2014, 4, 4), 4),
    new GeneratedTitleReign(2, 101, "Carson", -1, "", tagDate2,
        new DateTime(2015, 12, 31), 12),
    new GeneratedTitleReign(3, -1, "NPC 2", -1, "", tagDate3,
        new DateTime(2018, 1, 1), 7)
};
var fixedTagAnchors = new[] {
    new AnchoredHistoryPlanner.Anchor("Carson", 101, 2014, 2010, 2020,
        SourceOrder: 0)
};
var teamSeeds = Enumerable.Range(1, 12)
    .Select(n => new NpcNameSeed($"Historical Wrestler {n}", null, null)).ToArray();
var fixedTags = new Dictionary<int, TeamHistoryPlanner.LinkedTeam> {
    [0] = new(new[] { 101, 202 }, new[] { "Carson", "Tag Partner" })
};
var tagHistory = TeamHistoryPlanner.Populate(
    generatedTeamSingles, fixedTagAnchors, fixedTags, teamSeeds, 2, new Random(123));
if (tagHistory.Count != 3 || tagHistory[1].Holder1 != 101 ||
    tagHistory[1].Holder2 != 202 || tagHistory[1].Holder3 != -1 ||
    tagHistory[1].HolderName1 != "Carson" ||
    tagHistory[1].HolderName2 != "Tag Partner" ||
    tagHistory[1].HolderName3 != "" ||
    tagHistory[0].Holder1 != -1 || tagHistory[0].Holder2 != -1 ||
    tagHistory[0].HolderName1 == tagHistory[0].HolderName2 ||
    tagHistory[2].HolderName1 == tagHistory[2].HolderName2 ||
    tagHistory[1].Captured != tagDate2 || tagHistory[1].Defences != 12)
    throw new Exception("Tag-team full-history mapping altered worker links, NPC partners or dates.");

// Named NPC partners are valid even when the first team member is linked,
// e.g. Carson holds a historical tag title alongside his late father.
var carsonWithFather = TeamHistoryPlanner.Populate(
    new[] { generatedTeamSingles[1] }, fixedTagAnchors,
    new Dictionary<int, TeamHistoryPlanner.LinkedTeam> {
        [0] = new(new[] { 101, -1 }, new[] { "Carson", "Carson's Father" })
    }, teamSeeds, 2, new Random(55));
if (carsonWithFather.Count != 1 ||
    carsonWithFather[0].Holder1 != 101 ||
    carsonWithFather[0].Holder2 != -1 ||
    carsonWithFather[0].HolderName2 != "Carson's Father")
    throw new Exception("Mixed linked/NPC tag partners were not retained.");
HistoricalReignRules.ValidateHolders(
    new[] { carsonWithFather[0].Holder1, carsonWithFather[0].Holder2, -1 },
    new[] { carsonWithFather[0].HolderName1, carsonWithFather[0].HolderName2, "" },
    2, new HashSet<int> { 101 });

foreach (var reign in tagHistory)
    HistoricalReignRules.ValidateHolders(
        new[] { reign.Holder1, reign.Holder2, reign.Holder3 },
        new[] { reign.HolderName1, reign.HolderName2, reign.HolderName3 },
        2, new HashSet<int> { 101, 202 });

// Two different trios with the same primary wrestler winning twice in 2014
// must map to their distinct SourceOrder teams, not accidentally share partners.
var triosAnchors = new[] {
    new AnchoredHistoryPlanner.Anchor("Tavitia", 301, 2014, 2010, 2020,
        SourceOrder: 0),
    new AnchoredHistoryPlanner.Anchor("Tavitia", 301, 2014, 2010, 2020,
        SourceOrder: 1)
};
var triosSingles = new[] {
    new GeneratedTitleReign(1, 301, "Tavitia", -1, "",
        new DateTime(2014, 2, 1), new DateTime(2014, 5, 31), 3),
    new GeneratedTitleReign(2, 301, "Tavitia", -1, "",
        new DateTime(2014, 8, 1), new DateTime(2014, 11, 30), 5),
    new GeneratedTitleReign(3, -1, "Generated", -1, "",
        new DateTime(2015, 1, 1), new DateTime(2015, 12, 31), 4)
};
var triosTeams = new Dictionary<int, TeamHistoryPlanner.LinkedTeam> {
    [0] = new(new[] { 301, 302, 303 }, new[] { "Tavitia", "Partner A", "Partner B" }),
    [1] = new(new[] { 301, 304, 305 }, new[] { "Tavitia", "Partner C", "Partner D" })
};
var triosHistory = TeamHistoryPlanner.Populate(
    triosSingles, triosAnchors, triosTeams, teamSeeds, 3, new Random(4321));
if (triosHistory.Count != 3 ||
    triosHistory[0].Holder3 != 303 || triosHistory[0].HolderName3 != "Partner B" ||
    triosHistory[1].Holder2 != 304 || triosHistory[1].Holder3 != 305 ||
    triosHistory[2].Holder1 != -1 || triosHistory[2].Holder2 != -1 ||
    triosHistory[2].Holder3 != -1 ||
    new[] { triosHistory[2].HolderName1, triosHistory[2].HolderName2,
        triosHistory[2].HolderName3 }.Distinct().Count() != 3 ||
    triosHistory[1].Captured != new DateTime(2014, 8, 1))
    throw new Exception("Trios history lost third holders, repeat winning-year teams or NPC identities.");

foreach (var reign in triosHistory)
    HistoricalReignRules.ValidateHolders(
        new[] { reign.Holder1, reign.Holder2, reign.Holder3 },
        new[] { reign.HolderName1, reign.HolderName2, reign.HolderName3 },
        3, new HashSet<int> { 301, 302, 303, 304, 305 });

bool rejectsIncompleteTeam = false;
try
{
    TeamHistoryPlanner.Populate(generatedTeamSingles, fixedTagAnchors,
        new Dictionary<int, TeamHistoryPlanner.LinkedTeam> {
            [0] = new(new[] { 101, 101 }, new[] { "Carson", "Carson" })
        }, teamSeeds, 2);
}
catch (InvalidOperationException) { rejectsIncompleteTeam = true; }
if (!rejectsIncompleteTeam)
    throw new Exception("Generated team validation accepted a duplicate linked worker.");

bool rejectsInsufficientNpcNames = false;
try
{
    TeamHistoryPlanner.Populate(
        new[] { generatedTeamSingles[0] }, Array.Empty<AnchoredHistoryPlanner.Anchor>(),
        new Dictionary<int, TeamHistoryPlanner.LinkedTeam>(),
        new[] { teamSeeds[0], teamSeeds[1] }, 3, new Random(10));
}
catch (InvalidOperationException) { rejectsInsufficientNpcNames = true; }
if (!rejectsInsufficientNpcNames)
    throw new Exception("Generated trios validation accepted too few name-only NPCs.");

Console.WriteLine("Passed complete tag-team/trios title-history lineups, NPCs, multiple fixed teams and dates.");

// The Carson story: a linked wrestler may win a tag title alongside his
// deceased father, a name-only historical NPC not inserted into tblWorker.
var knownWorkers = new HashSet<int> { 101, 202 };
HistoricalReignRules.ValidateHolders(
    new[] { 101, -1, -1 }, new[] { "Carson", "Carson's Father", "" },
    2, knownWorkers);
// A name-only predecessor (e.g. Damien Ward) and real successor (Tavitia)
// may be represented independently, without inventing real worker identities.
HistoricalReignRules.ValidateHolders(
    new[] { -1, -1, -1 }, new[] { "Damien Ward", "", "" }, 1, knownWorkers);
HistoricalReignRules.ValidateHolders(
    new[] { 202, -1, -1 }, new[] { "Tavitia", "", "" }, 1, knownWorkers);
int chronologicalPosition = HistoricalReignRules.InsertionPosition(
    new (DateTime Captured, DateTime Lost)[] {
        (new DateTime(2010, 1, 1), new DateTime(2010, 12, 31)),
        (new DateTime(2015, 1, 1), new DateTime(2015, 12, 31))
    }, new DateTime(2014, 1, 1), new DateTime(2014, 12, 31), false);
if (chronologicalPosition != 1)
    throw new Exception("2014 title-history insertion was not placed between 2010 and 2015.");

bool rejectedDuplicateWorker = false;
try
{
    HistoricalReignRules.ValidateHolders(
        new[] { 101, 101, -1 }, new[] { "Carson", "Carson", "" },
        2, knownWorkers);
}
catch (InvalidOperationException) { rejectedDuplicateWorker = true; }
if (!rejectedDuplicateWorker)
    throw new Exception("A real worker appeared twice in the same tag-team history.");

bool rejectedNonexistentWorker = false;
try
{
    HistoricalReignRules.ValidateHolders(
        new[] { 101, 999999, -1 }, new[] { "Carson", "Unknown worker", "" },
        2, knownWorkers);
}
catch (InvalidOperationException) { rejectedNonexistentWorker = true; }
if (!rejectedNonexistentWorker)
    throw new Exception("An unlinked positive worker UID bypassed validation.");

bool rejectedOverlap = false;
try
{
    HistoricalReignRules.InsertionPosition(
        new (DateTime Captured, DateTime Lost)[] {
            (new DateTime(2013, 1, 1), new DateTime(2014, 6, 1))
        }, new DateTime(2014, 1, 1), new DateTime(2014, 12, 1), false);
}
catch (InvalidOperationException) { rejectedOverlap = true; }
if (!rejectedOverlap)
    throw new Exception("An overlapping historical title reign bypassed validation.");
Console.WriteLine("Passed name-only NPC tag team, 2014 title history and overlap safety checks.");

static void Check(int year, int month, int occurrence, DayOfWeek weekday, DateTime expected)
{
    var actual = AnnualTournamentCalendar.ResolveDate(year, month, occurrence, weekday);
    if (actual != expected)
        throw new Exception($"Expected {expected:yyyy-MM-dd}, got {actual:yyyy-MM-dd}.");
}
