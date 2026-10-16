using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Jarvis.Infrastructure.Persistence;

/// <summary>
/// PostgreSQL cannot store the NUL character in text or jsonb, and rejects the write with an error, so a stray one
/// in a pasted note, a tool result or a scraped page would fail the whole save. Every string about to be written
/// has them removed, wherever it came from; for jsonb columns that includes the \u0000 escape a serializer writes.
/// </summary>
public sealed partial class NulCharacterInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Strip(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Strip(eventData.Context);
        return ValueTask.FromResult(result);
    }

    internal static void Strip(DbContext? context)
    {
        if (context is null) return;
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified)) continue;
            foreach (var property in entry.Properties)
            {
                if (property.Metadata.ClrType != typeof(string) || property.CurrentValue is not string value) continue;
                if (entry.State == EntityState.Modified && !property.IsModified) continue;
                var clean = Clean(value, string.Equals(property.Metadata.GetColumnType(), "jsonb",
                    StringComparison.OrdinalIgnoreCase));
                if (!ReferenceEquals(clean, value)) property.CurrentValue = clean;
            }
        }
    }

    public static string Clean(string value, bool json)
    {
        var clean = value.Contains('\0') ? value.Replace("\0", "", StringComparison.Ordinal) : value;
        // An escaped NUL is only an escape when an even number of backslashes comes before the one that starts it.
        if (json && clean.Contains("\\u0000", StringComparison.OrdinalIgnoreCase))
            clean = EscapedNul().Replace(clean, "$1");
        return clean;
    }

    [GeneratedRegex(@"(?<!\\)((?:\\\\)*)\\u0000", RegexOptions.IgnoreCase)]
    private static partial Regex EscapedNul();
}
