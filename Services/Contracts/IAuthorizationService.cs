using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public interface IAuthorizationService
{
    bool IsInRole(UserRole role);
    bool CanManageGlobalCatalog { get; }
    bool CanViewAdminArea { get; }
    bool CanViewAllUsers { get; }
}
