using System.Globalization;
using System.Text;

namespace BotSemanticKernel;

public enum ItemKind { Burger, Soda }

/// <summary>Un producto de la carta.</summary>
public sealed record MenuItem(string Name, int Price, ItemKind Kind);

/// <summary>
/// La carta del restaurante. Los precios viven aquí, en código: el modelo NO los conoce de memoria,
/// los lee llamando a la función GetMenu. Así nunca inventa un precio.
/// </summary>
public static class Menu
{
    public static readonly IReadOnlyList<MenuItem> Items = new[]
    {
        new MenuItem("Clásica", 18_000, ItemKind.Burger),
        new MenuItem("Doble", 24_000, ItemKind.Burger),
        new MenuItem("Pollo", 20_000, ItemKind.Burger),
        new MenuItem("Cola", 5_000, ItemKind.Soda),
        new MenuItem("Limón", 5_000, ItemKind.Soda),
        new MenuItem("Naranja", 5_000, ItemKind.Soda),
    };

    /// <summary>Busca un producto ignorando mayúsculas, tildes y espacios ("clasica" encuentra "Clásica").</summary>
    public static MenuItem? Find(string name) =>
        Items.FirstOrDefault(item => Normalize(item.Name) == Normalize(name));

    /// <summary>18000 -> "$18.000"</summary>
    public static string Money(int amount) =>
        "$" + amount.ToString("N0", CultureInfo.GetCultureInfo("es-CO"));

    private static string Normalize(string text)
    {
        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var withoutAccents = decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark);
        return new string(withoutAccents.ToArray());
    }
}
