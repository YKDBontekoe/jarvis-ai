using Jarvis.Api.Errors;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ApiMessagesTests
{
    [Theory]
    [InlineData("Reminder time must be within the next two years. (Parameter 'request')",
        "Reminder time must be within the next two years.")]
    [InlineData("Name is required. (Parameter 'name')", "Name is required.")]
    [InlineData("Pick a chat. (Parameter 'chatId').", "Pick a chat.")]
    [InlineData("Fine as it is.", "Fine as it is.")]
    [InlineData("Keep (Parameter 'x') in the middle of the text.", "Keep (Parameter 'x') in the middle of the text.")]
    public void The_name_of_a_server_variable_is_not_shown_to_people(string message, string expected) =>
        Assert.Equal(expected, ApiMessages.Clean(message));

    [Fact]
    public void Argument_exceptions_reach_the_client_without_the_parameter_name()
    {
        var mapped = ExceptionProblemMapper.Map(new ArgumentException("Must be positive.", "amount"));

        Assert.Equal("Must be positive.", mapped.Detail);
    }

    [Fact]
    public void Validation_problems_are_cleaned_for_every_field()
    {
        var problem = JarvisProblemDetails.CreateValidation(
            new Dictionary<string, string[]> { ["dueAt"] = ["Too far away. (Parameter 'request')", "Clean."] },
            new DefaultHttpContext());

        Assert.Equal(["Too far away.", "Clean."], problem.Errors["dueAt"]);
    }
}
