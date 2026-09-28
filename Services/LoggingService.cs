using System.IO;
using System.Text;
using DietPlanner.Data;
using DietPlanner.Infrastructure;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Services;

public sealed class LoggingService : ILoggingService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAppPaths _paths;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public LoggingService(IDbContextFactory<AppDbContext> dbFactory, IAppPaths paths)
    {
        _dbFactory = dbFactory;
        _paths = paths;
    }

    public async Task LogActionAsync(
        Guid? userId,
        ActionType actionType,
        string description,
        string? entityName = null,
        Guid? entityId = null,
        string? beforeJson = null,
        string? afterJson = null,
        CancellationToken cancellationToken = default)
    {
        var suffix = entityName is null ? string.Empty : $" entity={entityName} id={entityId}";
        await WriteAsync($"ACTION time={DateTime.UtcNow:O} user={userId?.ToString() ?? "anonymous"} type={actionType} description={description}{suffix}\n", cancellationToken);

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var record = new ActionRecord(
                userId,
                actionType,
                description,
                entityName,
                entityId,
                beforeJson,
                afterJson);

            db.ActionRecords.Add(record);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Помилка запису дії в БД: {exception}");
        }
    }

    public async Task LogErrorAsync(Guid? userId, Exception exception, string context, CancellationToken cancellationToken = default)
    {
        await WriteAsync($"ERROR time={DateTime.UtcNow:O} user={userId?.ToString() ?? "anonymous"} context={context}\n{exception}\n", cancellationToken);
    }

    private async Task WriteAsync(string content, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_paths.LogsDirectory);
            var path = Path.Combine(_paths.LogsDirectory, $"app_{DateTime.UtcNow:yyyy-MM-dd}.log");
            await _lock.WaitAsync(cancellationToken);
            try
            {
                await File.AppendAllTextAsync(path, content, Encoding.UTF8, cancellationToken);
            }
            finally
            {
                _lock.Release();
            }
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Logging failure: {exception}");
        }
    }
}