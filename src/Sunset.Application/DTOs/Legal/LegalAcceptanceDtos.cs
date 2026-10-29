namespace Sunset.Application.DTOs.Legal;

/// <param name="CurrentVersion">Latest published version; null if the document was never published.</param>
/// <param name="AcceptedVersion">Latest version this user accepted; null if none.</param>
/// <param name="Pending">True when a published version exists that the user has not accepted yet.</param>
public sealed record LegalDocumentAcceptance(int? CurrentVersion, int? AcceptedVersion, DateTime? AcceptedAt, bool Pending);

public sealed record LegalAcceptanceStatusResponse(LegalDocumentAcceptance Terms, LegalDocumentAcceptance Privacy, bool Pending);

public sealed record AcceptLegalDocumentsRequest(int TermsVersion, int PrivacyVersion);
