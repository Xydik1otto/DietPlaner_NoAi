using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DietPlanner.Infrastructure;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public sealed class TokenService : ITokenService
{
    private readonly IAppPaths _appPaths;
    private string SessionDirectory => Path.GetDirectoryName(_appPaths.DatabasePath) ?? AppContext.BaseDirectory;
    private string SessionFilePath => Path.Combine(SessionDirectory, "session.bin");

    public TokenService(IAppPaths appPaths)
    {
        _appPaths = appPaths;
    }

    public async Task SaveSessionAsync(Guid userId, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var token = new UserSessionToken
        {
            UserId = userId,
            Token = Guid.NewGuid().ToString("N"),
            ExpiresAtUtc = DateTime.UtcNow.Add(lifetime)
        };

        var json = JsonSerializer.Serialize(token);
        var plainBytes = Encoding.UTF8.GetBytes(json);
        var encryptedBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);

        Directory.CreateDirectory(SessionDirectory);
        await File.WriteAllBytesAsync(SessionFilePath, encryptedBytes, cancellationToken);
    }

    public async Task<UserSessionToken?> GetValidSessionAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(SessionFilePath)) return null;

        try
        {
            var encryptedBytes = await File.ReadAllBytesAsync(SessionFilePath, cancellationToken);
            var plainBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(plainBytes);
            var session = JsonSerializer.Deserialize<UserSessionToken>(json);

            if (session == null || session.ExpiresAtUtc <= DateTime.UtcNow)
            {
                await ClearSessionAsync(cancellationToken);
                return null;
            }

            return session;
        }
        catch
        {
            await ClearSessionAsync(cancellationToken);
            return null;
        }
    }

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(SessionFilePath))
        {
            File.Delete(SessionFilePath);
        }
        return Task.CompletedTask;
    }
}