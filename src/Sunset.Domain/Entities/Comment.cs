namespace Sunset.Domain.Entities;

public class Comment : BaseEntity
{
    public Guid UserId { get; private set; }
    public Guid PhotoId { get; private set; }
    public string Content { get; private set; } = null!;
    public Guid? ParentCommentId { get; private set; }
    public int RepliesCount { get; private set; }

    public User User { get; private set; } = null!;
    public Photo Photo { get; private set; } = null!;

    private Comment() { }

    public Comment(Guid userId, Guid photoId, string content, Guid? parentCommentId = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId is required.", nameof(userId));
        if (photoId == Guid.Empty)
            throw new ArgumentException("PhotoId is required.", nameof(photoId));
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required.", nameof(content));

        UserId = userId;
        PhotoId = photoId;
        Content = content;
        ParentCommentId = parentCommentId;
    }

    public void IncrementRepliesCount() => RepliesCount++;

    public void DecrementRepliesCount()
    {
        if (RepliesCount > 0)
            RepliesCount--;
    }
}
