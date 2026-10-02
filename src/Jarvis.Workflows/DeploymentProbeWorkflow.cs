using Temporalio.Workflows;

namespace Jarvis.Workflows;

/// <summary>A side-effect-free workflow used to verify that a deployed worker can execute work.</summary>
[Workflow]
public sealed class DeploymentProbeWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(string nonce) => Task.FromResult(nonce);
}
