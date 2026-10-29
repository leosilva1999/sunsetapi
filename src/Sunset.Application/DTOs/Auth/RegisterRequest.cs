namespace Sunset.Application.DTOs.Auth;

/// <param name="TermsVersion">Version of the Terms of Use the user was shown and accepted (see GET /terms). Optional.</param>
/// <param name="PrivacyVersion">Version of the Privacy Policy the user was shown and accepted (see GET /privacy). Optional.</param>
public sealed record RegisterRequest(string Name, string Email, string Password, int? TermsVersion = null, int? PrivacyVersion = null);
