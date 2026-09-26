using Jarvis.Application.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Jarvis.Memory;

public static class DependencyInjection
{
    public static IServiceCollection AddJarvisMemory(this IServiceCollection services)
    {
        services.AddScoped<IMemoryService, MemoryService>();
        return services;
    }
}
