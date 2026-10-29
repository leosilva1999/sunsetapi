using Sunset.Application.DTOs.Legal;
using Sunset.Domain.Enums;

namespace Sunset.Application.Interfaces;

public interface ILegalAcceptanceService
{
    Task<LegalAcceptanceStatusResponse> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The user accepts the current Terms and Privacy Policy (both versions must be the current ones).</summary>
    Task<LegalAcceptanceStatusResponse> AcceptAsync(Guid userId, AcceptLegalDocumentsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Throws a validation error when a version the client says it showed is no longer the current one.
    /// Null versions are skipped; a document that was never published is skipped too.</summary>
    Task EnsureCurrentAsync(int? termsVersion, int? privacyVersion, CancellationToken cancellationToken = default);

    /// <summary>Records the acceptance of the given versions (assumes <see cref="EnsureCurrentAsync"/> passed). Null = nothing to record.</summary>
    Task RecordAsync(Guid userId, int? termsVersion, int? privacyVersion, LegalAcceptanceSource source, CancellationToken cancellationToken = default);
}
