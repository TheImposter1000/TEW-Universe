using System.Globalization;

namespace TEWUniverse.Studio;

/// <summary>
/// User-verified TEW IX numeric enumerations for display purposes only.
/// These labels never replace the numeric values read from or written to an MDB.
/// </summary>
internal sealed record TewCodeChoice(int Code, string Label)
{
    public override string ToString() => Label;
}

internal static class TewCodeLabels
{
    private static readonly TewCodeChoice[] Genders =
    {
        new(1, "Male"),
        new(2, "Transgender Male"),
        new(3, "Non-binary AMAB"),
        new(4, "Other Male"),
        new(5, "Female"),
        new(6, "Transgender Female"),
        new(7, "Non-binary AFAB"),
        new(8, "Other Female")
    };

    private static readonly TewCodeChoice[] TagTeamTypes =
    {
        new(1, "Permanent Unit"),
        new(2, "Unit"),
        new(3, "Individuals"),
        new(4, "Inactive Permanent Unit"),
        new(5, "Inactive Unit"),
        new(6, "Inactive Individuals")
    };

    // The line number (starting at one) is the actual TEW IX numeric code.
    // Never reorder these labels without reconfirming the corresponding game codes.
    private static TewCodeChoice[] Ordered(string labels) =>
        labels.Split('\n', StringSplitOptions.RemoveEmptyEntries |
            StringSplitOptions.TrimEntries)
        .Select((label, index) => new TewCodeChoice(index + 1, label))
        .ToArray();

    private static readonly TewCodeChoice[] Sexualities = Ordered("""
        Heterosexual
        Homosexual
        Bisexual
        Asexual
        Pansexual
        """);

    private static readonly TewCodeChoice[] PronounSets = Ordered("""
        He-Him
        She-Her
        They-Them
        He-Them
        She-Them
        """);

    private static readonly TewCodeChoice[] Races = Ordered("""
        White
        Black
        Asian
        Hispanic
        Native American
        Middle Eastern
        South Asian
        Pacific
        Other
        """);

    private static readonly TewCodeChoice[] BasedInRegions = Ordered("""
        USA
        Canada
        Mexico
        British Isles
        Europe
        Japan
        India
        Oceania
        Great Lakes
        Mid Atlantic
        Mid South
        Mid West
        New England
        North West
        South East
        South West
        Tri State
        Puerto Rico
        Hawaii
        Alberta
        British Columbia
        Manitoba
        Ontario
        Quebec
        Saskatchewan
        The Maritimes
        Centro
        Noreste
        Noroccidente
        Occidente
        Sur
        Sureste
        Midlands
        Northern England
        Southern England
        Ireland
        Scotland
        Wales
        Central Europe
        Eastern Europe
        Eastern Central Europe
        Iberia
        Northern Europe
        Southern Europe
        Southern Mediterranean
        Western Europe
        Chubu
        Chugoku
        Hokkaido
        Kansai
        Kanto
        Kyushu
        Shikoku
        Tohoku
        Eastern India
        Northern India
        Southern India
        Western India
        New South Wales
        New Zealand
        Queensland
        South Australia
        Tasmania
        Victoria
        Western Australia
        """);

    private static readonly TewCodeChoice[] Nationalities = Ordered("""
        Afghan
        Albanian
        Algerian
        American
        American Samoan
        Andorran
        Angolan
        Anguillian
        Antiguan
        Argentinian
        Armenian
        Aruban
        Australian
        Austrian
        Azerbaijani
        Bahamian
        Bahraini
        Bangladeshi
        Barbadian
        Basotho
        Belarusian
        Belgian
        Belizean
        Beninese
        Bhutanese
        Bolivian
        Bosnian
        Brazilian
        Bruneian
        Bulgarian
        Burkinabe
        Burmese
        Burundian
        Cambodian
        Cameroonian
        Canadian
        Cape Verdean
        Caymanian
        Central African
        Chadian
        Chilean
        Chinese
        Colombian
        Congolese
        Cook Islander
        Costa Rican
        Croatian
        Cuban
        Cypriot
        Czech
        Dagestani
        Danish
        Djiboutian
        Dominican
        Dutch
        Ecuadorian
        Egyptian
        Emirati
        English
        Eritrean
        Estonian
        Ethiopian
        Fijian
        Filipino
        Finnish
        French
        Gabonese
        Gambian
        Georgian
        German
        Ghanaian
        Greek
        Grenadian
        Guadeloupean
        Guamanian
        Guatemalan
        Guinean
        Guyanese
        Haitian
        Hawaiian
        Honduran
        Hong Konger
        Hungarian
        Icelandic
        Indian
        Indonesian
        Iranian
        Iraqi
        Irish
        Israeli
        Italian
        Ivorian
        Jamaican
        Japanese
        Jordanian
        Kazakh
        Kenyan
        Kittitian
        Kosovan
        Kuwaiti
        Kyrgyz
        Laotian
        Latvian
        Lebanese
        Liberian
        Libyan
        Liechtensteiner
        Lithuanian
        Luxembourger
        Macedonian
        Malagasy
        Malawian
        Malaysian
        Maldivian
        Malinese
        Maltese
        Martinican
        Mauritian
        Mexican
        Moldovan
        Monegasque
        Mongolian
        Montenegrin
        Montserratian
        Moroccan
        Motswanan
        Mozambican
        Namibian
        Nepalese
        Nevisian
        New Zealander
        Nicaraguan
        Nigerian
        North Korean
        Northern Irish
        Norwegian
        Omani
        Pakistani
        Palestinian
        Panamanian
        Papua New Guinean
        Paraguayan
        Peruvian
        Polish
        Portuguese
        Puerto Rican
        Qatari
        Romanian
        Russian
        Rwandan
        Saint Lucian
        Saint Martiner
        Salvadoran
        Sammarinese
        Samoan
        Saudi
        Scottish
        Senegalese
        Serbian
        Sierra Leonean
        Singaporean
        Slovakian
        Slovenian
        Somali
        South African
        South Korean
        Spanish
        Sri Lankan
        Sudanese
        Surinamese
        Swazi
        Swedish
        Swiss
        Syrian
        Taiwanese
        Tajik
        Tanzanian
        Thai
        Timorese
        Tobagonian
        Togolese
        Tongan
        Trinidadian
        Tunisian
        Turkish
        Turkmen
        Tuvaluan
        Ugandan
        Ukrainian
        Uruguayan
        Uzbek
        Vanuatuan
        Venezuelan
        Vietnamese
        Vincentian
        Virgin Islander
        Welsh
        Yemeni
        Zambian
        Zimbabwean
        """);

    private static readonly TewCodeChoice[] WrestlingStyles = Ordered("""
        Regular
        Entertainer
        Comedy
        Powerhouse
        Impactful
        Striker
        Brawler
        Hardcore
        Psychopath
        Luchador
        High Flyer
        Technician
        Technician Flyer
        Technician Striker
        Daredevil
        MMA Crossover
        """);

    private static readonly TewCodeChoice[] CompetesAgainst = Ordered("""
        Any
        Men's Division Only
        Women's Division Only
        Primarily Men's Division
        Primary Women's Division
        """);

    private static readonly TewCodeChoice[] OutsideRelationships = Ordered("""
        No
        Dating
        Engaged
        Married
        """);

    public static bool IsMappedField(string table, string column) =>
        Choices(table, column).Count > 0;

    public static IReadOnlyList<TewCodeChoice> Choices(string table, string column)
    {
        if (table.Equals("tblWorker", StringComparison.OrdinalIgnoreCase))
            return column.ToLowerInvariant() switch
            {
                "gender" => Genders,
                "sexuality" => Sexualities,
                "pronouns" => PronounSets,
                "race" => Races,
                "based_in" => BasedInRegions,
                "nationality" => Nationalities,
                "style" => WrestlingStyles,
                "competesagainst" => CompetesAgainst,
                "outsiderel" => OutsideRelationships,
                _ => Array.Empty<TewCodeChoice>()
            };
        if (table.Equals("tblTeam", StringComparison.OrdinalIgnoreCase) &&
            column.Equals("Type", StringComparison.OrdinalIgnoreCase))
            return TagTeamTypes;
        return Array.Empty<TewCodeChoice>();
    }

    public static bool TryGetCode(string? stored, out int code)
    {
        string text = stored?.Trim() ?? "";
        if (text.StartsWith("Code ", StringComparison.OrdinalIgnoreCase))
            text = text[5..].Trim();
        return int.TryParse(text, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out code);
    }

    public static bool TryGetLabel(string table, string column, string? stored, out string label)
    {
        label = "";
        if (!TryGetCode(stored, out int code)) return false;
        foreach (var choice in Choices(table, column))
        {
            if (choice.Code != code) continue;
            label = choice.Label;
            return true;
        }
        return false;
    }

    public static string Display(string table, string column, string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return "Not recorded";
        if (TryGetLabel(table, column, stored, out string label)) return label;
        string original = stored.Trim();
        return original.StartsWith("Code ", StringComparison.OrdinalIgnoreCase)
            ? original : "Code " + original;
    }
}
