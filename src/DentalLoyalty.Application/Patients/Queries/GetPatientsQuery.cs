using DentalLoyalty.Application.Common.Paging;

namespace DentalLoyalty.Application.Patients.Queries;

// Search matches first name, last name, email or phone number; with several
// whitespace-separated terms, every term must match one of them. Null or blank
// means no search. IsActive filters by status when set.
public sealed record GetPatientsQuery(
    string? Search = null,
    bool? IsActive = null,
    int Page = GetPatientsQuery.DefaultPage,
    int PageSize = GetPatientsQuery.DefaultPageSize)
{
    public const int DefaultPage = 1;

    public const int DefaultPageSize = 20;

    // BACKEND & API ARCHITECTURE.md §40: no unreasonable page sizes (as for
    // loyalty transactions).
    public const int MaximumPageSize = 100;

    public const int MaximumSearchLength = 100;
}

// A patient as the clinic's patient list shows it. HasAccount tells whether a
// user account is linked (POST /api/patients/{id}/account); the account itself
// is not exposed.
public sealed record PatientListItemDto(
    Guid Id,
    string FirstName,
    string LastName,
    string PhoneNumber,
    string? Email,
    bool IsActive,
    bool HasAccount);

public enum GetPatientsStatus
{
    Found = 1,
    InvalidPaging = 2,
    InvalidSearch = 3,

    // The caller has no trusted clinic (tenant).
    NoTenant = 4,
}

public sealed record GetPatientsResult(
    GetPatientsStatus Status,
    PagedResult<PatientListItemDto>? Patients = null);
