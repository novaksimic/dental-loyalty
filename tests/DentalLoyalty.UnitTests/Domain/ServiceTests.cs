using DentalLoyalty.Domain.Entities;

namespace DentalLoyalty.UnitTests.Domain;

// A catalog service's name and price (BUSINESS.md §15): a required name that
// fits its column, and a non-negative price the decimal(18,2) column can hold
// exactly. A price of 0 is allowed.
public class ServiceTests
{
    private static readonly Guid ClinicId = Guid.NewGuid();

    public static TheoryData<decimal> ValidPrices() => new() { 0m, 1_234.56m, Service.MaximumPrice };

    public static TheoryData<decimal> InvalidPrices() => new() { -0.01m, 10.005m, Service.MaximumPrice + 0.01m, decimal.MaxValue };

    [Theory]
    [MemberData(nameof(ValidPrices))]
    public void Create_WithValidValues_IsActive(decimal price)
    {
        var service = new Service(ClinicId, "Kompozitni ispun", price);

        Assert.Equal(ClinicId, service.ClinicId);
        Assert.Equal("Kompozitni ispun", service.Name);
        Assert.Equal(price, service.Price);
        Assert.True(service.IsActive);
        Assert.Null(service.UpdatedAt);
    }

    [Fact]
    public void Create_WithEmptyClinic_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => new Service(Guid.Empty, "Pregled", 100m));

        Assert.Equal("clinicId", exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithoutName_Throws(string? name)
    {
        var exception = Assert.Throws<ArgumentException>(() => new Service(ClinicId, name!, 100m));

        Assert.Equal("name", exception.ParamName);
    }

    [Fact]
    public void Create_WithNameAtMaximumLength_Succeeds_AndOneLongerThrows()
    {
        Assert.Equal(Service.MaximumNameLength, new Service(ClinicId, new string('a', Service.MaximumNameLength), 1m).Name.Length);

        var exception = Assert.Throws<ArgumentException>(() =>
            new Service(ClinicId, new string('a', Service.MaximumNameLength + 1), 1m));

        Assert.Equal("name", exception.ParamName);
    }

    [Theory]
    [MemberData(nameof(InvalidPrices))]
    public void Create_WithInvalidPrice_Throws(decimal price)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new Service(ClinicId, "Pregled", price));

        Assert.Equal("price", exception.ParamName);
    }

    [Fact]
    public void Update_ChangesNameAndPrice()
    {
        var service = new Service(ClinicId, "Pregled", 100m);

        service.Update("Specijalisticki pregled", 2_500m);

        Assert.Equal("Specijalisticki pregled", service.Name);
        Assert.Equal(2_500m, service.Price);
        Assert.NotNull(service.UpdatedAt);
    }

    [Fact]
    public void Update_WithInvalidValues_ChangesNothing()
    {
        var service = new Service(ClinicId, "Pregled", 100m);

        Assert.Throws<ArgumentException>(() => service.Update(" ", 200m));
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Update("Kontrola", -1m));

        Assert.Equal("Pregled", service.Name);
        Assert.Equal(100m, service.Price);
        Assert.Null(service.UpdatedAt);
    }
}
