using System.Text.RegularExpressions;

namespace lms_api.Common;

public static class PasswordPolicy
{
    public const int MinLength = 8;

    public static bool IsValid(string? password, out string error)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            error = "Password is required.";
            return false;
        }

        if (password.Length < MinLength)
        {
            error = $"Password must be at least {MinLength} characters.";
            return false;
        }

        if (!Regex.IsMatch(password, @"[A-Z]"))
        {
            error = "Password must contain at least one uppercase letter.";
            return false;
        }

        if (!Regex.IsMatch(password, @"[a-z]"))
        {
            error = "Password must contain at least one lowercase letter.";
            return false;
        }

        if (!Regex.IsMatch(password, @"[0-9]"))
        {
            error = "Password must contain at least one digit.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
