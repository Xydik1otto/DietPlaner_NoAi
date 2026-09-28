using DietPlanner.Models;

namespace DietPlanner.Services.Contracts;

public interface IUndoService
{
    void RecordAction(ActionRecord record);
    Task<List<ActionRecord>> GetHistoryStackAsync();
    Task<bool> UndoLastActionAsync();
}