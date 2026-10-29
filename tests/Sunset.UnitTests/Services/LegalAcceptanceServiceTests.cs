using FluentValidation;
using Moq;
using Sunset.Application.DTOs.Legal;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Services;

public class LegalAcceptanceServiceTests
{
    private readonly Mock<ILegalDocumentRepository> _documents = new();
    private readonly Mock<ILegalAcceptanceRepository> _acceptances = new();
    private readonly LegalAcceptanceService _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public LegalAcceptanceServiceTests()
    {
        _sut = new LegalAcceptanceService(_documents.Object, _acceptances.Object);
        _acceptances.Setup(r => r.GetByUserAsync(_userId, default)).ReturnsAsync([]);
    }

    private void Publish(LegalDocumentType type, int version) =>
        _documents.Setup(r => r.GetCurrentAsync(type, default))
            .ReturnsAsync(new LegalDocument(type, "texto", version, Guid.NewGuid()));

    private void Accepted(params (LegalDocumentType Type, int Version)[] rows) =>
        _acceptances.Setup(r => r.GetByUserAsync(_userId, default))
            .ReturnsAsync(rows.Select(r => new LegalAcceptance(_userId, r.Type, r.Version, LegalAcceptanceSource.Registration)).ToList());

    [Fact]
    public async Task GetStatus_WithNoAcceptance_IsPendingForPublishedDocuments()
    {
        Publish(LegalDocumentType.TermsOfService, 2);
        Publish(LegalDocumentType.PrivacyPolicy, 3);

        var status = await _sut.GetStatusAsync(_userId);

        Assert.True(status.Pending);
        Assert.Equal(2, status.Terms.CurrentVersion);
        Assert.Null(status.Terms.AcceptedVersion);
        Assert.True(status.Terms.Pending);
        Assert.True(status.Privacy.Pending);
    }

    [Fact]
    public async Task GetStatus_WhenAcceptedTheCurrentVersions_IsNotPending()
    {
        Publish(LegalDocumentType.TermsOfService, 2);
        Publish(LegalDocumentType.PrivacyPolicy, 3);
        Accepted((LegalDocumentType.TermsOfService, 1), (LegalDocumentType.TermsOfService, 2), (LegalDocumentType.PrivacyPolicy, 3));

        var status = await _sut.GetStatusAsync(_userId);

        Assert.False(status.Pending);
        Assert.Equal(2, status.Terms.AcceptedVersion);
        Assert.NotNull(status.Terms.AcceptedAt);
    }

    [Fact]
    public async Task GetStatus_WhenANewVersionIsPublished_OnlyThatDocumentIsPending()
    {
        Publish(LegalDocumentType.TermsOfService, 3);
        Publish(LegalDocumentType.PrivacyPolicy, 2);
        Accepted((LegalDocumentType.TermsOfService, 2), (LegalDocumentType.PrivacyPolicy, 2));

        var status = await _sut.GetStatusAsync(_userId);

        Assert.True(status.Pending);
        Assert.True(status.Terms.Pending);
        Assert.Equal(2, status.Terms.AcceptedVersion);
        Assert.False(status.Privacy.Pending);
    }

    [Fact]
    public async Task GetStatus_WhenADocumentWasNeverPublished_IsNotPendingForIt()
    {
        _documents.Setup(r => r.GetCurrentAsync(It.IsAny<LegalDocumentType>(), default)).ReturnsAsync((LegalDocument?)null);

        var status = await _sut.GetStatusAsync(_userId);

        Assert.False(status.Pending);
        Assert.Null(status.Terms.CurrentVersion);
    }

    [Fact]
    public async Task Accept_WithCurrentVersions_RecordsBothAsReacceptance()
    {
        Publish(LegalDocumentType.TermsOfService, 2);
        Publish(LegalDocumentType.PrivacyPolicy, 3);
        List<LegalAcceptance>? saved = null;
        _acceptances
            .Setup(r => r.AddRangeAsync(It.IsAny<IEnumerable<LegalAcceptance>>(), default))
            .Callback<IEnumerable<LegalAcceptance>, CancellationToken>((a, _) => saved = a.ToList())
            .Returns(Task.CompletedTask);

        await _sut.AcceptAsync(_userId, new AcceptLegalDocumentsRequest(2, 3));

        Assert.NotNull(saved);
        Assert.Equal(2, saved!.Count);
        Assert.Contains(saved, a => a.DocumentType == LegalDocumentType.TermsOfService && a.Version == 2 && a.Source == LegalAcceptanceSource.Reacceptance && a.UserId == _userId);
        Assert.Contains(saved, a => a.DocumentType == LegalDocumentType.PrivacyPolicy && a.Version == 3);
    }

    [Fact]
    public async Task Accept_WithAStaleVersion_ThrowsValidationAndRecordsNothing()
    {
        Publish(LegalDocumentType.TermsOfService, 3);
        Publish(LegalDocumentType.PrivacyPolicy, 3);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _sut.AcceptAsync(_userId, new AcceptLegalDocumentsRequest(2, 3)));

        Assert.Contains(ex.Errors, e => e.PropertyName == "TermsVersion" && e.ErrorMessage.Contains("current: 3"));
        _acceptances.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<LegalAcceptance>>(), default), Times.Never);
    }

    [Fact]
    public async Task Accept_Twice_IsIdempotent()
    {
        Publish(LegalDocumentType.TermsOfService, 2);
        Publish(LegalDocumentType.PrivacyPolicy, 3);
        Accepted((LegalDocumentType.TermsOfService, 2), (LegalDocumentType.PrivacyPolicy, 3));

        await _sut.AcceptAsync(_userId, new AcceptLegalDocumentsRequest(2, 3));

        _acceptances.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<LegalAcceptance>>(), default), Times.Never);
    }

    [Fact]
    public async Task Record_WithNullVersions_DoesNothing()
    {
        Publish(LegalDocumentType.TermsOfService, 2);

        await _sut.RecordAsync(_userId, null, null, LegalAcceptanceSource.Registration);

        _acceptances.Verify(r => r.AddRangeAsync(It.IsAny<IEnumerable<LegalAcceptance>>(), default), Times.Never);
    }

    [Fact]
    public async Task EnsureCurrent_IgnoresNullAndUnpublishedDocuments()
    {
        _documents.Setup(r => r.GetCurrentAsync(It.IsAny<LegalDocumentType>(), default)).ReturnsAsync((LegalDocument?)null);

        await _sut.EnsureCurrentAsync(7, 9);
        await _sut.EnsureCurrentAsync(null, null);
    }
}
