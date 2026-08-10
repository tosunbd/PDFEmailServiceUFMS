using System.Text.RegularExpressions;

namespace PDFEmailServiceUFMS.Services;

public static partial class EmailValidator
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 200)]
    private static partial Regex EmailRegex();

    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return false;

        try
        {
            return EmailRegex().IsMatch(email.Trim());
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
