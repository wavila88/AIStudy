using BotSemanticKernel;

namespace BotSemanticKernel.Tests;

public class OrderTests
{
    private static MenuItem Burger => Menu.Find("Clásica")!;
    private static MenuItem Soda => Menu.Find("Cola")!;

    // ---- carta ----
    [Fact]
    public void The_menu_has_burgers_and_sodas()
    {
        Assert.Contains(Menu.Items, i => i.Kind == ItemKind.Burger);
        Assert.Contains(Menu.Items, i => i.Kind == ItemKind.Soda);
    }

    [Theory]
    [InlineData("Clásica")]
    [InlineData("clasica")]
    [InlineData("  CLÁSICA  ")]
    public void Finding_ignores_case_accents_and_spaces(string typed)
    {
        Assert.Equal("Clásica", Menu.Find(typed)!.Name);
    }

    [Fact]
    public void Finding_something_that_is_not_on_the_menu_returns_null() =>
        Assert.Null(Menu.Find("pizza"));

    // ---- pedido ----
    [Fact]
    public void A_new_order_is_empty()
    {
        var order = new Order();

        Assert.Empty(order.Lines);
        Assert.Equal(0, order.Total);
        Assert.False(order.Confirmed);
        Assert.Equal("El pedido está vacío.", order.Summary());
    }

    [Fact]
    public void Adding_the_same_item_twice_merges_into_one_line()
    {
        var order = new Order();

        order.Add(Burger, 1);
        order.Add(Burger, 2);

        var line = Assert.Single(order.Lines);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(3 * Burger.Price, order.Total);
    }

    [Fact]
    public void The_total_adds_every_line()
    {
        var order = new Order();

        order.Add(Burger, 2);
        order.Add(Soda, 1);

        Assert.Equal(2 * Burger.Price + Soda.Price, order.Total);
    }

    [Fact]
    public void Removing_some_units_keeps_the_line_with_the_rest()
    {
        var order = new Order();
        order.Add(Burger, 3);

        var removed = order.Remove(Burger, 1);

        Assert.Equal(1, removed);
        Assert.Equal(2, Assert.Single(order.Lines).Quantity);
    }

    [Fact]
    public void Removing_more_than_there_is_removes_the_line_and_reports_what_was_removed()
    {
        var order = new Order();
        order.Add(Burger, 2);

        var removed = order.Remove(Burger, 5);

        Assert.Equal(2, removed);
        Assert.Empty(order.Lines);
    }

    [Fact]
    public void Removing_something_that_is_not_in_the_order_removes_nothing()
    {
        var order = new Order();
        order.Add(Burger, 1);

        Assert.Equal(0, order.Remove(Soda, 1));
        Assert.Single(order.Lines);
    }

    [Fact]
    public void The_summary_lists_each_line_and_the_total()
    {
        var order = new Order();
        order.Add(Burger, 2);
        order.Add(Soda, 1);

        var summary = order.Summary();

        Assert.Contains("2 x Clásica", summary);
        Assert.Contains("1 x Cola", summary);
        Assert.Contains("Total:", summary);
        Assert.Contains((2 * Burger.Price + Soda.Price).ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("es-CO")), summary);
    }

    [Fact]
    public void Resetting_clears_everything_including_the_confirmation()
    {
        var order = new Order();
        order.Add(Burger, 1);
        order.Confirm();

        order.Reset();

        Assert.Empty(order.Lines);
        Assert.False(order.Confirmed);
    }
}
