using System.Globalization;

/// <summary>
/// Turns an eval's numbers into a verdict. Each eval prints its numbers as before, then reports named checks against
/// thresholds (defaults are the recorded baseline, overridable with "--name value") and exits non-zero when one fails,
/// so a prompt or model change that makes learning worse cannot pass unnoticed.
/// </summary>
internal static class EvalGate
{
    /// <summary>The value after "--name", or <paramref name="fallback"/> when the option is absent.</summary>
    public static double Option(IReadOnlyList<string> args, string name, double fallback)
    {
        for (var index = 0; index < args.Count - 1; index++)
            if (args[index] == name)
                return double.Parse(args[index + 1], CultureInfo.InvariantCulture);
        return fallback;
    }

    public static (string Name, bool Passed, string Detail) AtLeast(string name, double actual, double minimum) =>
        (name, actual >= minimum, $"{actual.ToString("0.00", CultureInfo.InvariantCulture)} (needs at least {minimum.ToString("0.00", CultureInfo.InvariantCulture)})");

    public static (string Name, bool Passed, string Detail) AtMost(string name, double actual, double maximum) =>
        (name, actual <= maximum, $"{actual.ToString("0.##", CultureInfo.InvariantCulture)} (allowed {maximum.ToString("0.##", CultureInfo.InvariantCulture)})");

    /// <summary>Prints one line per check and returns whether all passed.</summary>
    public static bool Report(TextWriter output, params (string Name, bool Passed, string Detail)[] checks)
    {
        output.WriteLine();
        output.WriteLine("Gate");
        foreach (var (name, passed, detail) in checks)
            output.WriteLine($"  {(passed ? "pass" : "FAIL")}  {name,-22} {detail}");
        var ok = checks.All(check => check.Passed);
        output.WriteLine(ok ? "Gate passed." : "Gate FAILED.");
        return ok;
    }
}
