using DentalLoyalty.Infrastructure.Security;
using Xunit;

namespace DentalLoyalty.UnitTests.Infrastructure;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Hash_ProducesNonPlaintextHash()
    {
        const string password = "Sup3rSecret!";

        var hash = _hasher.Hash(password);

        Assert.NotEqual(password, hash);
        Assert.False(string.IsNullOrWhiteSpace(hash));
    }

    [Fact]
    public void Verify_WithCorrectPassword_ReturnsTrue()
    {
        const string password = "Sup3rSecret!";
        var hash = _hasher.Hash(password);

        Assert.True(_hasher.Verify(password, hash));
    }

    [Fact]
    public void Verify_WithIncorrectPassword_ReturnsFalse()
    {
        const string password = "Sup3rSecret!";
        var hash = _hasher.Hash(password);

        Assert.False(_hasher.Verify("WrongPassword!", hash));
    }
}
