namespace DietPlanner.Models;

public sealed class ActionRecord
{
    private ActionRecord()
    {
    }

    public ActionRecord(
        Guid? userId,
        ActionType actionType,
        string description,
        string? entityName = null,
        Guid? entityId = null,
        string? beforeJson = null,
        string? afterJson = null)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        ActionType = actionType;
        Description = description.Trim();
        EntityName = string.IsNullOrWhiteSpace(entityName) ? string.Empty : entityName.Trim();
        EntityId = entityId;
        BeforeJson = beforeJson;
        AfterJson = afterJson;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid? UserId { get; private set; }
    public User? User { get; private set; }
    public ActionType ActionType { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public string EntityName { get; private set; } = string.Empty;
    public Guid? EntityId { get; private set; }
    public string? BeforeJson { get; private set; }
    public string? AfterJson { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
}