using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public sealed class AuthorizationService : IAuthorizationService
{
    private readonly SessionService _session;

    public AuthorizationService(SessionService session)
    {
        _session = session;
    }

    public bool IsInRole(UserRole role) => _session.CurrentUser?.Role == role;
    public bool CanManageGlobalCatalog => IsInRole(UserRole.Admin);
    public bool CanViewAdminArea => IsInRole(UserRole.Admin);
    public bool CanViewAllUsers => IsInRole(UserRole.Admin);
}
