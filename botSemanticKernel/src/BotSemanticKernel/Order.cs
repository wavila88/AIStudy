namespace BotSemanticKernel;

public sealed record OrderLine(MenuItem Item, int Quantity)
{
    public int Subtotal => Item.Price * Quantity;
}

/// <summary>
/// El pedido del cliente. Es una clase normal de C#: guarda las líneas y calcula el total.
/// El modelo no guarda el pedido "en su cabeza": lo modifica llamando a las funciones del plugin,
/// y esas funciones modifican este objeto.
/// </summary>
public sealed class Order
{
    private readonly List<OrderLine> _lines = new();

    public IReadOnlyList<OrderLine> Lines => _lines;
    public bool Confirmed { get; private set; }
    public int Total => _lines.Sum(line => line.Subtotal);

    public bool Has(ItemKind kind) => _lines.Any(line => line.Item.Kind == kind);

    public void Add(MenuItem item, int quantity)
    {
        var index = _lines.FindIndex(line => line.Item == item);
        if (index < 0)
            _lines.Add(new OrderLine(item, quantity));
        else
            _lines[index] = _lines[index] with { Quantity = _lines[index].Quantity + quantity };
    }

    /// <summary>Quita unidades. Devuelve cuántas quitó realmente (0 si el producto no estaba).</summary>
    public int Remove(MenuItem item, int quantity)
    {
        var index = _lines.FindIndex(line => line.Item == item);
        if (index < 0) return 0;

        var removed = Math.Min(quantity, _lines[index].Quantity);
        if (removed == _lines[index].Quantity)
            _lines.RemoveAt(index);
        else
            _lines[index] = _lines[index] with { Quantity = _lines[index].Quantity - removed };
        return removed;
    }

    public void Confirm() => Confirmed = true;

    public void Reset()
    {
        _lines.Clear();
        Confirmed = false;
    }

    /// <summary>El resumen que ve el cliente. Lo arma el código, no el modelo, así los números siempre cuadran.</summary>
    public string Summary()
    {
        if (_lines.Count == 0) return "El pedido está vacío.";

        var text = _lines.Select(line => $"{line.Quantity} x {line.Item.Name} — {Menu.Money(line.Subtotal)}");
        return string.Join("\n", text) + $"\nTotal: {Menu.Money(Total)}";
    }
}
