namespace DietPlanner.Services.Contracts;

public interface IReportService
{
    Task<string> GenerateUserReportAsync(Guid userId, CancellationToken cancellationToken = default);
}