using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

/// <summary>
/// Lets the model recover from its own mistakes. Validation errors from Jarvis tools
/// (<see cref="ArgumentException"/>, <see cref="FormatException"/>, <see cref="JsonException"/>) are shown to the model so it can
/// fix the arguments; every other failure stays a generic message so internal details, paths, or
/// credentials in exception text never reach the transcript. Approval gating runs before this
/// invoker, so it only ever sees calls that are already allowed to execute.
/// </summary>
internal static class ToolFailureFeedback
{
    internal const int MaxMessageLength = 400;
    internal const string GenericFailure =
        "The tool failed unexpectedly. Try a different tool or approach, or tell the user it did not work.";

    public static void Configure(FunctionInvokingChatClient? invoker)
    {
        if (invoker is null) return;
        invoker.IncludeDetailedErrors = true;
        invoker.FunctionInvoker = InvokeAsync;
    }

    internal static async ValueTask<object?> InvokeAsync(FunctionInvocationContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await context.Function.InvokeAsync(context.Arguments, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or JsonException)
        {
            throw new ToolInputException(Describe(exception), exception);
        }
        catch (Exception exception)
        {
            throw new ToolFailedException(exception);
        }
    }

    private static string Describe(Exception exception)
    {
        var message = exception is ArgumentException argument && argument.ParamName is { } name
            ? argument.Message.Replace($" (Parameter '{name}')", string.Empty, StringComparison.Ordinal)
            : exception.Message;
        message = message.ReplaceLineEndings(" ").Trim();
        if (message.Length > MaxMessageLength) message = message[..MaxMessageLength] + "…";
        return $"The tool rejected its input: {message} Fix the arguments and try again, or choose another approach.";
    }

    internal sealed class ToolInputException(string message, Exception inner) : Exception(message, inner);

    internal sealed class ToolFailedException(Exception inner) : Exception(GenericFailure, inner);
}
