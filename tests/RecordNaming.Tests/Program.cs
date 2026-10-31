using TEWUniverse.Studio;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected '{expected}', got '{actual}'.");
}

var ongoing = new DateTime(1666, 1, 1);
var start = new DateTime(2022, 8, 4);
var end = new DateTime(2023, 10, 31);
Equal("Dynamic National Alliance (2022 - )",
    StudioRecordNames.Employment("Dynamic National Alliance", start, ongoing));
Equal("Dynamic National Alliance (2022 - 2023)",
    StudioRecordNames.Employment("Dynamic National Alliance", start, end));
Equal("OLLIE (2022 - )",
    StudioRecordNames.Employment("OLLIE", start, ongoing));

var automatic = EmploymentNameGeneration.Validate(
    "Dynamic National Alliance", "8/4/2022", "1/1/1666");
Equal(true, automatic.IsValid);
Equal("Dynamic National Alliance (2022 - )", automatic.RecordName);
Equal("Dynamic National Alliance (2022 - 2023)",
    EmploymentNameGeneration.Validate(
        "Dynamic National Alliance", "8/4/2022", "10/31/2023").RecordName);
Equal(false, EmploymentNameGeneration.Validate(
    "Dynamic National Alliance", "10/31/2023", "8/4/2022").IsValid);

string[] types = { "Fighting Against", "Teaming With", "Managing",
    "Commentating With", "Booking" };
string[] typeAbbr = { "FA", "TW", "MW", "CW", "BW" };
string[] qualities = { "Great", "Good", "Neutral", "Poor", "Awful" };
string[] qualityAbbr = { "GT", "GD", "NT", "PR", "AW" };
for (int type = 1; type <= 5; type++)
{
    Equal(types[type - 1], ChemistryRules.TypeLabel(type));
    Equal(typeAbbr[type - 1], ChemistryRules.TypeAbbreviation(type));
    for (int quality = 1; quality <= 5; quality++)
    {
        Equal(qualities[quality - 1], ChemistryRules.QualityLabel(quality));
        Equal(qualityAbbr[quality - 1], ChemistryRules.QualityAbbreviation(quality));
        Equal($"{typeAbbr[type - 1]}-{qualityAbbr[quality - 1]}: J.Cole / C.Potter",
            ChemistryRules.RecordName("Jayden Cole", "Colby Potter",
                type, quality, Array.Empty<string>(), 100));
    }
}
Equal("FA-GT: J.Cole / C.Potter #2",
    ChemistryRules.RecordName("Jayden Cole", "Colby Potter",
        1, 1, new[] { "FA-GT: J.Cole / C.Potter" }, 100));
Equal("J.Van Dam", ChemistryRules.CompactWorkerName("John Van Dam"));
Equal("Code 99", ChemistryRules.TypeLabel(99));
Equal("Code 99", ChemistryRules.QualityLabel(99));
ChemistryRules.ValidateObserved(5, 5, Array.Empty<int>(), Array.Empty<int>());

var history = new List<EmploymentRecordNameRow> {
    new(672, 12, "DNA",
        start, ongoing, "DNA (2022 -?)", "Dynamic National Alliance"),
    new(673, 0, "OLLIE",
        start, end, "OLLIE (2022-2023)")
};
var plan = RecordNameRepairPlanner.Employment(history, null);
Equal(2, plan.Changes.Count);
Equal("Dynamic National Alliance (2022 - )", plan.Changes[0].NewName);
Equal("OLLIE (2022 - 2023)", plan.Changes[1].NewName);

Console.WriteLine("Passed 25 chemistry format combinations, employment Auto-name, and batch repair naming.");
