namespace Sunset.Application.DTOs.Users;

// Forma pública de um usuário - sem Email. Usada em GET /users/{id}, que não exige
// autenticação e é referenciável por qualquer userId exposto em fotos/comentários/
// avaliações; UserResponse (com Email) fica restrita a contextos autenticados
// (login/register/refresh, PATCH /users/me).
public sealed record PublicUserResponse(Guid Id, string Name, string? AvatarUrl, string? Bio, DateTime CreatedAt);
