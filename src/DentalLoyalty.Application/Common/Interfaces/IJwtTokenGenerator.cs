using DentalLoyalty.Domain.Enums;

namespace DentalLoyalty.Application.Common.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateToken(
        Guid userId,
        UserRole role,
        Guid? clinicId);
}
