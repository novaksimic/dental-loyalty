using DentalLoyalty.Application.Identity;

namespace DentalLoyalty.UnitTests.Application.Identity;

// ADR-010 decision 4: account emails are trimmed and lower-cased, and only the
// plain local-part@domain form is accepted.
public class EmailAddressTests
{
    [Theory]
    [InlineData("ana@example.com", "ana@example.com")]
    [InlineData("  Ana.Petrovic@Example.COM  ", "ana.petrovic@example.com")]
    [InlineData("ana+loyalty@mail.example.rs", "ana+loyalty@mail.example.rs")]
    [InlineData("o'brien_1-x@my-clinic.co.rs", "o'brien_1-x@my-clinic.co.rs")]
    public void WellFormedAddress_IsNormalized(string input, string expected)
    {
        Assert.True(EmailAddress.TryNormalize(input, out var normalized));
        Assert.Equal(expected, normalized);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ana")]
    [InlineData("@example.com")]
    [InlineData("ana@")]
    [InlineData("ana@@example.com")]
    [InlineData("ana@b@example.com")]
    [InlineData("ana@localhost")]
    [InlineData("ana @example.com")]
    [InlineData("ana@exa mple.com")]
    [InlineData(".ana@example.com")]
    [InlineData("ana.@example.com")]
    [InlineData("an..a@example.com")]
    [InlineData("ana@example..com")]
    [InlineData("ana@.example.com")]
    [InlineData("ana@example.com.")]
    [InlineData("ana@-example.com")]
    [InlineData("ana@example-.com")]
    [InlineData("\"ana\"@example.com")]
    [InlineData("Ana <ana@example.com>")]
    [InlineData("ana@[127.0.0.1]")]
    [InlineData("ana@exam_ple.com")]
    [InlineData("ana\t@example.com")]
    [InlineData("ána@example.com")]
    public void MalformedAddress_IsRejected(string? input)
    {
        Assert.False(EmailAddress.TryNormalize(input, out var normalized));
        Assert.Equal(string.Empty, normalized);
    }

    // The column length (256) and the RFC limits on a local part (64) and a
    // domain label (63).
    [Fact]
    public void LengthLimits_AreEnforced()
    {
        var label63 = new string('d', 63);

        Assert.True(EmailAddress.TryNormalize($"{new string('a', 64)}@example.com", out _));
        Assert.False(EmailAddress.TryNormalize($"{new string('a', 65)}@example.com", out _));
        Assert.True(EmailAddress.TryNormalize($"ana@{label63}.com", out _));
        Assert.False(EmailAddress.TryNormalize($"ana@{label63}d.com", out _));

        // 64 + "@" + 191 (three 63-character labels and two dots) = 256.
        var atMaximum = $"{new string('a', 64)}@{label63}.{label63}.{label63}";
        Assert.Equal(EmailAddress.MaximumLength, atMaximum.Length);
        Assert.True(EmailAddress.TryNormalize(atMaximum, out _));
        // Every part within its own limit, only the total too long.
        Assert.False(EmailAddress.TryNormalize($"{atMaximum}.d", out _));
    }

    // Surrounding whitespace does not count towards the length limit.
    [Fact]
    public void SurroundingWhitespace_IsNotCountedTowardsTheLimit()
    {
        var address = $"{new string('a', 64)}@example.com";

        Assert.True(EmailAddress.TryNormalize($"   {address}   ", out var normalized));
        Assert.Equal(address, normalized);
    }
}
