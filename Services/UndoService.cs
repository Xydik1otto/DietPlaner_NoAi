using System.Text.Json;
using DietPlanner.Data;
using DietPlanner.Models;
using DietPlanner.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.Services;

public class UndoService : IUndoService
{
    private readonly AppDbContext _db;
    private readonly Stack<ActionRecord> _actionStack = new();

    public UndoService(AppDbContext db)
    {
        _db = db;
    }

    public void RecordAction(ActionRecord record)
    {
        _actionStack.Push(record);
        _db.ActionRecords.Add(record);
        _db.SaveChanges();
    }

    public async Task<List<ActionRecord>> GetHistoryStackAsync()
    {
        var records = await _db.ActionRecords
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(50)
            .ToListAsync();

        _actionStack.Clear();
        foreach (var r in records.AsEnumerable().Reverse())
        {
            _actionStack.Push(r);
        }

        return records;
    }

    public async Task<bool> UndoLastActionAsync()
    {
        if (_actionStack.Count == 0)
        {
            var lastFromDb = await _db.ActionRecords
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefaultAsync();

            if (lastFromDb == null) return false;
            _actionStack.Push(lastFromDb);
        }

        var lastAction = _actionStack.Pop();

        try
        {
            switch (lastAction.EntityName)
            {
                case nameof(Product):
                    await RestoreProductStateAsync(lastAction);
                    break;
                case nameof(Dish):
                    await RestoreDishStateAsync(lastAction);
                    break;
                case nameof(MealIntake):
                    await RestoreMealIntakeStateAsync(lastAction);
                    break;
            }

            _db.ActionRecords.Remove(lastAction);
            await _db.SaveChangesAsync();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task RestoreProductStateAsync(ActionRecord record)
    {
        if (record.ActionType == ActionType.Delete && !string.IsNullOrEmpty(record.BeforeJson))
        {
            var product = JsonSerializer.Deserialize<Product>(record.BeforeJson);
            if (product != null) _db.Products.Add(product);
        }
        else if (record.ActionType == ActionType.Create && record.EntityId.HasValue)
        {
            var product = await _db.Products.FindAsync(record.EntityId.Value);
            if (product != null) _db.Products.Remove(product);
        }
    }

    private async Task RestoreDishStateAsync(ActionRecord record)
    {
        if (record.ActionType == ActionType.Delete && !string.IsNullOrEmpty(record.BeforeJson))
        {
            var dish = JsonSerializer.Deserialize<Dish>(record.BeforeJson);
            if (dish != null) _db.Dishes.Add(dish);
        }
        else if (record.ActionType == ActionType.Create && record.EntityId.HasValue)
        {
            var dish = await _db.Dishes.FindAsync(record.EntityId.Value);
            if (dish != null) _db.Dishes.Remove(dish);
        }
    }

    private async Task RestoreMealIntakeStateAsync(ActionRecord record)
    {
        if (record.ActionType == ActionType.Create && record.EntityId.HasValue)
        {
            var intake = await _db.MealIntakes.FindAsync(record.EntityId.Value);
            if (intake != null) _db.MealIntakes.Remove(intake);
        }
    }
}