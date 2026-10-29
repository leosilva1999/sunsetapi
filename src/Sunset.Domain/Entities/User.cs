using Sunset.Domain.Enums;

namespace Sunset.Domain.Entities;

public class User : BaseEntity
{
    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;

    // False for accounts created through Google sign-in: PasswordHash is still required by the
    // entity, but holds a random hash nobody knows, so "change password" must not be offered
    // (there is no current password to check). Flips to true once a password is set through
    // the reset flow.
    public bool HasPassword { get; private set; } = true;
    public string? AvatarUrl { get; private set; }
    public string? Bio { get; private set; }
    public UserRole Role { get; private set; } = UserRole.User;
    public int FollowersCount { get; private set; }
    public int FollowingCount { get; private set; }

    // Ban state. BannedUntil == null with BannedAt set means permanent; an expired BannedUntil
    // lifts the ban on its own (IsBanned compares against the clock, nothing needs to run).
    public DateTime? BannedAt { get; private set; }
    public DateTime? BannedUntil { get; private set; }
    public string? BanReason { get; private set; }
    public Guid? BannedByUserId { get; private set; }

    public ICollection<Photo> Photos { get; private set; } = new List<Photo>();
    public ICollection<Like> Likes { get; private set; } = new List<Like>();
    public ICollection<Comment> Comments { get; private set; } = new List<Comment>();
    public ICollection<Rating> Ratings { get; private set; } = new List<Rating>();
    public ICollection<RefreshToken> RefreshTokens { get; private set; } = new List<RefreshToken>();

    private User() { }

    public User(string name, string email, string passwordHash, string? avatarUrl = null, bool hasPassword = true)
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
        HasPassword = hasPassword;
        AvatarUrl = avatarUrl;
    }

    public void UpdateProfile(string name, string? avatarUrl, string? bio)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Name = name;
        AvatarUrl = avatarUrl;
        Bio = bio;
    }

    // Exclusão de conta (LGPD Art. 18, IX): apaga os dados pessoais identificáveis mas
    // preserva a linha (e as fotos/comentários/avaliações ligados a ela via FK) - Name/
    // AvatarUrl são lidos ao vivo por foto/comentário/avaliação, então isso já basta pra
    // exibir "Usuário excluído" em tudo que essa pessoa postou, sem cascata em conteúdo
    // de terceiros (respostas de outras pessoas, curtidas, etc). Email vira um placeholder
    // único (não pode colidir com o índice único de Email) e a senha, um hash inutilizável -
    // reforça o bloqueio de login mesmo que a troca de email já impeça por si só.
    public void Anonymize(string placeholderEmail, string unusablePasswordHash)
    {
        Name = "Usuário excluído";
        Email = placeholderEmail;
        PasswordHash = unusablePasswordHash;
        HasPassword = false;
        AvatarUrl = null;
        Bio = null;
    }

    public void ChangeRole(UserRole newRole) => Role = newRole;

    public bool IsBanned(DateTime utcNow) =>
        BannedAt is not null && (BannedUntil is null || BannedUntil > utcNow);

    public void Ban(string reason, DateTime? until, Guid bannedByUserId, DateTime utcNow)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));
        if (until is not null && until <= utcNow)
            throw new ArgumentException("Ban end must be in the future.", nameof(until));

        BannedAt = utcNow;
        BannedUntil = until;
        BanReason = reason;
        BannedByUserId = bannedByUserId;
    }

    public void Unban()
    {
        BannedAt = null;
        BannedUntil = null;
        BanReason = null;
        BannedByUserId = null;
    }

    public void ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new ArgumentException("PasswordHash is required.", nameof(newPasswordHash));

        PasswordHash = newPasswordHash;
        HasPassword = true;
    }

    public void IncrementFollowersCount() => FollowersCount++;

    public void DecrementFollowersCount()
    {
        if (FollowersCount > 0)
            FollowersCount--;
    }

    public void IncrementFollowingCount() => FollowingCount++;

    public void DecrementFollowingCount()
    {
        if (FollowingCount > 0)
            FollowingCount--;
    }
}
