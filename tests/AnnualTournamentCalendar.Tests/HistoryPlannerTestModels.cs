// Minimal test-only versions of the internal data records declared in
// DatabaseSession.cs. They allow the real planner to run without opening an MDB.
namespace TEWUniverse.Studio;

internal sealed record NpcNameSeed(string Name, string? GenderCode, string? RaceCode);

internal sealed record GeneratedTitleReign(
    int Order, int Holder1, string HolderName1,
    int Holder2, string HolderName2,
    DateTime Captured, DateTime Lost, short Defences,
    int Holder3 = -1, string HolderName3 = "");

internal sealed record FieldEdit(string Table, string Key, string Field,
    Type FieldType, string NewText);
internal sealed record CompanyFieldChange(FieldEdit Edit, int RecordId);
