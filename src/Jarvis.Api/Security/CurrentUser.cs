using System.Security.Claims;
using Jarvis.Application.Conversations;

namespace Jarvis.Api.Security;

public sealed class CurrentUser(IHttpContextAccessor httpContextAccessor, IConfiguration configuration, IWebHostEnvironment environment) : ICurrentUser
{
    public Guid OwnerId
    {
        get
        {
            var context = httpContextAccessor.HttpContext;
            if (context?.Items.TryGetValue("Jarvis.InternalVoiceOwnerId", out var trustedOwnerId) == true &&
                trustedOwnerId is Guid ownerId)
                return ownerId;

            var principal = context?.User;
            var subject = principal?.FindFirstValue("sub") ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (Guid.TryParse(subject, out var userId))
                return userId;

            if (environment.IsDevelopment())
                return Guid.Parse(configuration["Jarvis:DevelopmentUserId"] ?? "01996b8c-6000-7000-8000-000000000001");

            throw new UnauthorizedAccessException("The authenticated identity has no account id.");
        }
    }
}
