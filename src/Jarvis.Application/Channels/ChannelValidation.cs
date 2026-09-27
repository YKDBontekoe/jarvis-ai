namespace Jarvis.Application.Channels;

public static class ChannelValidation
{
    public const int MaxAllowedSenders = 20;

    /// <summary>Normalizes a save request or throws <see cref="ArgumentException"/> with a user-facing message.</summary>
    public static SaveChannelRequest Normalize(SaveChannelRequest request, bool creating)
    {
        var kind = request.Kind?.Trim().ToLowerInvariant();
        if (!ChannelKinds.IsValid(kind)) throw new ArgumentException("Choose whatsapp or signal.");
        var name = request.DisplayName?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 80)
            throw new ArgumentException("Give the channel a name of up to 80 characters.");

        var account = request.Account?.Trim() ?? string.Empty;
        if (kind == ChannelKinds.WhatsApp)
        {
            if (account.Length is < 5 or > 30 || !account.All(char.IsDigit))
                throw new ArgumentException("Enter the WhatsApp phone number ID from Meta's API Setup page (digits only).");
        }
        else
        {
            account = ChannelAddresses.Normalize(account);
            if (!ChannelAddresses.IsPhoneNumber(account))
                throw new ArgumentException("Enter the Signal account number in international format, like +31612345678.");
        }

        var senders = (request.AllowedSenders ?? []).Select(ChannelAddresses.Normalize)
            .Where(value => value.Length > 0).Distinct().ToArray();
        if (senders.Length == 0)
            throw new ArgumentException("Add at least one phone number that may talk to Jarvis.");
        if (senders.Length > MaxAllowedSenders)
            throw new ArgumentException($"Allow at most {MaxAllowedSenders} phone numbers.");
        if (senders.FirstOrDefault(sender => !ChannelAddresses.IsPhoneNumber(sender)) is { } invalid)
            throw new ArgumentException($"{invalid} is not a valid international phone number.");

        var notify = string.IsNullOrWhiteSpace(request.NotifyRecipient) ? null : ChannelAddresses.Normalize(request.NotifyRecipient);
        if (notify is not null && !senders.Contains(notify))
            throw new ArgumentException("Notifications can only go to an allowed phone number.");

        var secrets = (request.Secrets ?? new Dictionary<string, string>())
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .ToDictionary(pair => pair.Key, pair => pair.Value.Trim());
        var allowedSecrets = ChannelKinds.SecretNames(kind!);
        if (secrets.Keys.FirstOrDefault(key => !allowedSecrets.Contains(key)) is { } unknown)
            throw new ArgumentException($"{unknown} is not a {kind} credential.");
        if (creating && allowedSecrets.FirstOrDefault(name => !secrets.ContainsKey(name)) is { } missing)
            throw new ArgumentException($"Provide the WhatsApp {missing.Replace('_', ' ')}.");
        if (secrets.Values.Any(value => value.Length > 1_000 || value.Any(char.IsControl)))
            throw new ArgumentException("Credentials must be single-line values of up to 1,000 characters.");

        return new SaveChannelRequest(kind, name, account, request.Enabled, senders, request.ForwardNotifications,
            notify, secrets);
    }
}
