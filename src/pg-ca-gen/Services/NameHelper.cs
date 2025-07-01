using System.Globalization;
using System.Text.RegularExpressions;
using System.Linq;

namespace PgCaGen.Services;

public static class NameHelper
{
    public static string ToPascalCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var parts = Regex.Split(input, "[_\- ]+");
        var textInfo = CultureInfo.InvariantCulture.TextInfo;
        return string.Concat(parts.Select(p => textInfo.ToTitleCase(p.ToLowerInvariant())));
    }

    public static string Singularize(string name)
    {
        // Very naive singularization: remove trailing 's' if present.
        if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase) && name.Length > 1)
        {
            return name[..^1];
        }
        return name;
    }

    public static string Pluralize(string name)
    {
        if (name.EndsWith("s", StringComparison.OrdinalIgnoreCase)) return name;
        return name + "s";
    }
}