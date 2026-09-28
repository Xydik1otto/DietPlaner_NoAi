namespace DietPlanner.Models;

public sealed class UserSessionToken
{
    public Guid UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}