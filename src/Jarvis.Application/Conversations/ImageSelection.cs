namespace Jarvis.Application.Conversations;

/// <summary>An image that goes to the model with a turn, and why.</summary>
public sealed record ImageToShow(Guid FileId, string FileName, string ContentType, ImageReason Reason);

public enum ImageReason
{
    /// <summary>Sent with this very message.</summary>
    ThisMessage,

    /// <summary>An image file the owner attached to the conversation as a source.</summary>
    Source,

    /// <summary>A photo from a recent earlier message, shown again so follow-up questions about it work.</summary>
    Earlier,
}

/// <summary>
/// Decides which images the model sees on a turn. Photos used to be shown once and then replaced by a note, so
/// "what was the total on that receipt?" could not be answered, and an image file attached as a source reached the
/// model only as searchable extracted text. Now the model gets the real pixels in both cases, up to the per-request
/// limit, with the current message always first.
/// </summary>
public static class ImageSelection
{
    /// <summary>How many earlier messages are searched for photos worth showing again.</summary>
    public const int EarlierMessageWindow = 6;

    /// <summary>At most this many earlier photos come back, so a follow-up stays cheap.</summary>
    public const int MaxEarlierPhotos = 2;

    public static IReadOnlyList<ImageToShow> Plan(
        IReadOnlyList<MessageAttachment> thisMessage,
        IReadOnlyList<(Guid FileId, string FileName, string ContentType)> sources,
        IReadOnlyList<IReadOnlyList<MessageAttachment>> earlierNewestFirst,
        int limit = MessageAttachments.MaxPerMessage)
    {
        var chosen = new List<ImageToShow>();
        var seen = new HashSet<Guid>();

        bool Add(Guid fileId, string name, string contentType, ImageReason reason)
        {
            if (chosen.Count >= limit || !MessageAttachments.IsSupportedImage(contentType) || !seen.Add(fileId))
                return false;
            chosen.Add(new ImageToShow(fileId, name, contentType, reason));
            return true;
        }

        foreach (var attachment in thisMessage)
            Add(attachment.FileId, attachment.FileName, attachment.ContentType, ImageReason.ThisMessage);

        // Sources the owner chose on purpose come before older photos.
        foreach (var source in sources)
            Add(source.FileId, source.FileName, source.ContentType, ImageReason.Source);

        // Earlier photos only fill what is left, and only when this message brought no photo of its own: a new
        // photo is what the owner is asking about now.
        if (thisMessage.Count == 0)
        {
            var earlier = 0;
            foreach (var message in earlierNewestFirst.Take(EarlierMessageWindow))
                foreach (var attachment in message)
                {
                    if (earlier >= MaxEarlierPhotos) return chosen;
                    if (Add(attachment.FileId, attachment.FileName, attachment.ContentType, ImageReason.Earlier))
                        earlier++;
                }
        }

        return chosen;
    }

    /// <summary>The line that tells the model what an attached image is, so it can say which one it means.</summary>
    public static string Caption(ImageToShow image) => image.Reason switch
    {
        ImageReason.Source => $"[Image file the user attached as a source: {SafeName(image.FileName)}]",
        ImageReason.Earlier => $"[Earlier photo from this conversation, shown again for reference: {SafeName(image.FileName)}]",
        _ => $"[Photo the user sent with this message: {SafeName(image.FileName)}]",
    };

    // File names are user data: keep them short and on one line so a name cannot pose as an instruction.
    private static string SafeName(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length <= 60 ? clean : clean[..59] + "…";
    }
}
