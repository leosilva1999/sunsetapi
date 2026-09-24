using Sunset.Domain.Enums;

namespace Sunset.Domain.Entities;

public class User : BaseEntity
{
    public string Name { get; private set; } = null!;
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string? AvatarUrl { get; private set; }
    public string? Bio { get; private set; }
    public UserRole Role { get; private set; } = UserRole.User;

    public ICollection<Photo> Photos { get; private set; } = new List<Photo>();
    public ICollection<Like> Likes { get; private set; } = new List<Like>();
    public ICollection<Comment> Comments { get; private set; } = new List<Comment>();
    public ICollection<Rating> Ratings { get; private set; } = new List<Rating>();
    public ICollection<RefreshToken> RefreshTokens { get; private set; } = new List<RefreshToken>();

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
        AvatarUrl = null;
        Bio = null;
    }

    public void ChangeRole(UserRole newRole) => Role = newRole;
}
