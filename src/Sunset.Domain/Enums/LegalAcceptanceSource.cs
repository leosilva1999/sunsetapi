namespace Sunset.Domain.Enums;

public enum LegalAcceptanceSource
{
    /// <summary>Accepted while creating the account (register / first Google sign-in).</summary>
    Registration,

    /// <summary>Accepted later, when a new version was published (or the account predates acceptance).</summary>
    Reacceptance
}
