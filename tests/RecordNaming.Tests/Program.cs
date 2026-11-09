using TEWUniverse.Studio;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new Exception($"Expected '{expected}', got '{actual}'.");
}

var ongoing = new DateTime(1666, 1, 1);
var start = new DateTime(2022, 8, 4);
var end = new DateTime(2023, 10, 31);
Equal("DNA (2022 - )",
    StudioRecordNames.Employment("DNA", start, ongoing));
Equal("DNA (2022 - 2023)",
    StudioRecordNames.Employment("DNA", start, end));
Equal("OLLIE (2022 - )",
    StudioRecordNames.Employment("OLLIE", start, ongoing));

var automatic = EmploymentNameGeneration.Validate(
    "DNA", "8/4/2022", "1/1/1666");
Equal(true, automatic.IsValid);
Equal("DNA (2022 - )", automatic.RecordName);
Equal("DNA (2022 - 2023)",
    EmploymentNameGeneration.Validate(
        "DNA", "8/4/2022", "10/31/2023").RecordName);
Equal(false, EmploymentNameGeneration.Validate(
    "DNA", "10/31/2023", "8/4/2022").IsValid);

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
        start, ongoing, "Dynamic National Alliance (2022 - )", "Dynamic National Alliance"),
    new(673, 0, "OLLIE",
        start, end, "OLLIE (2022-2023)")
};
var plan = RecordNameRepairPlanner.Employment(history, null);
Equal(2, plan.Changes.Count);
Equal("DNA (2022 - )", plan.Changes[0].NewName);
Equal("OLLIE (2022 - 2023)", plan.Changes[1].NewName);


Equal(TewFileKind.Scenario, TewFileKindDetector.Detect(new[]
{
    "tblWorker", "tblFed", "tblInfo", "tblBelt"
}));
Equal(TewFileKind.SaveGame, TewFileKindDetector.Detect(new[]
{
    "tblWorker", "tblFed", "tblInfo", "tblGameInfo", "tblPlayerInfo"
}));
Equal(TewFileKind.SaveGame, TewFileKindDetector.Detect(new[]
{
    "TBLGAMEINFO", "TBLMATCHLOG"
}));
Equal(TewFileKind.Unknown, TewFileKindDetector.Detect(new[] { "tblWorker", "tblInfo" }));


var skill = new System.Data.DataTable("tblWorkerSkill");
skill.Columns.Add("WorkerUID", typeof(int));
skill.Columns.Add("Brawling", typeof(int));
skill.Columns.Add("CustomRating", typeof(int));
var skillKey = new[] { "WorkerUID" };
Equal(true, SaveEditRules.CanEdit("tblWorkerSkill", skill.Columns["Brawling"]!, skillKey));
Equal(true, SaveEditRules.CanEdit("tblWorkerSkill", skill.Columns["CustomRating"]!, skillKey));
Equal(false, SaveEditRules.CanEdit("tblWorkerSkill", skill.Columns["WorkerUID"]!, skillKey));
Equal(false, SaveEditRules.CanEdit("tblWorkerSkill", skill.Columns["Brawling"]!, Array.Empty<string>()));
Equal(true, SaveEditRules.IsValidValue("tblWorkerSkill", skill.Columns["Brawling"]!, 100));
Equal(false, SaveEditRules.IsValidValue("tblWorkerSkill", skill.Columns["Brawling"]!, -1));
Equal(false, SaveEditRules.IsValidValue("tblWorkerSkill", skill.Columns["Brawling"]!, 101));
Equal(false, SaveEditRules.IsValidValue("tblWorkerSkill", skill.Columns["Brawling"]!, DBNull.Value));

var belts = new System.Data.DataTable("tblBelt");
belts.Columns.Add("UID", typeof(int));
belts.Columns.Add("Name", typeof(string));
belts.Columns.Add("Fed", typeof(int));
belts.Columns.Add("Picture", typeof(string));
belts.Columns.Add("Profile", typeof(string));
belts.Columns.Add("Prestige", typeof(int));
var beltKey = new[] { "UID" };
Equal(true, SaveEditRules.CanEdit("tblBelt", belts.Columns["Name"]!, beltKey));
Equal(true, SaveEditRules.CanEdit("tblBelt", belts.Columns["Picture"]!, beltKey));
Equal(true, SaveEditRules.CanEdit("tblBelt", belts.Columns["Profile"]!, beltKey));
Equal(true, SaveEditRules.CanEdit("tblBelt", belts.Columns["Prestige"]!, beltKey));
Equal(false, SaveEditRules.CanEdit("tblBelt", belts.Columns["UID"]!, beltKey));
Equal(false, SaveEditRules.CanEdit("tblBelt", belts.Columns["Fed"]!, beltKey));
Equal(true, SaveEditRules.IsValidValue("tblBelt", belts.Columns["Prestige"]!, 75));

var companies = new System.Data.DataTable("tblFed");
companies.Columns.Add("UID", typeof(int));
companies.Columns.Add("Name", typeof(string));
companies.Columns.Add("Money", typeof(long));
Equal(true, SaveEditRules.CanEdit("tblFed", companies.Columns["Money"]!, beltKey));
Equal(true, SaveEditRules.CanEdit("tblFed", companies.Columns["Name"]!, beltKey));
Equal(true, SaveEditRules.IsValidValue("tblFed", companies.Columns["Money"]!, 500000L));

var game = new System.Data.DataTable("tblGameInfo");
game.Columns.Add("UID", typeof(int));
game.Columns.Add("CurrentDate", typeof(DateTime));
Equal(true, SaveEditRules.CanEdit("tblGameInfo", game.Columns["CurrentDate"]!, beltKey));
Equal(false, SaveEditRules.CanEdit("randomTable", game.Columns["CurrentDate"]!, beltKey));

Console.WriteLine("Passed naming checks and broad record-editing rules for Save Studio.");

