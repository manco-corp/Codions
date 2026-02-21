using System.Text;

namespace Codions.BotHarness.Helpers;

internal static class DiffHelper
{
    public static string ExtractAddedLines(string diffText)
    {
        if (string.IsNullOrEmpty(diffText))
            return "";

        var sb = new StringBuilder();
        foreach (var line in diffText.Split('\n'))
        {
            if (line.StartsWith("+++"))
                continue;
            if (line.StartsWith('+'))
                sb.AppendLine(line[1..]);
        }

        return sb.ToString();
    }
}