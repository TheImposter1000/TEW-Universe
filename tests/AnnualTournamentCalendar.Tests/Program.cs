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

static void Check(int year, int month, int occurrence, DayOfWeek weekday, DateTime expected)
{
    var actual = AnnualTournamentCalendar.ResolveDate(year, month, occurrence, weekday);
    if (actual != expected)
        throw new Exception($"Expected {expected:yyyy-MM-dd}, got {actual:yyyy-MM-dd}.");
}
