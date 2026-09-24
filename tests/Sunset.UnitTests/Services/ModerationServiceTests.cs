using Moq;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Services;

public class ModerationServiceTests
{
    private readonly Mock<IReportRepository> _reportRepository = new();
    private readonly Mock<IModerationActionRepository> _moderationActionRepository = new();
    private readonly Mock<ILegalDocumentRepository> _legalDocumentRepository = new();
    private readonly Mock<IUserRepository> _userRepository = new();
    private readonly Mock<IPhotoRepository> _photoRepository = new();
    private readonly ModerationService _sut;

    public ModerationServiceTests()
    {
        _sut = new ModerationService(
            _reportRepository.Object,
            _moderationActionRepository.Object,
            _legalDocumentRepository.Object,
            _userRepository.Object,
            _photoRepository.Object);
    }

    private static Report CreateReport(Guid reporterId, User reporter, ReportTargetType targetType, Guid targetId, ReportReason reason = ReportReason.Spam)
    {
        var report = new Report(reporterId, targetType, targetId, reason);
        typeof(Report).GetProperty(nameof(Report.Reporter))!.SetValue(report, reporter);
        return report;
    }

    [Fact]
    public async Task CreateReportAsync_WhenTargetPhotoDoesNotExist_ThrowsNotFoundException()
    {
        var reporter = new User("Ana", "ana@sunset.com", "hashed");
        _photoRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Photo?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CreateReportAsync(reporter.Id, ReportTargetType.Photo, Guid.NewGuid(), new CreateReportRequest(ReportReason.Spam)));

        _reportRepository.Verify(r => r.AddAsync(It.IsAny<Report>(), default), Times.Never);
    }

    [Fact]
    public async Task CreateReportAsync_WhenAlreadyReportedByThisUser_ThrowsConflictException()
    {
        var reporter = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var photo = new Photo(Guid.NewGuid(), location.Id, "https://sunset.com/photo.jpg", null);

        _photoRepository.Setup(r => r.GetByIdAsync(photo.Id, default)).ReturnsAsync(photo);
        _reportRepository.Setup(r => r.ExistsAsync(reporter.Id, ReportTargetType.Photo, photo.Id, default)).ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.CreateReportAsync(reporter.Id, ReportTargetType.Photo, photo.Id, new CreateReportRequest(ReportReason.Spam)));

        _reportRepository.Verify(r => r.AddAsync(It.IsAny<Report>(), default), Times.Never);
    }

    [Fact]
    public async Task CreateReportAsync_WithNewReport_AddsItAndReturnsResponse()
    {
        var reporter = new User("Ana", "ana@sunset.com", "hashed");
        var location = new Location("Praia do Rosa", -28.13, -48.62, "Imbituba");
        var photo = new Photo(Guid.NewGuid(), location.Id, "https://sunset.com/photo.jpg", null);
        var request = new CreateReportRequest(ReportReason.Harassment, "Comentário ofensivo");

        _photoRepository.Setup(r => r.GetByIdAsync(photo.Id, default)).ReturnsAsync(photo);
        _reportRepository.Setup(r => r.ExistsAsync(reporter.Id, ReportTargetType.Photo, photo.Id, default)).ReturnsAsync(false);

        Report? added = null;
        _reportRepository
            .Setup(r => r.AddAsync(It.IsAny<Report>(), default))
            .Callback<Report, CancellationToken>((r, _) => added = r)
            .Returns(Task.CompletedTask);
        _reportRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync(() => CreateReport(reporter.Id, reporter, ReportTargetType.Photo, photo.Id, ReportReason.Harassment));

        var response = await _sut.CreateReportAsync(reporter.Id, ReportTargetType.Photo, photo.Id, request);

        Assert.NotNull(added);
        Assert.Equal(ReportReason.Harassment, response.Reason);
        Assert.Equal(ReportStatus.Pending, response.Status);
        Assert.Equal("Ana", response.ReporterName);
    }

    [Fact]
    public async Task ResolveReportAsync_MarksResolvedAndLogsModerationAction()
    {
        var reporter = new User("Ana", "ana@sunset.com", "hashed");
        var report = CreateReport(reporter.Id, reporter, ReportTargetType.Photo, Guid.NewGuid());
        var moderatorId = Guid.NewGuid();

        _reportRepository.Setup(r => r.GetByIdAsync(report.Id, default)).ReturnsAsync(report);

        var response = await _sut.ResolveReportAsync(moderatorId, report.Id, new ResolveReportRequest(ReportStatus.Resolved));

        Assert.Equal(ReportStatus.Resolved, response.Status);
        Assert.Equal(moderatorId, report.ResolvedByUserId);
        _moderationActionRepository.Verify(
            r => r.AddAsync(It.Is<ModerationAction>(a => a.ModeratorId == moderatorId && a.ActionType == ModerationActionType.ReportResolved), default),
            Times.Once);
    }

    [Fact]
    public async Task ResolveReportAsync_WithUnknownReport_ThrowsNotFoundException()
    {
        _reportRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Report?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.ResolveReportAsync(Guid.NewGuid(), Guid.NewGuid(), new ResolveReportRequest(ReportStatus.Dismissed)));
    }

    [Fact]
    public async Task UpdateLegalDocumentAsync_WhenNoneExistYet_CreatesVersionOne()
    {
        var adminId = Guid.NewGuid();
        _legalDocumentRepository.Setup(r => r.GetCurrentAsync(LegalDocumentType.TermsOfService, default)).ReturnsAsync((LegalDocument?)null);

        var response = await _sut.UpdateLegalDocumentAsync(adminId, LegalDocumentType.TermsOfService, new UpdateLegalDocumentRequest("Termos v1"));

        Assert.Equal(1, response.Version);
        _legalDocumentRepository.Verify(r => r.AddAsync(It.Is<LegalDocument>(t => t.Version == 1), default), Times.Once);
        _moderationActionRepository.Verify(
            r => r.AddAsync(It.Is<ModerationAction>(a => a.ActionType == ModerationActionType.LegalDocumentUpdated), default),
            Times.Once);
    }

    [Fact]
    public async Task UpdateLegalDocumentAsync_WhenAVersionExists_IncrementsIt()
    {
        var adminId = Guid.NewGuid();
        var current = new LegalDocument(LegalDocumentType.TermsOfService, "Termos v1", 1, adminId);
        _legalDocumentRepository.Setup(r => r.GetCurrentAsync(LegalDocumentType.TermsOfService, default)).ReturnsAsync(current);

        var response = await _sut.UpdateLegalDocumentAsync(adminId, LegalDocumentType.TermsOfService, new UpdateLegalDocumentRequest("Termos v2"));

        Assert.Equal(2, response.Version);
    }

    [Fact]
    public async Task UpdateLegalDocumentAsync_VersionsPrivacyPolicyIndependentlyFromTerms()
    {
        var adminId = Guid.NewGuid();
        var currentTerms = new LegalDocument(LegalDocumentType.TermsOfService, "Termos v3", 3, adminId);
        _legalDocumentRepository.Setup(r => r.GetCurrentAsync(LegalDocumentType.TermsOfService, default)).ReturnsAsync(currentTerms);
        _legalDocumentRepository.Setup(r => r.GetCurrentAsync(LegalDocumentType.PrivacyPolicy, default)).ReturnsAsync((LegalDocument?)null);

        var response = await _sut.UpdateLegalDocumentAsync(adminId, LegalDocumentType.PrivacyPolicy, new UpdateLegalDocumentRequest("Privacidade v1"));

        Assert.Equal(1, response.Version);
        Assert.Equal(LegalDocumentType.PrivacyPolicy, response.DocumentType);
    }

    [Fact]
    public async Task ChangeUserRoleAsync_UpdatesRoleAndLogsModerationAction()
    {
        var adminId = Guid.NewGuid();
        var target = new User("Ana", "ana@sunset.com", "hashed");
        _userRepository.Setup(r => r.GetByIdAsync(target.Id, default)).ReturnsAsync(target);

        var response = await _sut.ChangeUserRoleAsync(adminId, target.Id, new ChangeUserRoleRequest(UserRole.Moderator));

        Assert.Equal(UserRole.Moderator, response.Role);
        _userRepository.Verify(r => r.SaveChangesAsync(default), Times.Once);
        _moderationActionRepository.Verify(
            r => r.AddAsync(It.Is<ModerationAction>(a => a.ModeratorId == adminId && a.ActionType == ModerationActionType.UserRoleChanged), default),
            Times.Once);
    }

    [Fact]
    public async Task ChangeUserRoleAsync_WithUnknownUser_ThrowsNotFoundException()
    {
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((User?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.ChangeUserRoleAsync(Guid.NewGuid(), Guid.NewGuid(), new ChangeUserRoleRequest(UserRole.Moderator)));
    }
}
