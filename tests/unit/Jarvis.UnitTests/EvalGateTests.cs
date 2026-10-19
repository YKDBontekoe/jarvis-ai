using Xunit;

namespace Jarvis.UnitTests;

public sealed class EvalGateTests
{
    [Fact]
    public void An_option_is_read_from_its_flag_and_falls_back_to_the_default()
    {
        string[] args = ["--runs", "2", "--min-pass", "0.8"];

        Assert.Equal(0.8, EvalGate.Option(args, "--min-pass", 0.95));
        Assert.Equal(0, EvalGate.Option(args, "--max-harmful", 0));
        Assert.Equal(0.95, EvalGate.Option(["--min-pass"], "--min-pass", 0.95)); // a flag with no value
    }

    [Theory]
    [InlineData(1.0, 0.95, true)]
    [InlineData(0.95, 0.95, true)]
    [InlineData(0.94, 0.95, false)]
    public void A_minimum_check_passes_at_or_above_the_threshold(double actual, double minimum, bool passes) =>
        Assert.Equal(passes, EvalGate.AtLeast("share", actual, minimum).Passed);

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(1, 0, false)]
    [InlineData(2, 2, true)]
    public void A_maximum_check_passes_at_or_below_the_threshold(double actual, double maximum, bool passes) =>
        Assert.Equal(passes, EvalGate.AtMost("harmful", actual, maximum).Passed);

    [Fact]
    public void The_report_names_every_check_and_fails_if_any_one_fails()
    {
        var output = new StringWriter();

        var ok = EvalGate.Report(output, EvalGate.AtLeast("cases passed", 1.0, 0.95), EvalGate.AtMost("harmful writes", 1, 0));

        Assert.False(ok);
        var text = output.ToString();
        Assert.Contains("pass  cases passed", text);
        Assert.Contains("FAIL  harmful writes", text);
        Assert.Contains("Gate FAILED.", text);
    }

    [Fact]
    public void The_report_passes_when_all_checks_pass()
    {
        var output = new StringWriter();

        Assert.True(EvalGate.Report(output, EvalGate.AtLeast("cases passed", 1.0, 0.95)));
        Assert.Contains("Gate passed.", output.ToString());
    }
}
