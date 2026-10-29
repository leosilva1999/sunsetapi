namespace Sunset.Application.DTOs.Auth;

/// <param name="TermsVersion">Version of the Terms of Use the user accepted on the sign-up screen. Optional.</param>
/// <param name="PrivacyVersion">Version of the Privacy Policy the user accepted on the sign-up screen. Optional.</param>
public sealed record GoogleAuthRequest(string IdToken, int? TermsVersion = null, int? PrivacyVersion = null);
