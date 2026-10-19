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
    internal const string TransientFailure =
        "The tool could not reach what it needs just now, which is usually temporary. You may try it once more; if it fails again, tell the user it is unavailable right now.";

    internal const int DefaultMaxResultChars = 16_000;
    internal const int DefaultMaxIterations = 24;
    /// <summary>The same tool with the same arguments may run this many times in one turn; the next call is refused.</summary>
    internal const int MaxIdenticalCalls = 2;

    public static void Configure(FunctionInvokingChatClient? invoker, int maxResultChars = DefaultMaxResultChars,
        int maxIterations = DefaultMaxIterations)
    {
        if (invoker is null) return;
        invoker.IncludeDetailedErrors = true;
        invoker.MaximumIterationsPerRequest = maxIterations;
        invoker.FunctionInvoker = (context, cancellationToken) => InvokeAsync(context, cancellationToken, maxResultChars);
    }

    internal static async ValueTask<object?> InvokeAsync(FunctionInvocationContext context,
        CancellationToken cancellationToken, int maxResultChars = DefaultMaxResultChars)
    {
        if (CountIdenticalCalls(context) > MaxIdenticalCalls)
            throw new ToolInputException(
                $"You already called {context.Function.Name} with these arguments {MaxIdenticalCalls} times in this turn. Use the earlier result or change your approach instead of repeating it.",
                new InvalidOperationException("repeated tool call"));
        try
        {
            return LimitSize(await context.Function.InvokeAsync(context.Arguments, cancellationToken), maxResultChars);
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
            throw new ToolFailedException(exception, IsTransient(exception));
        }
    }

    /// <summary>
    /// A result over the cap is cut to its head and tail so one huge page or listing cannot fill the context window;
    /// anything smaller is returned untouched.
    /// </summary>
    internal static object? LimitSize(object? result, int maxChars)
    {
        if (result is null || maxChars <= 0) return result;
        var text = result as string ?? JsonSerializer.Serialize(result);
        if (text.Length <= maxChars) return result;
        var head = maxChars * 3 / 4;
        var tail = maxChars - head;
        return $"{text[..head]}\n[truncated {text.Length - maxChars} characters]\n{text[^tail..]}";
    }

    /// <summary>
    /// How many times this exact call (tool and arguments) appears since the user's latest message, the current call
    /// included. Earlier turns do not count.
    /// </summary>
    internal static int CountIdenticalCalls(FunctionInvocationContext context)
    {
        var messages = context.Messages;
        if (messages is null) return 0;
        var key = CallKey(context.Function.Name, context.Arguments);
        var count = 0;
        for (var index = messages.Count - 1; index >= 0; index--)
        {
            var message = messages[index];
            if (message.Role == ChatRole.User && !string.IsNullOrWhiteSpace(message.Text) && !ToolGroups.IsInjectedContext(message))
                break;
            count += message.Contents.OfType<FunctionCallContent>()
                .Count(call => CallKey(call.Name, call.Arguments) == key);
        }
        return count;
    }

    private static string CallKey(string name, IEnumerable<KeyValuePair<string, object?>>? arguments)
    {
        var ordered = (arguments ?? []).OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={JsonSerializer.Serialize(pair.Value)}");
        return $"{name}({string.Join(",", ordered)})";
    }

    /// <summary>
    /// Network and timeout failures are worth one more try; anything else would fail the same way again. Only the
    /// category reaches the model, never the exception text.
    /// </summary>
    internal static bool IsTransient(Exception exception) => exception switch
    {
        HttpRequestException or TimeoutException => true,
        TaskCanceledException or IOException => true,
        System.Net.Sockets.SocketException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsTransient),
        _ => exception.InnerException is { } inner && IsTransient(inner)
    };

    /// <summary>A category for a failed tool call: bad arguments, a temporary problem, or any other failure.</summary>
    internal static string Kind(Exception exception) => exception switch
    {
        ToolInputException => "input",
        ToolFailedException { Transient: true } => "transient",
        _ => "failed"
    };

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

    internal sealed class ToolFailedException(Exception inner, bool transient = false)
        : Exception(transient ? TransientFailure : GenericFailure, inner)
    {
        public bool Transient { get; } = transient;
    }
}
