using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using DietPlanner.Common;
using DietPlanner.Services;
using DietPlanner.Services.Contracts;

namespace DietPlanner.ViewModels;

public class DailyStatItem
{
    public DateTime Date { get; set; }
    public string DateDisplay => Date.ToString("dd.MM.yyyy (ddd)");
    public double Calories { get; set; }
    public double Proteins { get; set; }
    public double Fats { get; set; }
    public double Carbs { get; set; }
    public string GoalCompletionStatus { get; set; } = "100%";
}

public class WeekOption
{
    public int WeekNumber { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    
    public string DisplayName => $"Тиждень {WeekNumber} ({StartDate:dd.MM} - {EndDate:dd.MM})";

    public override string ToString() => DisplayName;
}

public class StatisticsViewModel : ViewModelBase
{
    private readonly IStatisticsService _statsService;
    private readonly SessionService _session;

    private bool _isDayMode = true;
    private bool _isWeekMode;
    private bool _isMonthMode;

    private DateTime _selectedDate = DateTime.Today;
    private string _selectedMonth = string.Empty;
    private int _selectedYear = DateTime.Today.Year;
    private WeekOption? _selectedWeek;

    private string _totalCaloriesText = "0 ккал";
    private string _totalProteinsText = "0 г";
    private string _totalFatsText = "0 г";
    private string _totalCarbsText = "0 г";

    private string _calorieGoalDiffText = "Ціль: 2000 ккал";
    private string _proteinGoalText = "Ціль: 150 г";
    private string _fatGoalText = "Ціль: 70 г";
    private string _carbGoalText = "Ціль: 250 г";

    private double _calorieProgressPercent;
    private double _proteinProgressPercent;
    private double _fatProgressPercent;
    private double _carbProgressPercent;

    public bool IsDayMode
    {
        get => _isDayMode;
        set
        {
            if (SetProperty(ref _isDayMode, value) && value)
            {
                _isWeekMode = false;
                _isMonthMode = false;
                OnPropertyChanged(nameof(IsWeekMode));
                OnPropertyChanged(nameof(IsMonthMode));
                _ = LoadDataAsync();
            }
        }
    }

    public bool IsWeekMode
    {
        get => _isWeekMode;
        set
        {
            if (SetProperty(ref _isWeekMode, value) && value)
            {
                _isDayMode = false;
                _isMonthMode = false;
                OnPropertyChanged(nameof(IsDayMode));
                OnPropertyChanged(nameof(IsMonthMode));
                _ = LoadDataAsync();
            }
        }
    }

    public bool IsMonthMode
    {
        get => _isMonthMode;
        set
        {
            if (SetProperty(ref _isMonthMode, value) && value)
            {
                _isDayMode = false;
                _isWeekMode = false;
                OnPropertyChanged(nameof(IsDayMode));
                OnPropertyChanged(nameof(IsWeekMode));
                _ = LoadDataAsync();
            }
        }
    }

    public ObservableCollection<string> MonthsList { get; } = new ObservableCollection<string>
    {
        "Січень", "Лютий", "Березень", "Квітень", "Травень", "Червень",
        "Липень", "Серпень", "Вересень", "Жовтень", "Листопад", "Грудень"
    };

    public ObservableCollection<int> YearsList { get; } = new ObservableCollection<int> { 2025, 2026, 2027 };
    public ObservableCollection<WeekOption> WeeksList { get; } = new ObservableCollection<WeekOption>();
    public ObservableCollection<DailyStatItem> DailyBreakdownList { get; } = new ObservableCollection<DailyStatItem>();

    public DateTime SelectedDate
    {
        get => _selectedDate;
        set { if (SetProperty(ref _selectedDate, value)) _ = LoadDataAsync(); }
    }

    public string SelectedMonth
    {
        get => _selectedMonth;
        set
        {
            if (SetProperty(ref _selectedMonth, value))
            {
                UpdateWeeksList();
                _ = LoadDataAsync();
            }
        }
    }

    public int SelectedYear
    {
        get => _selectedYear;
        set
        {
            if (SetProperty(ref _selectedYear, value))
            {
                UpdateWeeksList();
                _ = LoadDataAsync();
            }
        }
    }

    public WeekOption? SelectedWeek
    {
        get => _selectedWeek;
        set { if (SetProperty(ref _selectedWeek, value)) _ = LoadDataAsync(); }
    }

    public string TotalCaloriesText
    {
        get => _totalCaloriesText;
        set => SetProperty(ref _totalCaloriesText, value);
    }

    public string TotalProteinsText
    {
        get => _totalProteinsText;
        set => SetProperty(ref _totalProteinsText, value);
    }

    public string TotalFatsText
    {
        get => _totalFatsText;
        set => SetProperty(ref _totalFatsText, value);
    }

    public string TotalCarbsText
    {
        get => _totalCarbsText;
        set => SetProperty(ref _totalCarbsText, value);
    }

    public string CalorieGoalDiffText
    {
        get => _calorieGoalDiffText;
        set => SetProperty(ref _calorieGoalDiffText, value);
    }

    public string ProteinGoalText
    {
        get => _proteinGoalText;
        set => SetProperty(ref _proteinGoalText, value);
    }

    public string FatGoalText
    {
        get => _fatGoalText;
        set => SetProperty(ref _fatGoalText, value);
    }

    public string CarbGoalText
    {
        get => _carbGoalText;
        set => SetProperty(ref _carbGoalText, value);
    }

    public double CalorieProgressPercent
    {
        get => _calorieProgressPercent;
        set => SetProperty(ref _calorieProgressPercent, value);
    }

    public double ProteinProgressPercent
    {
        get => _proteinProgressPercent;
        set => SetProperty(ref _proteinProgressPercent, value);
    }

    public double FatProgressPercent
    {
        get => _fatProgressPercent;
        set => SetProperty(ref _fatProgressPercent, value);
    }

    public double CarbProgressPercent
    {
        get => _carbProgressPercent;
        set => SetProperty(ref _carbProgressPercent, value);
    }

    public string PeriodTableTitle => IsDayMode ? "Деталізація за день" : (IsWeekMode ? "Деталізація по днях тижня" : "Деталізація за кожен день місяця");

    public ICommand RefreshStatsCommand { get; }

    public StatisticsViewModel(IStatisticsService statsService, SessionService session)
    {
        _statsService = statsService;
        _session = session;

        _selectedMonth = MonthsList[DateTime.Today.Month - 1];
        UpdateWeeksList();

        RefreshStatsCommand = new AsyncRelayCommand(LoadDataAsync);
        _ = LoadDataAsync();
    }

    private void UpdateWeeksList()
    {
        WeeksList.Clear();
        int monthIdx = MonthsList.IndexOf(SelectedMonth) + 1;
        if (monthIdx <= 0) monthIdx = DateTime.Today.Month;

        int daysInMonth = DateTime.DaysInMonth(SelectedYear, monthIdx);
        DateTime currentStart = new DateTime(SelectedYear, monthIdx, 1);

        int weekNum = 1;

        while (currentStart.Month == monthIdx)
        {
            int daysUntilSunday = ((int)DayOfWeek.Sunday - (int)currentStart.DayOfWeek + 7) % 7;
            DateTime currentEnd = currentStart.AddDays(daysUntilSunday);

            if (currentEnd.Month != monthIdx)
            {
                currentEnd = new DateTime(SelectedYear, monthIdx, daysInMonth);
            }

            WeeksList.Add(new WeekOption
            {
                WeekNumber = weekNum++,
                StartDate = currentStart,
                EndDate = currentEnd
            });

            currentStart = currentEnd.AddDays(1);
        }

        SelectedWeek = WeeksList.FirstOrDefault();
    }

    private async Task LoadDataAsync()
    {
        DailyBreakdownList.Clear();
        Guid userId = _session.CurrentUser?.Id ?? Guid.Empty;

        List<DailyStatItem> items = new();

        if (IsDayMode)
        {
            items = await _statsService.GetDailyStatsAsync(userId, SelectedDate);
        }
        else if (IsWeekMode && SelectedWeek != null)
        {
            items = await _statsService.GetWeeklyStatsAsync(userId, SelectedWeek.StartDate, SelectedWeek.EndDate);
        }
        else if (IsMonthMode)
        {
            int monthIdx = MonthsList.IndexOf(SelectedMonth) + 1;
            if (monthIdx > 0)
                items = await _statsService.GetMonthlyStatsAsync(userId, SelectedYear, monthIdx);
        }

        foreach (var item in items)
            DailyBreakdownList.Add(item);

        double sumCal = DailyBreakdownList.Sum(x => x.Calories);
        double sumProt = DailyBreakdownList.Sum(x => x.Proteins);
        double sumFat = DailyBreakdownList.Sum(x => x.Fats);
        double sumCarb = DailyBreakdownList.Sum(x => x.Carbs);

        TotalCaloriesText = $"{sumCal:F0} ккал";
        TotalProteinsText = $"{sumProt:F0} г";
        TotalFatsText = $"{sumFat:F0} г";
        TotalCarbsText = $"{sumCarb:F0} г";

        int daysCount = Math.Max(1, DailyBreakdownList.Count);
        double targetCal = 2000 * daysCount;
        double targetProt = 150 * daysCount;
        double targetFat = 70 * daysCount;
        double targetCarb = 250 * daysCount;

        CalorieGoalDiffText = $"Ціль: {targetCal:F0} ккал";
        ProteinGoalText = $"Ціль: {targetProt:F0} г";
        FatGoalText = $"Ціль: {targetFat:F0} г";
        CarbGoalText = $"Ціль: {targetCarb:F0} г";

        CalorieProgressPercent = Math.Min(100, (sumCal / targetCal) * 100);
        ProteinProgressPercent = Math.Min(100, (sumProt / targetProt) * 100);
        FatProgressPercent = Math.Min(100, (sumFat / targetFat) * 100);
        CarbProgressPercent = Math.Min(100, (sumCarb / targetCarb) * 100);

        OnPropertyChanged(nameof(PeriodTableTitle));
    }
}