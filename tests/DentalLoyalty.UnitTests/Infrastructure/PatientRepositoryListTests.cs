using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Infrastructure.Persistence;
using DentalLoyalty.Infrastructure.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DentalLoyalty.UnitTests.Infrastructure;

// PatientRepository.ListForTenantAsync translated to SQL on a relational engine
// (SQLite): the clinic filter, search, status filter, ordering, count and paging
// all run in the database. SQLite's LIKE is case-insensitive for ASCII, as SQL
// Server's default collation is for all text.
public sealed class PatientRepositoryListTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public PatientRepositoryListTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private DentalLoyaltyDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<DentalLoyaltyDbContext>().UseSqlite(_connection).Options);

    private async Task<Guid> SeedClinicAsync(string name = "Clinic A")
    {
        await using var context = CreateContext();
        var clinic = new Clinic(name);
        context.Clinics.Add(clinic);
        await context.SaveChangesAsync();
        return clinic.Id;
    }

    private async Task<Patient> SeedPatientAsync(
        Guid clinicId,
        string firstName,
        string lastName,
        string phone = "+381600000000",
        string? email = null,
        bool active = true)
    {
        await using var context = CreateContext();
        var patient = new Patient(clinicId, firstName, lastName, phone, email);

        if (!active)
        {
            patient.Deactivate();
        }

        context.Patients.Add(patient);
        await context.SaveChangesAsync();
        return patient;
    }

    private async Task<PatientPage> ListAsync(
        Guid clinicId,
        string[]? terms = null,
        bool? isActive = null,
        int skip = 0,
        int take = 100)
    {
        await using var context = CreateContext();
        return await new PatientRepository(context).ListForTenantAsync(clinicId, terms ?? [], isActive, skip, take);
    }

    private static string[] LastNames(PatientPage page) => page.Items.Select(p => p.LastName).ToArray();

    [Fact]
    public async Task ReturnsOnlyTheClinicsPatients_OrderedByLastThenFirstName()
    {
        var clinicA = await SeedClinicAsync("Clinic A");
        var clinicB = await SeedClinicAsync("Clinic B");
        await SeedPatientAsync(clinicA, "Marko", "Petrovic");
        await SeedPatientAsync(clinicA, "Ana", "Petrovic");
        await SeedPatientAsync(clinicA, "Jovan", "Ilic");
        await SeedPatientAsync(clinicB, "Ana", "Other");

        var page = await ListAsync(clinicA);

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(new[] { "Ilic", "Petrovic", "Petrovic" }, LastNames(page));
        Assert.Equal(new[] { "Jovan", "Ana", "Marko" }, page.Items.Select(p => p.FirstName));
        Assert.All(page.Items, p => Assert.Equal(clinicA, p.ClinicId));
    }

    [Fact]
    public async Task PagesInTheDatabase_WithTheFullCount()
    {
        var clinicId = await SeedClinicAsync();

        foreach (var name in new[] { "A", "B", "C", "D", "E" })
        {
            await SeedPatientAsync(clinicId, "Ana", name);
        }

        var second = await ListAsync(clinicId, skip: 2, take: 2);
        var beyond = await ListAsync(clinicId, skip: 10, take: 2);

        Assert.Equal(5, second.TotalCount);
        Assert.Equal(new[] { "C", "D" }, LastNames(second));
        Assert.Equal(5, beyond.TotalCount);
        Assert.Empty(beyond.Items);
    }

    [Theory]
    [InlineData("jovana")]
    [InlineData("JOVANA")]
    [InlineData("nikol")]
    [InlineData("NIKOLIC")]
    [InlineData("jn@mail")]
    [InlineData("JN@MAIL.TEST")]
    [InlineData("641112")]
    public async Task Search_MatchesEachField_CaseInsensitively(string term)
    {
        var clinicId = await SeedClinicAsync();
        await SeedPatientAsync(clinicId, "Jovana", "Nikolic", "+381641112233", "jn@mail.test");
        await SeedPatientAsync(clinicId, "Marko", "Markovic", "+381609998877", "marko@mail.test");

        var page = await ListAsync(clinicId, [term]);

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Nikolic", Assert.Single(page.Items).LastName);
    }

    // Several terms narrow the result: every term must match some field.
    [Fact]
    public async Task Search_WithSeveralTerms_RequiresEveryTerm()
    {
        var clinicId = await SeedClinicAsync();
        await SeedPatientAsync(clinicId, "Ana", "Petrovic");
        await SeedPatientAsync(clinicId, "Ana", "Ilic");
        await SeedPatientAsync(clinicId, "Marko", "Petrovic");

        var page = await ListAsync(clinicId, ["ana", "petrovic"]);

        var patient = Assert.Single(page.Items);
        Assert.Equal(("Ana", "Petrovic"), (patient.FirstName, patient.LastName));
    }

    // A patient without an email is still found by its other fields.
    [Fact]
    public async Task Search_PatientWithoutEmail_IsFoundByName()
    {
        var clinicId = await SeedClinicAsync();
        await SeedPatientAsync(clinicId, "Ana", "Petrovic", email: null);

        Assert.Equal(1, (await ListAsync(clinicId, ["petro"])).TotalCount);
    }

    // LIKE wildcards in the input match only themselves.
    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("[a]")]
    [InlineData("\\")]
    public async Task Search_WildcardCharacters_MatchLiterally(string term)
    {
        var clinicId = await SeedClinicAsync();
        await SeedPatientAsync(clinicId, "Ana", "Petrovic", email: "ana@mail.test");
        await SeedPatientAsync(clinicId, "Odd", $"Name{term}Here");

        var page = await ListAsync(clinicId, [term]);

        Assert.Equal($"Name{term}Here", Assert.Single(page.Items).LastName);
    }

    [Fact]
    public async Task FiltersByStatus()
    {
        var clinicId = await SeedClinicAsync();
        await SeedPatientAsync(clinicId, "Ana", "Active");
        await SeedPatientAsync(clinicId, "Ana", "Inactive", active: false);

        Assert.Equal(new[] { "Active" }, LastNames(await ListAsync(clinicId, isActive: true)));
        Assert.Equal(new[] { "Inactive" }, LastNames(await ListAsync(clinicId, isActive: false)));
        Assert.Equal(2, (await ListAsync(clinicId)).TotalCount);
    }

    [Fact]
    public async Task NoMatches_IsAnEmptyPage()
    {
        var clinicId = await SeedClinicAsync();
        await SeedPatientAsync(clinicId, "Ana", "Petrovic");

        var page = await ListAsync(clinicId, ["nobody"]);

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }

    // Another clinic's patient never matches, whatever the search.
    [Fact]
    public async Task Search_NeverReachesAnotherClinic()
    {
        var clinicA = await SeedClinicAsync("Clinic A");
        var clinicB = await SeedClinicAsync("Clinic B");
        await SeedPatientAsync(clinicB, "Ana", "Petrovic", email: "ana@b.test");

        Assert.Equal(0, (await ListAsync(clinicA, ["ana"])).TotalCount);
    }

    [Fact]
    public async Task ReturnedPatients_AreNotTracked()
    {
        var clinicId = await SeedClinicAsync();
        await SeedPatientAsync(clinicId, "Ana", "Petrovic");

        await using var context = CreateContext();
        await new PatientRepository(context).ListForTenantAsync(clinicId, [], null, 0, 10);

        Assert.Empty(context.ChangeTracker.Entries());
    }
}
