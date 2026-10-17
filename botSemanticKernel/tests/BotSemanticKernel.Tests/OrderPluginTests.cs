using BotSemanticKernel;

namespace BotSemanticKernel.Tests;

// Estas son las "herramientas" que el modelo puede llamar. Se prueban como código normal, sin OpenAI.
public class OrderPluginTests
{
    private static (OrderPlugin plugin, Order order) Build()
    {
        var order = new Order();
        return (new OrderPlugin(order), order);
    }

    [Fact]
    public void GetMenu_lists_every_item_with_its_price()
    {
        var (plugin, _) = Build();

        var menu = plugin.GetMenu();

        foreach (var item in Menu.Items) Assert.Contains(item.Name, menu);
        Assert.Contains("18.000", menu);
    }

    [Fact]
    public void AddItem_puts_the_item_in_the_order_even_if_the_name_is_typed_loosely()
    {
        var (plugin, order) = Build();

        var reply = plugin.AddItem("clasica", 2);

        Assert.Equal(2, Assert.Single(order.Lines).Quantity);
        Assert.Contains("Clásica", reply);
    }

    [Fact]
    public void AddItem_defaults_to_one_unit()
    {
        var (plugin, order) = Build();

        plugin.AddItem("Cola");

        Assert.Equal(1, Assert.Single(order.Lines).Quantity);
    }

    [Fact]
    public void AddItem_refuses_an_item_that_is_not_on_the_menu_and_says_what_is()
    {
        var (plugin, order) = Build();

        var reply = plugin.AddItem("pizza", 1);

        Assert.Empty(order.Lines);
        Assert.Contains("No tenemos", reply);
        Assert.Contains("Clásica", reply);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(21)]
    public void AddItem_refuses_silly_quantities(int quantity)
    {
        var (plugin, order) = Build();

        var reply = plugin.AddItem("Clásica", quantity);

        Assert.Empty(order.Lines);
        Assert.Contains("cantidad", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RemoveItem_takes_units_out_and_reports_it()
    {
        var (plugin, order) = Build();
        plugin.AddItem("Doble", 3);

        var reply = plugin.RemoveItem("Doble", 1);

        Assert.Equal(2, Assert.Single(order.Lines).Quantity);
        Assert.Contains("Doble", reply);
    }

    [Fact]
    public void RemoveItem_says_so_when_the_item_is_not_in_the_order()
    {
        var (plugin, _) = Build();

        var reply = plugin.RemoveItem("Doble", 1);

        Assert.Contains("no está", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOrderSummary_returns_the_real_order_not_a_guess()
    {
        var (plugin, order) = Build();
        plugin.AddItem("Clásica", 2);
        plugin.AddItem("Cola", 2);

        var summary = plugin.GetOrderSummary();

        Assert.Equal(order.Summary(), summary);
        Assert.Contains("2 x Clásica", summary);
    }

    [Fact]
    public void ConfirmOrder_refuses_an_empty_order()
    {
        var (plugin, order) = Build();

        var reply = plugin.ConfirmOrder();

        Assert.False(order.Confirmed);
        Assert.Contains("vacío", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfirmOrder_asks_for_a_soda_when_there_is_only_a_burger()
    {
        var (plugin, order) = Build();
        plugin.AddItem("Clásica", 1);

        var reply = plugin.ConfirmOrder();

        Assert.False(order.Confirmed);
        Assert.Contains("gaseosa", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfirmOrder_asks_for_a_burger_when_there_is_only_a_soda()
    {
        var (plugin, order) = Build();
        plugin.AddItem("Cola", 1);

        var reply = plugin.ConfirmOrder();

        Assert.False(order.Confirmed);
        Assert.Contains("hamburguesa", reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConfirmOrder_confirms_a_burger_with_a_soda_and_shows_the_total()
    {
        var (plugin, order) = Build();
        plugin.AddItem("Clásica", 1);
        plugin.AddItem("Cola", 1);

        var reply = plugin.ConfirmOrder();

        Assert.True(order.Confirmed);
        Assert.Contains("confirmado", reply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Total:", reply);
    }

    [Fact]
    public void A_confirmed_order_cannot_be_changed()
    {
        var (plugin, order) = Build();
        plugin.AddItem("Clásica", 1);
        plugin.AddItem("Cola", 1);
        plugin.ConfirmOrder();

        var added = plugin.AddItem("Doble", 1);
        var removed = plugin.RemoveItem("Cola", 1);

        Assert.Equal(2, order.Lines.Count);
        Assert.Contains("confirmado", added, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("confirmado", removed, StringComparison.OrdinalIgnoreCase);
    }
}
