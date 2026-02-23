using System.Text.RegularExpressions;

namespace Codions.Contracts.Validation;

public static class JobIdValidation
{
    public const string Pattern = "^[a-f0-9]{12}$";
    public const string FormatErrorMessage = "Invalid job id format. Expected 12 lowercase hex characters.";

    private static readonly Regex JobIdRegex = new(Pattern, RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValid(string? jobId)
    {
        return !string.IsNullOrWhiteSpace(jobId) && JobIdRegex.IsMatch(jobId);
    }
}
