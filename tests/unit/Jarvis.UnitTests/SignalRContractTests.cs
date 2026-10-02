using System.Reflection;
using System.Text.Json;
using Jarvis.Api.Realtime;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class SignalRContractTests
{
    [Fact]
    public void Mobile_hub_invocations_keep_their_names_and_parameter_types()
    {
        var methods = typeof(JarvisEventsHub).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => method.GetBaseDefinition().DeclaringType == typeof(JarvisEventsHub))
            .ToDictionary(method => method.Name, method => method.GetParameters().Select(parameter => Describe(parameter.ParameterType)).ToArray());
        var path = Path.Combine(AppContext.BaseDirectory, "signalr.json");
        var baseline = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(path))!;
        foreach (var (name, parameters) in baseline)
        {
            Assert.True(methods.TryGetValue(name, out var current), $"SignalR method removed: {name}");
            Assert.Equal(parameters, current);
        }
    }

    private static string Describe(Type type) => type.IsGenericType
        ? type.GetGenericTypeDefinition().FullName!.Split('`')[0] + "<" +
          string.Join(",", type.GetGenericArguments().Select(Describe)) + ">"
        : type.FullName!;
}
