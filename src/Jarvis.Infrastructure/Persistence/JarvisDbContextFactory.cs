using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pgvector.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed class JarvisDbContextFactory : IDesignTimeDbContextFactory<JarvisDbContext>
{
    public JarvisDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__jarvis")
            ?? "Host=localhost;Port=5432;Database=jarvis;Username=jarvis;Password=jarvis";
        var options = new DbContextOptionsBuilder<JarvisDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .Options;
        return new JarvisDbContext(options);
    }
}
