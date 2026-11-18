namespace DentalLoyalty.Application.Identity;

// The password policy for accounts created through the API (ADR-010 decision 5;
// SECURITY.md §8.3): 10-128 characters, no composition rules, and not the
// account's email. Length counts Unicode characters (scalar values), not UTF-16
// code units, so a character outside the Basic Multilingual Plane counts once.
// The password is otherwise taken as typed - it is never trimmed or changed -
// and is never logged or returned.
public static class PasswordPolicy
{
    public const int MinimumLength = 10;

    public const int MaximumLength = 128;

    // normalizedEmail is the account's email as EmailAddress.TryNormalize
    // returns it. The comparison ignores case and surrounding whitespace, so
    // " Ana@Example.com " is refused for ana@example.com.
    public static bool IsSatisfiedBy(string? password, string normalizedEmail)
    {
        ArgumentNullException.ThrowIfNull(normalizedEmail);

        if (password is null)
        {
            return false;
        }

        var length = password.EnumerateRunes().Count();

        if (length < MinimumLength || length > MaximumLength)
        {
            return false;
        }

        return !string.Equals(password.Trim(), normalizedEmail, StringComparison.OrdinalIgnoreCase);
    }
}
