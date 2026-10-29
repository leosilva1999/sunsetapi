namespace Sunset.Application.DTOs.Moderation;

/// <param name="Reason">Audit trail and notice email to the user.</param>
/// <param name="HideContent">Also hides (soft-deletes) the user's photos and comments; otherwise
/// they stay visible, shown as "Usuário excluído".</param>
public sealed record AnonymizeUserRequest(string Reason, bool HideContent = false);
