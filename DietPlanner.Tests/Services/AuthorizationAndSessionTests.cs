using DietPlanner.Models;
using DietPlanner.Services;

namespace DietPlanner.Tests.Services;

public sealed class AuthorizationAndSessionTests
{
    [Fact]
    public void AuthorizationService_AdminCanManageGlobalCatalog()
    {
        var session = new SessionService();
        var authorization = new AuthorizationService(session);
        session.SignIn(new User("admin@example.com", "Admin", "hash", UserRole.Admin));

        Assert.True(authorization.CanManageGlobalCatalog);
        Assert.True(authorization.CanViewAdminArea);
        Assert.True(authorization.CanViewAllUsers);
        Assert.True(authorization.IsInRole(UserRole.Admin));
    }

    [Fact]
    public void AuthorizationService_UserCannotManageGlobalCatalog()
    {
        var session = new SessionService();
        var authorization = new AuthorizationService(session);
        session.SignIn(new User("user@example.com", "User", "hash", UserRole.User));

        Assert.False(authorization.CanManageGlobalCatalog);
        Assert.False(authorization.CanViewAdminArea);
        Assert.False(authorization.CanViewAllUsers);
        Assert.True(authorization.IsInRole(UserRole.User));
    }

    [Fact]
    public void SessionService_SignOutClearsCurrentUser()
    {
        var session = new SessionService();
        var user = new User("user@example.com", "User", "hash", UserRole.User);
        session.SignIn(user);

        Assert.True(session.IsAuthenticated);
        Assert.Same(user, session.CurrentUser);

        session.SignOut();

        Assert.False(session.IsAuthenticated);
        Assert.Null(session.CurrentUser);
    }
}
