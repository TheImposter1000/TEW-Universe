using System.Globalization;
using System.Text;

namespace TEWUniverse.Studio;

/// <summary>
/// Human-readable unique database record keys for NEW entries only.
/// Existing TEW IX Recordname/RecordName identifiers remain untouched.
/// The trailing identity suffix survives truncation and protects against
/// collisions between workers, promotions and dates with the same name.
/// </summary>
internal static class StudioRecordNames
{
    internal static string Employment(string worker, string company, int workerId,
        int companyId, DateTime from, string nonce, int maxLength)
    {
        var prefix = $"CV {worker} {company}";
        var suffix = $" W{workerId} F{companyId} {from:yyyyMMdd} {nonce}";
        return Build(prefix, suffix, maxLength);
    }

    internal static string TitleReign(string title, int beltId, int order,
        string champion, DateTime captured, int maxLength)
    {
        var prefix = $"{title} {champion}";
        // Order and belt UID retain uniqueness even after shortening.
        var suffix = $" B{beltId} R{order} {captured:yyyyMMdd}";
        return Build(prefix, suffix, maxLength);
    }

    private static string Build(string prefix, string suffix, int maxLength)
    {
        static string Clean(string text)
        {
            var result = new StringBuilder(text.Length);
            foreach (char ch in text.Normalize(NormalizationForm.FormKD))
                if (char.IsLetterOrDigit(ch) && ch <= 127 ||
                    ch is ' ' or '-' or '_' or '.')
                    result.Append(ch);
            return string.Join(" ", result.ToString().Split(' ',
                StringSplitOptions.RemoveEmptyEntries));
        }
        var tail = Clean(suffix);
        string head = Clean(prefix);
        int limit = maxLength > 0 ? maxLength : 255;
        if (tail.Length > limit)
            throw new InvalidOperationException(
                "MDB Recordname column is too short for a unique generated identifier.");
        int prefixBudget = Math.Max(0, limit - tail.Length - 1);
        head = head.Length > prefixBudget
            ? head[..prefixBudget].TrimEnd() : head;
        return head.Length > 0 ? head + " " + tail : tail;
    }
}
