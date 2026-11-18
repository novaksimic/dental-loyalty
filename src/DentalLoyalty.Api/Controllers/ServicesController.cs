using System.ComponentModel.DataAnnotations;
using DentalLoyalty.Application.Catalog.Commands;
using DentalLoyalty.Application.Catalog.Queries;
using DentalLoyalty.Domain.Entities;
using DentalLoyalty.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DentalLoyalty.Api.Controllers;

// The clinic's service catalog (BUSINESS.md §15, §18 "manage services and
// prices", §21: a clinic manages only its own services).
//
// ClinicAdmin only. No request carries a clinic id: every read and write is of
// the caller's own clinic, resolved from the authenticated tenant
// (ICurrentTenant), so a service of another clinic is indistinguishable from
// none (404). PlatformAdmin is not granted these, as for loyalty configuration:
// it has no clinic of its own and no catalog use case.
//
// Services are never deleted - history refers to them. Deactivation keeps a
// service in the catalog and in history but stops it from being recorded for a
// patient; activation reverses it.
[ApiController]
[Route("api/services")]
[Authorize(Roles = nameof(UserRole.ClinicAdmin))]
public class ServicesController : ControllerBase
{
    private readonly GetServicesHandler _getServicesHandler;
    private readonly GetServiceByIdHandler _getServiceByIdHandler;
    private readonly CreateServiceHandler _createServiceHandler;
    private readonly UpdateServiceHandler _updateServiceHandler;
    private readonly SetServiceActiveHandler _setServiceActiveHandler;

    public ServicesController(
        GetServicesHandler getServicesHandler,
        GetServiceByIdHandler getServiceByIdHandler,
        CreateServiceHandler createServiceHandler,
        UpdateServiceHandler updateServiceHandler,
        SetServiceActiveHandler setServiceActiveHandler)
    {
        _getServicesHandler = getServicesHandler;
        _getServiceByIdHandler = getServiceByIdHandler;
        _createServiceHandler = createServiceHandler;
        _updateServiceHandler = updateServiceHandler;
        _setServiceActiveHandler = setServiceActiveHandler;
    }

    // isActive filters by status; omitted, active and inactive services are listed.
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool? isActive,
        CancellationToken cancellationToken)
    {
        var services = await _getServicesHandler.HandleAsync(
            new GetServicesQuery(isActive),
            cancellationToken);

        if (services is null)
        {
            return Forbid();
        }

        return Ok(services.Select(ToResponse));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var service = await _getServiceByIdHandler.HandleAsync(
            new GetServiceByIdQuery(id),
            cancellationToken);

        return service is null ? NotFound() : Ok(ToResponse(service));
    }

    [HttpPost]
    public async Task<IActionResult> Create(
        ServiceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _createServiceHandler.HandleAsync(
            new CreateServiceCommand(request.Name, request.Price!.Value),
            cancellationToken);

        if (result.Status == ServiceCommandStatus.Saved)
        {
            var service = result.Service!;

            return CreatedAtAction(nameof(GetById), new { id = service.Id }, ToResponse(service));
        }

        return Rejected(result.Status);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id,
        ServiceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _updateServiceHandler.HandleAsync(
            new UpdateServiceCommand(id, request.Name, request.Price!.Value),
            cancellationToken);

        return result.Status == ServiceCommandStatus.Saved ? NoContent() : Rejected(result.Status);
    }

    [HttpPost("{id:guid}/deactivation")]
    public Task<IActionResult> Deactivate(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, isActive: false, cancellationToken);

    [HttpPost("{id:guid}/activation")]
    public Task<IActionResult> Activate(Guid id, CancellationToken cancellationToken) =>
        SetActiveAsync(id, isActive: true, cancellationToken);

    private async Task<IActionResult> SetActiveAsync(Guid id, bool isActive, CancellationToken cancellationToken)
    {
        var found = await _setServiceActiveHandler.HandleAsync(
            new SetServiceActiveCommand(id, isActive),
            cancellationToken);

        return found ? NoContent() : NotFound();
    }

    private IActionResult Rejected(ServiceCommandStatus status)
    {
        switch (status)
        {
            case ServiceCommandStatus.InvalidName:
                ModelState.AddModelError(
                    nameof(ServiceRequest.Name),
                    $"The name is required and can be at most {Service.MaximumNameLength} characters.");
                return ValidationProblem(ModelState);

            case ServiceCommandStatus.InvalidPrice:
                ModelState.AddModelError(
                    nameof(ServiceRequest.Price),
                    $"The price must be between 0 and {Service.MaximumPrice} RSD with at most {Service.PriceDecimalPlaces} decimal places.");
                return ValidationProblem(ModelState);

            case ServiceCommandStatus.NotFound:
                return NotFound();

            case ServiceCommandStatus.NoTenant:
                return Forbid();

            default:
                throw new InvalidOperationException($"Unexpected result {status}.");
        }
    }

    private static object ToResponse(Service service) => new
    {
        id = service.Id,
        name = service.Name,
        price = service.Price,
        isActive = service.IsActive,
    };
}

// Price is in RSD. It is nullable only so that a missing price is rejected (400)
// instead of silently binding to 0 - a valid price for a free service.
public sealed record ServiceRequest(string Name, [Required] decimal? Price);
