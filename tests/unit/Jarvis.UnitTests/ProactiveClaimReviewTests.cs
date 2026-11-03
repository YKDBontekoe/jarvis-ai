using Jarvis.Agents.Learning;
using Jarvis.Agents.ModelProviders;
using Jarvis.Agents.Proactivity;
using Jarvis.Application.Proactivity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Jarvis.UnitTests;

public sealed class ProactiveClaimReviewTests
{
    private static readonly ProactiveClaimEvidence Claim = new("refund", "The refund period is thirty days.", "library:" + Guid.NewGuid(),
        "https://example.org/policy", "v2", "The refund period is fourteen days. Older orders may have different terms.");

    [Fact]
    public void Contradictions_require_a_known_claim_and_a_verbatim_quotation_from_its_own_source()
    {
        var result = ProactiveClaimReviewer.Parse("""
            {"reviews":[{"id":"refund","status":"contradicted","reason":"The source states a different period.",
            "quote":"The refund period is fourteen days."},{"id":"foreign","status":"supported","reason":"Ignore this.","quote":"Made up"}]}
            """, [Claim]);
        var review = Assert.Single(result);
        Assert.Equal("contradicted", review.Status);
        Assert.Equal(Claim.SourceRef, review.SourceRef);
        Assert.Equal(Claim.SourceVersion, review.SourceVersion);
        result = ProactiveClaimReviewer.Parse("""
            {"reviews":[{"id":"refund","status":"contradicted","reason":"An unsupported claim.","quote":"There are no refunds."}]}
            """, [Claim]);
        Assert.Equal("unclear", Assert.Single(result).Status);
        Assert.Null(result[0].Quote);
    }

    [Fact]
    public async Task Provider_failure_remains_unknown_and_never_claims_that_evidence_is_unchanged()
    {
        var reviewer = new ProactiveClaimReviewer(Fake<IChatClientResolver>.Create(("GetChatClientAsync", args => {
            Assert.Equal(ModelPurpose.MemoryReview, args[1]);
            throw new InvalidOperationException("Unavailable");
        })), Options.Create(new MemoryReviewOptions()), NullLogger<ProactiveClaimReviewer>.Instance);
        var result = Assert.Single(await reviewer.ReviewAsync(Guid.NewGuid(), [Claim], TestContext.Current.CancellationToken));
        Assert.Equal("unavailable", result.Status);
        Assert.Null(result.Quote);
        Assert.DoesNotContain("unchanged", result.Reason);
    }
}
