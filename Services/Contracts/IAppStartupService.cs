namespace DietPlanner.Services.Contracts;

public interface IAppStartupService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
