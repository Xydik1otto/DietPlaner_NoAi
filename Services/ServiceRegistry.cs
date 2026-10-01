using DietPlanner.Services;
using DietPlanner.Services.Contracts;
using DietPlanner.ViewModels;
using DietPlanner.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DietPlanner.Services;

public static class ServiceRegistry
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<LoggingService>();
        services.AddSingleton<ILoggingService>(sp => sp.GetRequiredService<LoggingService>());
        services.AddSingleton<SessionService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IValidationService, ValidationService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();

        services.AddScoped<IAuthenticationService, AuthenticationService>();
        services.AddScoped<IAuthorizationService, AuthorizationService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<ICategoryService, CategoryService>();
        services.AddScoped<IDishService, DishService>();
        services.AddScoped<INutritionCalculator, NutritionCalculator>();
        services.AddScoped<IRestrictionService, RestrictionService>();
        services.AddScoped<ISearchService, SearchService>();
        services.AddScoped<ISortService, SortService>();
        services.AddScoped<IUndoService, UndoService>();
        services.AddScoped<IMealIntakeService, MealIntakeService>();
        services.AddScoped<IReportService, ReportService>();

        services.AddTransient<AuthViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<AdminViewModel>();

        services.AddTransient<AuthPage>();
        services.AddTransient<DashboardPage>();
        services.AddTransient<ProfilePage>();
        services.AddTransient<AdminPage>();

        return services;
    }
}
