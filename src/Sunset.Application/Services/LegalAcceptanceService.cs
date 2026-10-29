using FluentValidation;
using FluentValidation.Results;
using Sunset.Application.DTOs.Legal;
using Sunset.Application.Interfaces;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.Application.Services;

public class LegalAcceptanceService(
    ILegalDocumentRepository legalDocumentRepository,
    ILegalAcceptanceRepository legalAcceptanceRepository) : ILegalAcceptanceService
{
    public async Task<LegalAcceptanceStatusResponse> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var terms = await legalDocumentRepository.GetCurrentAsync(LegalDocumentType.TermsOfService, cancellationToken);
        var privacy = await legalDocumentRepository.GetCurrentAsync(LegalDocumentType.PrivacyPolicy, cancellationToken);
        var accepted = await legalAcceptanceRepository.GetByUserAsync(userId, cancellationToken);

        var termsStatus = BuildStatus(terms, accepted.Where(a => a.DocumentType == LegalDocumentType.TermsOfService));
        var privacyStatus = BuildStatus(privacy, accepted.Where(a => a.DocumentType == LegalDocumentType.PrivacyPolicy));

        return new LegalAcceptanceStatusResponse(termsStatus, privacyStatus, termsStatus.Pending || privacyStatus.Pending);
    }

    public async Task<LegalAcceptanceStatusResponse> AcceptAsync(Guid userId, AcceptLegalDocumentsRequest request, CancellationToken cancellationToken = default)
    {
        await EnsureCurrentAsync(request.TermsVersion, request.PrivacyVersion, cancellationToken);
        await RecordAsync(userId, request.TermsVersion, request.PrivacyVersion, LegalAcceptanceSource.Reacceptance, cancellationToken);

        return await GetStatusAsync(userId, cancellationToken);
    }

    public async Task EnsureCurrentAsync(int? termsVersion, int? privacyVersion, CancellationToken cancellationToken = default)
    {
        var failures = new List<ValidationFailure>();

        if (termsVersion is { } t
            && await legalDocumentRepository.GetCurrentAsync(LegalDocumentType.TermsOfService, cancellationToken) is { } terms
            && t != terms.Version)
            failures.Add(new ValidationFailure("TermsVersion", $"Not the current version of the Terms of Use (current: {terms.Version})."));

        if (privacyVersion is { } p
            && await legalDocumentRepository.GetCurrentAsync(LegalDocumentType.PrivacyPolicy, cancellationToken) is { } privacy
            && p != privacy.Version)
            failures.Add(new ValidationFailure("PrivacyVersion", $"Not the current version of the Privacy Policy (current: {privacy.Version})."));

        if (failures.Count > 0)
            throw new ValidationException(failures);
    }

    public async Task RecordAsync(Guid userId, int? termsVersion, int? privacyVersion, LegalAcceptanceSource source, CancellationToken cancellationToken = default)
    {
        var existing = await legalAcceptanceRepository.GetByUserAsync(userId, cancellationToken);
        var toAdd = new List<LegalAcceptance>();

        async Task Collect(LegalDocumentType type, int? version)
        {
            // A document that was never published has nothing to accept.
            if (version is not { } v || await legalDocumentRepository.GetCurrentAsync(type, cancellationToken) is null)
                return;
            if (existing.Any(a => a.DocumentType == type && a.Version == v))
                return;
            toAdd.Add(new LegalAcceptance(userId, type, v, source));
        }

        await Collect(LegalDocumentType.TermsOfService, termsVersion);
        await Collect(LegalDocumentType.PrivacyPolicy, privacyVersion);

        if (toAdd.Count > 0)
            await legalAcceptanceRepository.AddRangeAsync(toAdd, cancellationToken);
    }

    private static LegalDocumentAcceptance BuildStatus(LegalDocument? current, IEnumerable<LegalAcceptance> acceptances)
    {
        var latest = acceptances.OrderByDescending(a => a.Version).FirstOrDefault();
        var pending = current is not null && (latest is null || latest.Version < current.Version);

        return new LegalDocumentAcceptance(current?.Version, latest?.Version, latest?.CreatedAt, pending);
    }
}
