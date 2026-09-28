namespace DietPlanner.Services.Contracts;

public record ExternalProductDto(
    string Name,
    string BrandOrCategory,
    decimal Calories,
    decimal Proteins,
    decimal Fats,
    decimal Carbs,
    string Code = ""
);

public interface IOpenFoodFactsService
{
    Task<List<ExternalProductDto>> SearchAsync(string query, CancellationToken cancellationToken = default);
}