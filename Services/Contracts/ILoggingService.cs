using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public interface ILoggingService
{
    Task LogActionAsync(
        Guid? userId,
        ActionType actionType,
        string description,
        string? entityName = null,
        Guid? entityId = null,
        string? beforeJson = null,
        string? afterJson = null,
        CancellationToken cancellationToken = default);

    Task LogErrorAsync(
        Guid? userId,
        Exception exception,
        string context,
        CancellationToken cancellationToken = default);
}