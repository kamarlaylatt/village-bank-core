using System.Text.RegularExpressions;
using VillageBank.Api.Domain;

namespace VillageBank.Api.Auth;

public static class PasswordPolicy
{
    public const int MinLength = 8;
    public const int MaxLength = 100;

    public static void Validate(string? password, string username)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
            throw new BusinessRuleException($"Password must be at least {MinLength} characters.");

        if (password.Length > MaxLength)
            throw new BusinessRuleException($"Password can be at most {MaxLength} characters.");

        if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
            throw new BusinessRuleException("Password must not be the same as the username.");
    }
}

public static class UsernamePolicy
{
    private static readonly Regex Pattern = new("^[a-z0-9][a-z0-9._-]{2,49}$", RegexOptions.Compiled);

    public static string NormalizeAndValidate(string? username)
    {
        var normalized = (username ?? "").Trim().ToLowerInvariant();

        if (!Pattern.IsMatch(normalized))
            throw new BusinessRuleException(
                "Username must be 3-50 characters: lowercase letters, digits, '.', '_' or '-', starting with a letter or digit.");

        return normalized;
    }
}
