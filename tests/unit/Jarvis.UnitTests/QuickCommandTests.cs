using Jarvis.Application.Channels;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class QuickCommandTests
{
    [Theory]
    [InlineData("/spent 12.50 lunch with Sam", 12.50, null, "lunch with Sam")]
    [InlineData("/spent 12,50 broodje", 12.50, null, "broodje")]
    [InlineData("/spent €45 petrol", 45, "EUR", "petrol")]
    [InlineData("/spent $9.99", 9.99, "USD", null)]
    [InlineData("/spent 20 eur train ticket", 20, "EUR", "train ticket")]
    [InlineData("/SPENT 1,234.50 laptop bag", 1234.50, null, "laptop bag")]
    [InlineData("/spent 8 £ coffee", 8, "GBP", "coffee")]
    public void Spending_is_read_with_decimal_commas_symbols_and_currency_words(string text, double amount, string? currency, string? description)
    {
        var spent = Assert.IsType<QuickCommand.Spent>(QuickCommands.Parse(text));

        Assert.Equal((decimal)amount, spent.Amount);
        Assert.Equal(currency, spent.Currency);
        Assert.Equal(description, spent.Description);
    }

    [Theory]
    [InlineData("/spent")]
    [InlineData("/spent lunch")]
    [InlineData("/spent 0 free")]
    [InlineData("/spent -5 refund")]
    [InlineData("/spent 99999999 yacht")]
    [InlineData("/spent 1.2.3 odd")]
    public void Unreadable_spending_explains_the_format(string text) =>
        Assert.Equal(QuickCommands.SpentUsage, Assert.IsType<QuickCommand.Usage>(QuickCommands.Parse(text)).Message);

    [Fact]
    public void Check_in_takes_one_or_several_habits_up_to_five_and_flattens_names()
    {
        Assert.Equal(["meditate"], Assert.IsType<QuickCommand.CheckIn>(QuickCommands.Parse("/checkin meditate")).Habits);
        Assert.Equal(["meditate", "read", "gym"],
            Assert.IsType<QuickCommand.CheckIn>(QuickCommands.Parse("/CHECKIN meditate, read;gym")).Habits);
        Assert.Equal(5, Assert.IsType<QuickCommand.CheckIn>(QuickCommands.Parse("/checkin a,b,c,d,e,f,g")).Habits.Count);
        Assert.Equal(QuickCommands.CheckInUsage, Assert.IsType<QuickCommand.Usage>(QuickCommands.Parse("/checkin")).Message);
        Assert.Equal(QuickCommands.CheckInUsage, Assert.IsType<QuickCommand.Usage>(QuickCommands.Parse("/checkin , ,")).Message);
    }

    [Theory]
    [InlineData("/today")]
    [InlineData("/Today ")]
    public void Today_is_a_command(string text) => Assert.IsType<QuickCommand.Today>(QuickCommands.Parse(text));

    [Theory]
    [InlineData("I spent 12 euros")]
    [InlineData("/todayish")]
    [InlineData("/lists")]
    [InlineData("")]
    public void Other_messages_are_not_quick_commands(string text) => Assert.Null(QuickCommands.Parse(text));
}
