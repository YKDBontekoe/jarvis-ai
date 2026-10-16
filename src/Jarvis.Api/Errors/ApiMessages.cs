using System.Text.RegularExpressions;

namespace Jarvis.Api.Errors;

internal static partial class ApiMessages
{
    /// <summary>
    /// .NET appends " (Parameter 'request')" to the message of an <see cref="ArgumentException"/>. It names a variable
    /// in the server's code, which means nothing to the person reading the error, so it is dropped.
    /// </summary>
    public static string Clean(string message) => ParameterSuffix().Replace(message, "").TrimEnd();

    public static IDictionary<string, string[]> Clean(IDictionary<string, string[]> errors) =>
        errors.ToDictionary(pair => pair.Key, pair => pair.Value.Select(Clean).ToArray());

    [GeneratedRegex(@"\s*\(Parameter '[^']*'\)\.?\s*$")]
    private static partial Regex ParameterSuffix();
}
