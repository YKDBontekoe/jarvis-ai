using System.Reflection;

namespace Jarvis.UnitTests;

/// <summary>
/// Builds an interface implementation from per-method handlers. Unconfigured members throw so a test fails loudly
/// when code under test starts depending on something new.
/// </summary>
internal class Fake<T> : DispatchProxy where T : class
{
    private Dictionary<string, Func<object?[], object?>> _handlers = [];

    public static T Create(params (string Method, Func<object?[], object?> Handler)[] handlers)
    {
        var proxy = Create<T, Fake<T>>();
        ((Fake<T>)(object)proxy)._handlers = handlers.ToDictionary(item => item.Method, item => item.Handler);
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (!_handlers.TryGetValue(targetMethod.Name, out var handler))
            throw new NotSupportedException($"{typeof(T).Name}.{targetMethod.Name} is not configured in this test.");
        var result = handler(args ?? []);
        var returnType = targetMethod.ReturnType;
        if (returnType == typeof(Task)) return result as Task ?? Task.CompletedTask;
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>) && result is not Task)
        {
            var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(returnType.GetGenericArguments()[0]);
            return fromResult.Invoke(null, [result]);
        }
        return result;
    }
}
