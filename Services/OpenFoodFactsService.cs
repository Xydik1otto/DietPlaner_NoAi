using System.Net;
using System.Net.Http;
using System.Text.Json;
using DietPlanner.Services.Contracts;

namespace DietPlanner.Services;

public class OpenFoodFactsService : IOpenFoodFactsService
{
    private static readonly HttpClient HttpClient;

    static OpenFoodFactsService()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true
        };

        HttpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(12)
        };

        HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("DietPlannerApp/1.0 (Windows; Ukrainian Market Integration)");
        HttpClient.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    }

    public async Task<List<ExternalProductDto>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<ExternalProductDto>();

        var cleanQuery = query.Trim();

        // 1. Пошук з пріоритетом під ринок України (сорт за популярністю)
        var results = await FetchWithRetryAsync(cleanQuery, cancellationToken);

        // 2. Якщо знайдено менше 3 результатів, робимо автопереклад англійською та шукаємо додатково
        if (results.Count < 3)
        {
            var translatedQuery = await TranslateToEnglishAsync(cleanQuery, cancellationToken);
            
            if (!string.IsNullOrWhiteSpace(translatedQuery) && 
                !translatedQuery.Equals(cleanQuery, StringComparison.OrdinalIgnoreCase))
            {
                var translatedResults = await FetchWithRetryAsync(translatedQuery, cancellationToken);
                results.AddRange(translatedResults);
            }
        }

        return results.DistinctBy(r => r.Name).Take(35).ToList();
    }

    /// <summary>
    /// Динамічний переклад з української на англійську через MyMemory API
    /// </summary>
    private static async Task<string?> TranslateToEnglishAsync(string text, CancellationToken cancellationToken)
    {
        try
        {
            var encodedText = Uri.EscapeDataString(text);
            var url = $"https://api.mymemory.translated.net/get?q={encodedText}&langpair=uk|en";

            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(jsonContent);

            if (doc.RootElement.TryGetProperty("responseData", out var responseData) &&
                responseData.TryGetProperty("translatedText", out var translatedTextVal))
            {
                var translated = translatedTextVal.GetString();
                if (!string.IsNullOrWhiteSpace(translated) && !translated.Contains("QUERY LENGTH LIMIT EXCEEDED", StringComparison.OrdinalIgnoreCase))
                {
                    return translated.Trim();
                }
            }
        }
        catch
        {
            // При помилці мережі перекладу просто продовжуємо з оригінальним запитом
        }

        return null;
    }

    private static async Task<List<ExternalProductDto>> FetchWithRetryAsync(string searchTerm, CancellationToken cancellationToken)
    {
        var encoded = Uri.EscapeDataString(searchTerm);

        var urls = new[]
        {
            // 1. Пріоритет: товари, зареєстровані/доступні в Україні, впорядковані за популярністю
            $"https://ua.openfoodfacts.org/cgi/search.pl?search_terms={encoded}&search_simple=1&action=process&json=1&countries_tags_en=ukraine&cc=ua&lc=uk&sort_by=popularity&page_size=30",
            
            // 2. Резервний: україномовний розділ OpenFoodFacts
            $"https://uk.openfoodfacts.org/cgi/search.pl?search_terms={encoded}&search_simple=1&action=process&json=1&lc=uk&sort_by=popularity&page_size=30",
            
            // 3. Глобальна база даних з українською локалізацією відповідей
            $"https://world.openfoodfacts.org/cgi/search.pl?search_terms={encoded}&search_simple=1&action=process&json=1&lc=uk&page_size=30"
        };

        foreach (var url in urls)
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var res = await FetchFromUrlAsync(url, cancellationToken);
                if (res.Count > 0)
                {
                    return res;
                }

                if (attempt == 0)
                {
                    await Task.Delay(300, cancellationToken);
                }
            }
        }

        return new List<ExternalProductDto>();
    }

    private static async Task<List<ExternalProductDto>> FetchFromUrlAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseContentRead, cancellationToken);
            if (!response.IsSuccessStatusCode) return new List<ExternalProductDto>();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(jsonContent);
            var root = doc.RootElement;

            if (!root.TryGetProperty("products", out var productsElement) || productsElement.ValueKind != JsonValueKind.Array)
            {
                return new List<ExternalProductDto>();
            }

            var list = new List<ExternalProductDto>();

            foreach (var item in productsElement.EnumerateArray())
            {
                string name = GetProductName(item);
                if (string.IsNullOrWhiteSpace(name)) continue;

                string brand = GetStringProp(item, "brands");
                string code = GetStringProp(item, "code");

                decimal calories = 0m, proteins = 0m, fats = 0m, carbs = 0m;

                if (item.TryGetProperty("nutriments", out var nutriments))
                {
                    calories = ExtractCalories(nutriments);
                    proteins = ExtractNutrient(nutriments, "proteins_100g", "proteins_value", "proteins");
                    fats = ExtractNutrient(nutriments, "fat_100g", "fat_value", "fat");
                    carbs = ExtractNutrient(nutriments, "carbohydrates_100g", "carbohydrates_value", "carbohydrates");
                }

                string displayName = !string.IsNullOrWhiteSpace(brand) && !name.Contains(brand, StringComparison.OrdinalIgnoreCase)
                    ? $"{name} ({brand})"
                    : name;

                list.Add(new ExternalProductDto(
                    Name: displayName,
                    BrandOrCategory: string.IsNullOrWhiteSpace(brand) ? "Перекус (Україна)" : brand,
                    Calories: Math.Round(Math.Max(0m, calories), 1),
                    Proteins: Math.Round(Math.Max(0m, proteins), 1),
                    Fats: Math.Round(Math.Max(0m, fats), 1),
                    Carbs: Math.Round(Math.Max(0m, carbs), 1),
                    Code: code
                ));
            }

            return list;
        }
        catch
        {
            return new List<ExternalProductDto>();
        }
    }

    private static string GetProductName(JsonElement item)
    {
        string[] nameProps = { "product_name_uk", "product_name", "product_name_en", "generic_name_uk", "generic_name" };

        foreach (var prop in nameProps)
        {
            if (item.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String)
            {
                var str = val.GetString();
                if (!string.IsNullOrWhiteSpace(str)) return str.Trim();
            }
        }

        return string.Empty;
    }

    private static decimal ExtractCalories(JsonElement nutriments)
    {
        var kcal = ExtractNutrient(nutriments, "energy-kcal_100g", "energy-kcal_value", "energy-kcal", "energy-kcal_serving");
        if (kcal > 0m) return kcal;

        var kj = ExtractNutrient(nutriments, "energy_100g", "energy_value", "energy");
        if (kj > 0m) return kj / 4.184m;

        return 0m;
    }

    private static decimal ExtractNutrient(JsonElement element, params string[] propertyNames)
    {
        foreach (var prop in propertyNames)
        {
            if (element.TryGetProperty(prop, out var val))
            {
                if (val.ValueKind == JsonValueKind.Number && val.TryGetDecimal(out var dec))
                    return dec;
                if (val.ValueKind == JsonValueKind.String && decimal.TryParse(val.GetString(), out var parsed))
                    return parsed;
            }
        }
        return 0m;
    }

    private static string GetStringProp(JsonElement element, string propName)
    {
        if (element.TryGetProperty(propName, out var val) && val.ValueKind == JsonValueKind.String)
        {
            return val.GetString() ?? string.Empty;
        }
        return string.Empty;
    }
}