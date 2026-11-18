using DentalLoyalty.Application.Common.Interfaces;
using DentalLoyalty.Application.Common.Paging;

namespace DentalLoyalty.Application.Patients.Queries;

// One page of the caller's own clinic's patients (BUSINESS.md §1 "manage
// patients", §18; SECURITY.md §5.4 "Manage Patients" = Own Clinic), ordered by
// last name, then first name.
//
// The clinic is the authenticated tenant (ICurrentTenant) and is applied by the
// database query itself - no other clinic's patient is ever read. Searching,
// counting and paging also happen in the database.
public sealed class GetPatientsHandler
{
    private readonly IPatientRepository _patientRepository;
    private readonly ICurrentTenant _currentTenant;

    public GetPatientsHandler(
        IPatientRepository patientRepository,
        ICurrentTenant currentTenant)
    {
        _patientRepository = patientRepository;
        _currentTenant = currentTenant;
    }

    public async Task<GetPatientsResult> HandleAsync(
        GetPatientsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!_currentTenant.HasTenant)
        {
            return new GetPatientsResult(GetPatientsStatus.NoTenant);
        }

        // The rows skipped must fit an int; a page that far out is invalid.
        if (query.Page < 1
            || query.PageSize < 1
            || query.PageSize > GetPatientsQuery.MaximumPageSize
            || (long)(query.Page - 1) * query.PageSize > int.MaxValue)
        {
            return new GetPatientsResult(GetPatientsStatus.InvalidPaging);
        }

        var search = query.Search?.Trim() ?? string.Empty;

        if (search.Length > GetPatientsQuery.MaximumSearchLength)
        {
            return new GetPatientsResult(GetPatientsStatus.InvalidSearch);
        }

        var terms = search.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

        var page = await _patientRepository.ListForTenantAsync(
            _currentTenant.ClinicId!.Value,
            terms,
            query.IsActive,
            skip: (query.Page - 1) * query.PageSize,
            take: query.PageSize,
            cancellationToken);

        var items = page.Items
            .Select(p => new PatientListItemDto(
                p.Id,
                p.FirstName,
                p.LastName,
                p.PhoneNumber,
                p.Email,
                p.IsActive,
                HasAccount: p.UserId is not null))
            .ToList();

        return new GetPatientsResult(
            GetPatientsStatus.Found,
            PagedResult<PatientListItemDto>.Create(items, query.Page, query.PageSize, page.TotalCount));
    }
}
