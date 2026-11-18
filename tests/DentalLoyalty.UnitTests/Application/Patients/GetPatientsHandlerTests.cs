using DentalLoyalty.Application.Patients.Queries;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.UnitTests.TestDoubles;

namespace DentalLoyalty.UnitTests.Application.Patients;

// GetPatientsHandler: paging and search validation, the tenant passed to the
// repository, the page metadata and the list item it returns.
public class GetPatientsHandlerTests
{
    private readonly Guid _clinicId = Guid.NewGuid();
    private readonly InMemoryPatientRepository _repository = new();

    private GetPatientsHandler Handler(FakeCurrentTenant? tenant = null) =>
        new(_repository, tenant ?? FakeCurrentTenant.ForClinic(_clinicId));

    private Patient Seed(string lastName, Guid? clinicId = null)
    {
        var patient = new Patient(clinicId ?? _clinicId, "Ana", lastName, "+381600000000", $"{lastName}@mail.test");
        _repository.Seed(patient);
        return patient;
    }

    [Fact]
    public async Task WithoutTenant_IsRejected_AndReadsNothing()
    {
        Seed("Petrovic");

        var result = await Handler(FakeCurrentTenant.NoTenant()).HandleAsync(new GetPatientsQuery());

        Assert.Equal(GetPatientsStatus.NoTenant, result.Status);
        Assert.Null(_repository.LastListCall);
    }

    [Fact]
    public async Task UsesTheCallersClinic_AndPagesFromDefaults()
    {
        await Handler().HandleAsync(new GetPatientsQuery());

        var call = _repository.LastListCall!.Value;
        Assert.Equal(_clinicId, call.ClinicId);
        Assert.Equal(0, call.Skip);
        Assert.Equal(GetPatientsQuery.DefaultPageSize, call.Take);
        Assert.Empty(call.Terms);
        Assert.Null(call.IsActive);
    }

    [Fact]
    public async Task SplitsAndTrimsSearchTerms_AndComputesSkip()
    {
        await Handler().HandleAsync(new GetPatientsQuery("  ana \t petrovic  ", IsActive: false, Page: 3, PageSize: 10));

        var call = _repository.LastListCall!.Value;
        Assert.Equal(new[] { "ana", "petrovic" }, call.Terms);
        Assert.False(call.IsActive);
        Assert.Equal(20, call.Skip);
        Assert.Equal(10, call.Take);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, -5)]
    [InlineData(1, GetPatientsQuery.MaximumPageSize + 1)]
    [InlineData(int.MaxValue, GetPatientsQuery.MaximumPageSize)]
    public async Task InvalidPaging_IsRejected_AndReadsNothing(int page, int pageSize)
    {
        var result = await Handler().HandleAsync(new GetPatientsQuery(Page: page, PageSize: pageSize));

        Assert.Equal(GetPatientsStatus.InvalidPaging, result.Status);
        Assert.Null(_repository.LastListCall);
    }

    [Fact]
    public async Task MaximumPageSize_IsAccepted()
    {
        var result = await Handler().HandleAsync(new GetPatientsQuery(PageSize: GetPatientsQuery.MaximumPageSize));

        Assert.Equal(GetPatientsStatus.Found, result.Status);
    }

    [Fact]
    public async Task SearchTooLong_IsRejected()
    {
        var result = await Handler().HandleAsync(new GetPatientsQuery(new string('a', GetPatientsQuery.MaximumSearchLength + 1)));

        Assert.Equal(GetPatientsStatus.InvalidSearch, result.Status);
        Assert.Null(_repository.LastListCall);
    }

    [Fact]
    public async Task ReturnsPageMetadata_AndListItems()
    {
        var linked = Seed("A");
        linked.LinkUser(Guid.NewGuid());
        Seed("B");
        Seed("C");
        Seed("Other clinic", Guid.NewGuid());

        var result = await Handler().HandleAsync(new GetPatientsQuery(Page: 1, PageSize: 2));

        var patients = result.Patients!;
        Assert.Equal(GetPatientsStatus.Found, result.Status);
        Assert.Equal(1, patients.Page);
        Assert.Equal(2, patients.PageSize);
        Assert.Equal(3, patients.TotalCount);
        Assert.Equal(2, patients.TotalPages);
        Assert.Equal(new[] { "A", "B" }, patients.Items.Select(p => p.LastName));
        Assert.True(patients.Items[0].HasAccount);
        Assert.False(patients.Items[1].HasAccount);
        Assert.Equal("A@mail.test", patients.Items[0].Email);
    }

    [Fact]
    public async Task NoPatients_IsAnEmptyPage_WithZeroPages()
    {
        var result = await Handler().HandleAsync(new GetPatientsQuery());

        Assert.Equal(GetPatientsStatus.Found, result.Status);
        Assert.Empty(result.Patients!.Items);
        Assert.Equal(0, result.Patients.TotalCount);
        Assert.Equal(0, result.Patients.TotalPages);
    }
}
