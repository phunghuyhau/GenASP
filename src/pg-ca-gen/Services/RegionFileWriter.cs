using System.Linq;
using System.Collections.Generic;

namespace PgCaGen.Services;

public static class RegionFileWriter
{
    public static void WriteFile(string path, string newRenderedContent, string regionName = "generated")
    {
        var regionStart = $"#region {regionName}";
        var regionEnd = "#endregion";

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, newRenderedContent);
            return;
        }

        // Extract new generated section
        var newGeneratedSection = ExtractRegion(newRenderedContent, regionStart, regionEnd);
        if (newGeneratedSection is null)
            throw new InvalidOperationException($"New rendered content missing required region {regionStart}");

        var lines = File.ReadAllLines(path).ToList();
        var startIdx = lines.FindIndex(l => l.Trim() == regionStart);
        if (startIdx == -1)
            throw new InvalidOperationException($"Start region not found in existing file {path}");
        var endIdx = lines.FindIndex(startIdx + 1, l => l.Trim() == regionEnd);
        if (endIdx == -1)
            throw new InvalidOperationException($"End region not found in existing file {path}");

        // Replace between markers
        lines.RemoveRange(startIdx + 1, endIdx - startIdx - 1);
        lines.InsertRange(startIdx + 1, newGeneratedSection);
        File.WriteAllLines(path, lines);
    }

    private static IEnumerable<string>? ExtractRegion(string text, string regionStart, string regionEnd)
    {
        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var startIdx = lines.FindIndex(l => l.Trim() == regionStart);
        if (startIdx == -1) return null;
        var endIdx = lines.FindIndex(startIdx + 1, l => l.Trim() == regionEnd);
        if (endIdx == -1) return null;
        return lines.Skip(startIdx + 1).Take(endIdx - startIdx - 1).ToList();
    }
}