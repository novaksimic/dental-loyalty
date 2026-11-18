namespace DentalLoyalty.Application.Identity;

// The canonical form of an account email (ADR-010 decision 4): trimmed and
// lower-cased, so the same address always maps to the same account and the
// platform-wide unique index on Users.Email compares like
// with like.
//
// Validation is deliberately conservative. It accepts the plain
// local-part@domain form that account creation needs: printable ASCII only, no
// quoted local parts, comments, display names or IP-literal domains. It is a
// syntax check only and proves nothing about ownership of the address
// (ADR-010 decision 4).
public static class EmailAddress
{
    // Users.Email column length (UserConfiguration).
    public const int MaximumLength = 256;

    private const int MaximumLocalPartLength = 64;

    // Characters RFC 5322 allows in an unquoted local part besides letters,
    // digits and dots.
    private const string LocalPartSymbols = "!#$%&'*+/=?^_`{|}~-";

    // Returns false - and an empty normalized value - when email is not a
    // well-formed address.
    public static bool TryNormalize(string? email, out string normalized)
    {
        normalized = string.Empty;

        if (email is null)
        {
            return false;
        }

        var candidate = email.Trim().ToLowerInvariant();

        if (candidate.Length == 0 || candidate.Length > MaximumLength)
        {
            return false;
        }

        var at = candidate.IndexOf('@');

        if (at <= 0 || at != candidate.LastIndexOf('@') || at == candidate.Length - 1)
        {
            return false;
        }

        var localPart = candidate[..at];
        var domain = candidate[(at + 1)..];

        if (!IsValidLocalPart(localPart) || !IsValidDomain(domain))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    private static bool IsValidLocalPart(string localPart) =>
        localPart.Length <= MaximumLocalPartLength
        && HasValidDots(localPart)
        && localPart.All(c => IsAsciiLetterOrDigit(c) || c == '.' || LocalPartSymbols.Contains(c));

    // Dot-separated labels of letters, digits and inner hyphens, with at least
    // two labels (a top-level domain is required).
    private static bool IsValidDomain(string domain) =>
        domain.Contains('.')
        && HasValidDots(domain)
        && domain.Split('.').All(label =>
            label.Length <= 63
            && label[0] != '-'
            && label[^1] != '-'
            && label.All(c => IsAsciiLetterOrDigit(c) || c == '-'));

    // No leading, trailing or consecutive dots.
    private static bool HasValidDots(string part) =>
        part[0] != '.' && part[^1] != '.' && !part.Contains("..", StringComparison.Ordinal);

    private static bool IsAsciiLetterOrDigit(char c) => char.IsAsciiLetterOrDigit(c);
}
