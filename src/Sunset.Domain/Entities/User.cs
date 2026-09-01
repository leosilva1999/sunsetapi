namespace Sunset.Domain.Entities;

public class User : BaseEntity
{
    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string? AvatarUrl { get; private set; }

    public ICollection<Photo> Photos { get; private set; } = new List<Photo>();
    public ICollection<Like> Likes { get; private set; } = new List<Like>();
    public ICollection<Comment> Comments { get; private set; } = new List<Comment>();
    public ICollection<Rating> Ratings { get; private set; } = new List<Rating>();

    private User() { }

    public User(string name, string email, string passwordHash, string? avatarUrl = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new ArgumentException("PasswordHash is required.", nameof(passwordHash));

        Name = name;
        Email = email;
        PasswordHash = passwordHash;
        AvatarUrl = avatarUrl;
    }

    public void UpdateProfile(string name, string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Name = name;
        AvatarUrl = avatarUrl;
    }
}
