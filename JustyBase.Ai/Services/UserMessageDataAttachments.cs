namespace JustyBase.Ai.Services;

public abstract class UserMessageDataAttachmentsItem
{
    public string Path { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
}

public sealed class UserMessageDataAttachmentsItemDirectory : UserMessageDataAttachmentsItem
{
    // discriminator useful for serialization
    public string Type { get; } = "directory";
}

public sealed class UserMessageDataAttachmentsItemFile : UserMessageDataAttachmentsItem
{
    public string Type { get; } = "file";
    public int? StartLine { get; set; }
    public int? EndLine { get; set; }
}
