using Jarvis.Application.Improvements;
using Jarvis.Domain.Improvements;

namespace Jarvis.UnitTests;

internal sealed class InMemoryImprovementRepository : IImprovementRepository
{
    private readonly List<ImprovementProposal> _items = [];

    public Task<IReadOnlyList<ImprovementProposal>> ListAsync(Guid ownerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ImprovementProposal>>(
            _items.Where(item => item.OwnerId == ownerId).OrderByDescending(item => item.UpdatedAt).ToArray());

    public async Task<ImprovementProposal?> GetAsync(Guid id, Guid ownerId, CancellationToken cancellationToken) =>
        (await ListAsync(ownerId, cancellationToken)).FirstOrDefault(item => item.Id == id);

    public async Task<ImprovementProposal?> FindByFingerprintAsync(Guid ownerId, string fingerprint,
        CancellationToken cancellationToken) =>
        (await ListAsync(ownerId, cancellationToken)).FirstOrDefault(item => item.Fingerprint == fingerprint);

    public Task AddAsync(ImprovementProposal proposal, CancellationToken cancellationToken)
    {
        if (_items.Any(item => item.OwnerId == proposal.OwnerId && item.Fingerprint == proposal.Fingerprint))
            throw new InvalidOperationException("Duplicate fingerprint.");
        _items.Add(proposal);
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(ImprovementProposal proposal, CancellationToken cancellationToken)
    {
        var index = _items.FindIndex(item => item.Id == proposal.Id && item.OwnerId == proposal.OwnerId);
        if (index < 0) return Task.FromResult(false);
        _items[index] = proposal;
        return Task.FromResult(true);
    }

    public Task<int> DeleteManyAsync(Guid ownerId, IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken) =>
        Task.FromResult(_items.RemoveAll(item => item.OwnerId == ownerId && ids.Contains(item.Id)));
}
