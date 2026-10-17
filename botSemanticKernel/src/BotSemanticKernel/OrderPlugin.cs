using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace BotSemanticKernel;

/// <summary>
/// Un "plugin" de Semantic Kernel es una clase con métodos marcados con [KernelFunction].
/// Esos métodos son las HERRAMIENTAS que el modelo puede decidir llamar.
///
/// Cómo lo sabe el modelo: Semantic Kernel envía a OpenAI el nombre de cada función, su [Description]
/// y la descripción de sus parámetros. Con eso, cuando el cliente dice "quiero dos clásicas", el modelo
/// responde "llama a AddItem(name='Clásica', quantity=2)". Semantic Kernel ejecuta ese método y le devuelve
/// al modelo el texto que retorna, para que redacte la respuesta al cliente.
///
/// Todo lo que el método devuelve es lo que el modelo "ve", por eso devolvemos frases claras.
/// </summary>
public sealed class OrderPlugin(Order order)
{
    private const int MaxPerLine = 20;
    private const string AlreadyConfirmed = "El pedido ya está confirmado y no se puede modificar.";

    [KernelFunction, Description("Muestra la carta: las hamburguesas y las gaseosas disponibles con su precio.")]
    public string GetMenu()
    {
        string Lines(ItemKind kind) =>
            string.Join("\n", Menu.Items.Where(i => i.Kind == kind).Select(i => $"- {i.Name}: {Menu.Money(i.Price)}"));

        return $"Hamburguesas:\n{Lines(ItemKind.Burger)}\nGaseosas:\n{Lines(ItemKind.Soda)}";
    }

    [KernelFunction, Description("Agrega una hamburguesa o una gaseosa al pedido del cliente.")]
    public string AddItem(
        [Description("Nombre del producto tal como aparece en la carta, por ejemplo 'Clásica' o 'Cola'")] string name,
        [Description("Cantidad de unidades, entre 1 y 20")] int quantity = 1)
    {
        if (order.Confirmed) return AlreadyConfirmed;
        if (quantity < 1 || quantity > MaxPerLine) return $"La cantidad debe estar entre 1 y {MaxPerLine}.";

        var item = Menu.Find(name);
        if (item is null) return NotOnMenu(name);

        order.Add(item, quantity);
        return $"Agregado: {quantity} x {item.Name}.\nPedido actual:\n{order.Summary()}";
    }

    [KernelFunction, Description("Quita unidades de un producto del pedido del cliente.")]
    public string RemoveItem(
        [Description("Nombre del producto tal como aparece en la carta")] string name,
        [Description("Cantidad de unidades a quitar")] int quantity = 1)
    {
        if (order.Confirmed) return AlreadyConfirmed;
        if (quantity < 1) return "La cantidad debe ser al menos 1.";

        var item = Menu.Find(name);
        if (item is null) return NotOnMenu(name);

        var removed = order.Remove(item, quantity);
        if (removed == 0) return $"{item.Name} no está en el pedido.";
        return $"Quitado: {removed} x {item.Name}.\nPedido actual:\n{order.Summary()}";
    }

    [KernelFunction, Description("Devuelve el resumen real del pedido: productos, cantidades y total. Úsala siempre que el cliente pida ver su pedido o antes de confirmarlo.")]
    public string GetOrderSummary() => order.Summary();

    [KernelFunction, Description("Confirma el pedido. Solo llámala cuando el cliente haya dicho claramente que quiere confirmar.")]
    public string ConfirmOrder()
    {
        if (order.Confirmed) return AlreadyConfirmed;
        if (order.Lines.Count == 0) return "El pedido está vacío; no hay nada que confirmar.";
        if (!order.Has(ItemKind.Burger)) return "Falta una hamburguesa. Pregunta al cliente cuál quiere antes de confirmar.";
        if (!order.Has(ItemKind.Soda)) return "Falta la gaseosa. Pregunta al cliente cuál quiere antes de confirmar.";

        order.Confirm();
        return $"Pedido confirmado.\n{order.Summary()}";
    }

    private static string NotOnMenu(string name)
    {
        var names = string.Join(", ", Menu.Items.Select(i => i.Name));
        return $"No tenemos '{name}'. Productos disponibles: {names}.";
    }
}
