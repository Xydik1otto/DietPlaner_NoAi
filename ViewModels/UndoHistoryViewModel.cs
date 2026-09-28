using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DietPlanner.Common;
using DietPlanner.Data;
using DietPlanner.Services.Contracts;
using Microsoft.EntityFrameworkCore;

namespace DietPlanner.ViewModels;

public class ActionRecordDisplayDto
{
    public DateTime Timestamp { get; set; }
    public string ActionType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
}

public partial class UndoHistoryViewModel : ViewModelBase
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly IAuthenticationService _auth;
    private readonly IUndoService _undoService;
    private readonly ILocalizationService _loc;

    [ObservableProperty] private ObservableCollection<ActionRecordDisplayDto> _historyItems = new();
    [ObservableProperty] private string _statusMessage = string.Empty;

    public UndoHistoryViewModel(
        IDbContextFactory<AppDbContext> dbFactory,
        IAuthenticationService auth,
        IUndoService undoService,
        ILocalizationService loc)
    {
        _dbFactory = dbFactory;
        _auth = auth;
        _undoService = undoService;
        _loc = loc;
    }

    [RelayCommand]
    public async Task LoadHistoryAsync(CancellationToken cancellationToken = default)
    {
        var user = _auth.CurrentUser;
        if (user == null) return;

        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);

        var records = await db.ActionRecords
            .AsNoTracking()
            .Where(r => r.UserId == user.Id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        var dtos = records.Select(r => new ActionRecordDisplayDto
        {
            Timestamp = r.CreatedAtUtc.ToLocalTime(),
            ActionType = r.ActionType.ToString(),
            Description = r.Description,
            EntityName = string.IsNullOrWhiteSpace(r.EntityName) ? _loc.GetString("Undo_GeneralEntity") : r.EntityName
        }).ToList();

        HistoryItems = new ObservableCollection<ActionRecordDisplayDto>(dtos);
    }

    [RelayCommand]
    private async Task UndoAsync(CancellationToken cancellationToken = default)
    {
        var user = _auth.CurrentUser;
        if (user == null) return;

        await _undoService.UndoLastActionAsync();
        StatusMessage = _loc.GetString("Undo_SuccessMessage");
        await LoadHistoryAsync(cancellationToken);
    }
}