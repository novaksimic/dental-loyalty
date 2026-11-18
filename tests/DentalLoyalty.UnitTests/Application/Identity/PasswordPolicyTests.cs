using DentalLoyalty.Application.Identity;

namespace DentalLoyalty.UnitTests.Application.Identity;

// ADR-010 decision 5 (SECURITY.md §8.3): 10-128 characters, no composition
// rules, and not the account's email.
public class PasswordPolicyTests
{
    private const string Email = "ana@example.com";

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void Length_MustBeBetween10And128(int length, bool satisfied)
    {
        Assert.Equal(satisfied, PasswordPolicy.IsSatisfiedBy(new string('x', length), Email));
    }

    // No composition rules: letters only, digits only, spaces and any
    // characters are all allowed.
    [Theory]
    [InlineData("aaaaaaaaaa")]
    [InlineData("1234567890")]
    [InlineData("correct horse battery staple")]
    [InlineData("lozinka-šđčćž")]
    public void AnyCharacters_AreAllowed(string password)
    {
        Assert.True(PasswordPolicy.IsSatisfiedBy(password, Email));
    }

    // Length counts characters, not UTF-16 code units: 10 emoji are 20 code
    // units but 10 characters.
    [Fact]
    public void Length_CountsCharacters_NotCodeUnits()
    {
        var tenEmoji = string.Concat(Enumerable.Repeat("😀", 10));
        var nineEmoji = string.Concat(Enumerable.Repeat("😀", 9));
        var hundredTwentyEightEmoji = string.Concat(Enumerable.Repeat("😀", 128));

        Assert.Equal(20, tenEmoji.Length);
        Assert.True(PasswordPolicy.IsSatisfiedBy(tenEmoji, Email));
        Assert.False(PasswordPolicy.IsSatisfiedBy(nineEmoji, Email));
        Assert.True(PasswordPolicy.IsSatisfiedBy(hundredTwentyEightEmoji, Email));
    }

    [Theory]
    [InlineData("ana@example.com")]
    [InlineData("ANA@EXAMPLE.COM")]
    [InlineData("  Ana@Example.com  ")]
    public void Password_EqualToTheEmail_IsRejected(string password)
    {
        Assert.False(PasswordPolicy.IsSatisfiedBy(password, Email));
    }

    // Containing the email is not equal to it.
    [Fact]
    public void Password_ContainingTheEmail_IsAllowed()
    {
        Assert.True(PasswordPolicy.IsSatisfiedBy("ana@example.com!", Email));
    }

    [Fact]
    public void NullPassword_IsRejected()
    {
        Assert.False(PasswordPolicy.IsSatisfiedBy(null, Email));
    }

    // Whitespace is part of the password, not trimmed away for the length.
    [Fact]
    public void Whitespace_CountsTowardsTheLength()
    {
        Assert.True(PasswordPolicy.IsSatisfiedBy("     abcde", Email));
    }
}
