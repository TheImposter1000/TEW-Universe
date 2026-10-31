using TEWUniverse.Studio;

static void Expect(string expected, string actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new Exception($"Expected '{expected}', actual '{actual}'.");
}
var from = new DateTime(2022, 3, 7);
Expect("Dynamic National Alliance (2022 - )",
    StudioRecordNames.Employment("Dynamic National Alliance", from,
        StudioDateFormat.OngoingEndDate));
Expect("Dynamic National Alliance (2022 - 2024)",
    StudioRecordNames.Employment("Dynamic National Alliance", from,
        new DateTime(2024, 2, 2)));
Expect("OLLIE (2022 - 2022)",
    StudioRecordNames.Employment("OLLIE", from, new DateTime(2022, 3, 8)));
var check = EmploymentNameGeneration.Validate(
    "Dynamic National Alliance", "3/7/2022", "1/1/1666");
if (!check.IsValid) throw new Exception("Valid ongoing contract did not generate.");
Expect("Dynamic National Alliance (2022 - )", check.RecordName!);
var invalid = EmploymentNameGeneration.Validate("DNA", "3/7/2022", "1/2/2000");
if (invalid.IsValid) throw new Exception("Reversed dates were accepted.");
Expect("Fighting Against", ChemistryRules.TypeLabel(1));
Expect("Teaming With", ChemistryRules.TypeLabel(2));
Expect("Managing", ChemistryRules.TypeLabel(3));
Expect("Commentating With", ChemistryRules.TypeLabel(4));
Expect("Booking", ChemistryRules.TypeLabel(5));
Expect("Great", ChemistryRules.QualityLabel(1));
Expect("Good", ChemistryRules.QualityLabel(2));
Expect("Neutral", ChemistryRules.QualityLabel(3));
Expect("Poor", ChemistryRules.QualityLabel(4));
Expect("Awful", ChemistryRules.QualityLabel(5));
Expect("Code 99", ChemistryRules.TypeLabel(99));
Expect("FA-GT: J.Cole / C.Potter",
    ChemistryRules.RecordName("Jayden Cole", "Colby Potter", 1, 1, Array.Empty<string>(), 255));
Expect("TW-GD: J.Cole / C.Potter",
    ChemistryRules.RecordName("Jayden Cole", "Colby Potter", 2, 2, Array.Empty<string>(), 255));
Expect("FA-GT: J.Cole / C.Potter #2",
    ChemistryRules.RecordName("Jayden Cole", "Colby Potter", 1, 1,
        new[] { "FA-GT: J.Cole / C.Potter" }, 255));
bool threw = false;
try { ChemistryRules.RecordName("Jayden Cole", "Colby Potter", 99, 1,
    Array.Empty<string>(), 255); }
catch (InvalidOperationException) { threw = true; }
if (!threw) throw new Exception("Unknown type generated a misleading chemistry name.");
Console.WriteLine("Employment and Chemistry record naming checks passed.");
