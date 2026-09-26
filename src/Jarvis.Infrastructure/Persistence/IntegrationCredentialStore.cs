using System.Text.Json;
using System.Text.RegularExpressions;
using System.Data;
using Jarvis.Application.Audit;
using Jarvis.Application.Integrations;
using Jarvis.Domain.Audit;
using Jarvis.Domain.Integrations;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Jarvis.Infrastructure.Persistence;

public sealed partial class IntegrationCredentialStore(JarvisDbContext db,
    IDataProtectionProvider protectionProvider) : IIntegrationCredentialStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<IntegrationCredentialStatus>> ListAsync(Guid ownerId,
        CancellationToken cancellationToken) =>
        (await db.IntegrationCredentials.AsNoTracking().Where(x => x.OwnerId == ownerId)
            .OrderBy(x => x.Provider).ToListAsync(cancellationToken))
        .Select(ToStatus).ToArray();

    public async Task<IntegrationCredentialStatus?> GetStatusAsync(Guid ownerId, string provider,
        CancellationToken cancellationToken) =>
        (await db.IntegrationCredentials.AsNoTracking().SingleOrDefaultAsync(
            x => x.OwnerId == ownerId && x.Provider == provider, cancellationToken)) is { } credential
            ? ToStatus(credential) : null;

    public async Task SaveAsync(Guid ownerId, string provider, IReadOnlyDictionary<string, string> secrets,
        CancellationToken cancellationToken)
    {
        Validate(provider, secrets);
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var credential = await GetLockedAsync(ownerId, provider, cancellationToken);
        var protectedPayload = Protect(ownerId, provider, secrets);
        var secretNames = SerializeSecretNames(secrets);
        if (credential is null)
            db.IntegrationCredentials.Add(new IntegrationCredential(ownerId, provider, protectedPayload, secretNames));
        else
            credential.Replace(protectedPayload, secretNames);

        db.AuditEvents.Add(new AuditEvent(ownerId, "integrations", "credentials.saved", "moderate", true,
            metadataJson: JsonSerializer.Serialize(new { provider, secretCount = secrets.Count }, JsonOptions)));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveSecretAsync(Guid ownerId, string provider, string secretName, string value,
        CancellationToken cancellationToken)
    {
        Validate(provider, new Dictionary<string, string> { [secretName] = value });
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var credential = await GetLockedAsync(ownerId, provider, cancellationToken);
        var secrets = credential is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : Unprotect(ownerId, provider, credential.ProtectedPayload);
        secrets[secretName] = value;
        if (secrets.Count > 32)
            throw new ArgumentException("Store no more than 32 credentials per integration.", nameof(secretName));

        var protectedPayload = Protect(ownerId, provider, secrets);
        var secretNames = SerializeSecretNames(secrets);
        if (credential is null)
            db.IntegrationCredentials.Add(new IntegrationCredential(ownerId, provider, protectedPayload, secretNames));
        else
            credential.Replace(protectedPayload, secretNames);
        db.AuditEvents.Add(new AuditEvent(ownerId, "integrations", "credential.saved", "moderate", true,
            metadataJson: JsonSerializer.Serialize(new { provider, secretName }, JsonOptions)));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<bool> DeleteSecretAsync(Guid ownerId, string provider, string secretName,
        CancellationToken cancellationToken)
    {
        if (!ProviderPattern().IsMatch(provider) || !SecretNamePattern().IsMatch(secretName))
            throw new ArgumentException("Provider or credential name is invalid.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var credential = await GetLockedAsync(ownerId, provider, cancellationToken);
        if (credential is null) return false;
        var secrets = Unprotect(ownerId, provider, credential.ProtectedPayload);
        if (!secrets.Remove(secretName)) return false;
        if (secrets.Count == 0)
            db.IntegrationCredentials.Remove(credential);
        else
            credential.Replace(Protect(ownerId, provider, secrets), SerializeSecretNames(secrets));
        db.AuditEvents.Add(new AuditEvent(ownerId, "integrations", "credential.deleted", "moderate", true,
            metadataJson: JsonSerializer.Serialize(new { provider, secretName }, JsonOptions)));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyDictionary<string, string>?> GetSecretsAsync(Guid ownerId, string provider,
        CancellationToken cancellationToken)
    {
        var credential = await db.IntegrationCredentials.AsNoTracking().SingleOrDefaultAsync(
            x => x.OwnerId == ownerId && x.Provider == provider, cancellationToken);
        if (credential is null) return null;
        return Unprotect(ownerId, provider, credential.ProtectedPayload);
    }

    public async Task<bool> DeleteAsync(Guid ownerId, string provider, CancellationToken cancellationToken)
    {
        if (!ProviderPattern().IsMatch(provider))
            throw new ArgumentException("Provider must be a lowercase integration slug (letters, numbers, hyphens).", nameof(provider));
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var credential = await GetLockedAsync(ownerId, provider, cancellationToken);
        if (credential is null) return false;
        db.IntegrationCredentials.Remove(credential);
        db.AuditEvents.Add(new AuditEvent(ownerId, "integrations", "credentials.deleted", "moderate", true,
            metadataJson: JsonSerializer.Serialize(new { provider }, JsonOptions)));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    private static void Validate(string provider, IReadOnlyDictionary<string, string> secrets)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        if (!ProviderPattern().IsMatch(provider))
            throw new ArgumentException("Provider must be a lowercase integration slug (letters, numbers, hyphens).", nameof(provider));
        if (secrets.Count is < 1 or > 32)
            throw new ArgumentException("Store between 1 and 32 credentials per integration.", nameof(secrets));
        foreach (var (name, value) in secrets)
        {
            if (!SecretNamePattern().IsMatch(name))
                throw new ArgumentException("Credential names must use letters, numbers, underscores, or hyphens and be at most 64 characters.", nameof(secrets));
            if (string.IsNullOrWhiteSpace(value) || value.Length > 16_384)
                throw new ArgumentException("Credential values must contain 1 to 16,384 characters.", nameof(secrets));
        }
    }

    private static IntegrationCredentialStatus ToStatus(IntegrationCredential credential)
    {
        var secretNames = JsonSerializer.Deserialize<string[]>(credential.SecretNamesJson, JsonOptions) ?? [];
        return new IntegrationCredentialStatus(credential.Provider, secretNames, credential.UpdatedAt);
    }

    private async Task<IntegrationCredential?> GetLockedAsync(Guid ownerId, string provider,
        CancellationToken cancellationToken) =>
        await db.IntegrationCredentials.FromSqlInterpolated(
            $"SELECT * FROM integration_credentials WHERE owner_id = {ownerId} AND \"Provider\" = {provider} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    private string Protect(Guid ownerId, string provider, IReadOnlyDictionary<string, string> secrets) =>
        protectionProvider.CreateProtector("Jarvis.IntegrationCredentials.v1", ownerId.ToString("N"), provider)
            .Protect(JsonSerializer.Serialize(secrets, JsonOptions));

    private Dictionary<string, string> Unprotect(Guid ownerId, string provider, string protectedPayload)
    {
        var protector = protectionProvider.CreateProtector("Jarvis.IntegrationCredentials.v1", ownerId.ToString("N"), provider);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(
            protector.Unprotect(protectedPayload), JsonOptions)
            ?? throw new InvalidOperationException("The stored integration credentials could not be decoded.");
    }

    private static string SerializeSecretNames(IReadOnlyDictionary<string, string> secrets) =>
        JsonSerializer.Serialize(secrets.Keys.OrderBy(name => name, StringComparer.Ordinal), JsonOptions);

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex ProviderPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretNamePattern();
}
