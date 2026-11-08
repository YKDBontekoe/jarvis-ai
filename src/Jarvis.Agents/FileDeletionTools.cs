using System.ComponentModel;
using Jarvis.Application.Conversations;
using Jarvis.Application.Files;
using Microsoft.Extensions.AI;

namespace Jarvis.Agents;

internal sealed class FileDeletionToolContributor(IFileService files, ICurrentUser currentUser) : IAgentToolContributor
{
    public IEnumerable<AITool> GetTools(AgentBuildContext context)
    {
        if (context.IsBackgroundTask) yield break;
        var tools = new FileDeletionTools(files, currentUser);
        yield return new ApprovalRequiredAIFunction(AIFunctionFactory.Create(tools.DeleteFileAsync));
    }
}

/// <summary>Deleting a file removes its stored bytes and its search index, so the user always approves it first.</summary>
internal sealed class FileDeletionTools(IFileService files, ICurrentUser currentUser)
{
    [Description("Permanently delete one of the user's files, only when the user clearly asks to delete it. The file's contents and search index are removed. The user is asked to approve first. Find the file id with ListFiles or SearchFiles.")]
    public async Task<string> DeleteFileAsync(
        [Description("The file id from ListFiles or SearchFiles.")] string fileId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(fileId, out var id)) return "fileId must be an id from ListFiles.";
        return await files.DeleteAsync(id, currentUser.OwnerId, cancellationToken)
            ? "The file is deleted."
            : "I could not find that file.";
    }
}
