using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using DietPlanner.Data;
using DietPlanner.Infrastructure;
using DietPlanner.Services;
using DietPlanner.Services.Contracts;
using DietPlanner.ViewModels;
using DietPlanner.Views;

namespace DietPlanner;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var serviceCollection = new ServiceCollection();
        ConfigureServices(serviceCollection);

        Services = serviceCollection.BuildServiceProvider();

        try
        {
            using (var scope = Services.CreateScope())
            {
                var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                await db.Database.EnsureCreatedAsync();

                var initializer = scope.ServiceProvider.GetRequiredService<DbInitializer>();
                await initializer.InitializeAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Помилка ініціалізації бази даних:\n{ex.Message}", "Помилка запуску", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        var navService = Services.GetRequiredService<INavigationService>();
        var authService = Services.GetRequiredService<IAuthenticationService>();
        var mainVm = Services.GetRequiredService<MainWindowViewModel>();

        bool isAuthenticated = false;
        try
        {
            isAuthenticated = await authService.TryAutoLoginAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Помилка автовходу за токеном: {ex.Message}");
        }

        if (isAuthenticated)
        {
            mainVm.UpdateAuthenticationState();
            navService.Navigate<DashboardViewModel>();
        }
        else
        {
            navService.Navigate<AuthViewModel>();
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // 1. Інфраструктура та База даних
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddDbContextFactory<AppDbContext>((sp, options) =>
        {
            var paths = sp.GetRequiredService<IAppPaths>();
            options.UseSqlite($"Data Source={paths.DatabasePath}");
        });
        services.AddTransient<DbInitializer>();

        // 2. Сервіси бізнес-логіки
        services.AddSingleton<ITokenService, TokenService>();
        services.AddTransient<IEmailService, EmailService>();
        services.AddSingleton<SessionService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IAuthenticationService, AuthenticationService>();
        services.AddSingleton<IAuthorizationService, AuthorizationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IDishService, DishService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IRestrictionService, RestrictionService>();
        services.AddScoped<INutritionCalculator, NutritionCalculator>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<ISortService, SortService>();
        services.AddScoped<IPlanService, PlanService>();
        services.AddScoped<IUndoService, UndoService>();
        services.AddSingleton<IValidationService, ValidationService>();
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddScoped<IMealIntakeService, MealIntakeService>();
        services.AddScoped<IReportService, ReportService>();
        services.AddTransient<IStatisticsService, StatisticsService>();
        services.AddSingleton<IOpenFoodFactsService, OpenFoodFactsService>();
        
        // 3. ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<AuthViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<CatalogViewModel>();
        services.AddTransient<PlanGeneratorViewModel>();
        services.AddTransient<StatisticsViewModel>();
        services.AddTransient<UndoHistoryViewModel>();
        services.AddTransient<AdminViewModel>();

        // 4. Views
        services.AddSingleton<MainWindow>();
        services.AddTransient<AuthPage>();
        services.AddTransient<DashboardPage>();
        services.AddTransient<ProfilePage>();
        services.AddTransient<CatalogPage>();
        services.AddTransient<PlanGeneratorPage>();
        services.AddTransient<StatisticsPage>();
        services.AddTransient<UndoHistoryPage>();
        services.AddTransient<AdminPage>();
    }
}