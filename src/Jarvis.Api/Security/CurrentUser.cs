using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
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
            if (environment.IsDevelopment())
                return Guid.Parse(configuration["Jarvis:DevelopmentUserId"] ?? "01996b8c-6000-7000-8000-000000000001");

            var principal = context?.User;
            var subject = principal?.FindFirstValue("sub") ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            var issuer = principal?.FindFirstValue("iss") ?? configuration["Authentication:Authority"];
            if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(issuer))
                throw new UnauthorizedAccessException("The authenticated identity has no OIDC authority or subject.");

            var identity = SHA256.HashData(Encoding.UTF8.GetBytes($"{issuer.TrimEnd('/')}\n{subject}"));
            return new Guid(identity.AsSpan(0, 16));
        }
    }
}
