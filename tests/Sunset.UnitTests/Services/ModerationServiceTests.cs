using Moq;
using Sunset.Application.DTOs.Moderation;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces;
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
    private readonly Mock<IRefreshTokenRepository> _refreshTokenRepository = new();
    private readonly Mock<IEmailSender> _emailSender = new();
    private readonly Mock<IBanStatusService> _banStatusService = new();
    private readonly Mock<ILocationRepository> _locationRepository = new();
    private readonly Mock<IPasswordHasher> _passwordHasher = new();
    private readonly Mock<ICityRepository> _cityRepository = new();
    private readonly Mock<IResponseCacheInvalidator> _cache = new();
    private readonly ModerationService _sut;

    public ModerationServiceTests()
    {
        _locationRepository.Setup(r => r.GetStatsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), default))
            .ReturnsAsync(new Dictionary<Guid, Sunset.Application.DTOs.Locations.LocationStats>());

        _sut = new ModerationService(
            _reportRepository.Object,
            _moderationActionRepository.Object,
            _legalDocumentRepository.Object,
            _userRepository.Object,
            _photoRepository.Object,
            _refreshTokenRepository.Object,
            _emailSender.Object,
            _banStatusService.Object,
            _locationRepository.Object,
            _passwordHasher.Object,
            _cityRepository.Object,
            new Sunset.Application.Cities.CityResolver(_cityRepository.Object),
            _cache.Object);
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

    private User SetupUser(UserRole role = UserRole.User)
    {
        var user = new User("Fulano", $"{Guid.NewGuid():N}@sunset.com", "hashed");
        if (role != UserRole.User)
            user.ChangeRole(role);
        _userRepository.Setup(r => r.GetByIdAsync(user.Id, default)).ReturnsAsync(user);
        return user;
    }

    [Fact]
    public async Task BanUserAsync_BansRevokesSessionsInvalidatesCacheLogsAndEmails()
    {
        var moderator = SetupUser(UserRole.Moderator);
        var target = SetupUser();
        var until = DateTime.UtcNow.AddDays(7);

        await _sut.BanUserAsync(moderator.Id, target.Id, new BanUserRequest("  Spam repetido  ", until));

        Assert.True(target.IsBanned(DateTime.UtcNow));
        Assert.Equal("Spam repetido", target.BanReason);
        Assert.Equal(moderator.Id, target.BannedByUserId);
        _userRepository.Verify(r => r.SaveChangesAsync(default), Times.Once);
        _refreshTokenRepository.Verify(r => r.RevokeAllForUserAsync(target.Id, default), Times.Once);
        _banStatusService.Verify(s => s.Invalidate(target.Id), Times.Once);
        _moderationActionRepository.Verify(
            r => r.AddAsync(It.Is<ModerationAction>(a => a.ModeratorId == moderator.Id && a.ActionType == ModerationActionType.UserBanned), default),
            Times.Once);
        _emailSender.Verify(e => e.SendAsync(target.Email, It.IsAny<string>(), It.Is<string>(b => b.Contains("Spam repetido")), default), Times.Once);
    }

    [Fact]
    public async Task BanUserAsync_WhenEmailFails_StillBans()
    {
        var moderator = SetupUser(UserRole.Moderator);
        var target = SetupUser();
        _emailSender
            .Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default))
            .ThrowsAsync(new ExternalServiceException("smtp down"));

        await _sut.BanUserAsync(moderator.Id, target.Id, new BanUserRequest("Abuso"));

        Assert.True(target.IsBanned(DateTime.UtcNow));
        Assert.Null(target.BannedUntil);
    }

    [Fact]
    public async Task BanUserAsync_Self_IsForbidden()
    {
        var moderator = SetupUser(UserRole.Moderator);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.BanUserAsync(moderator.Id, moderator.Id, new BanUserRequest("Abuso")));
    }

    [Theory]
    [InlineData(UserRole.Moderator, UserRole.Admin)]
    [InlineData(UserRole.Admin, UserRole.Admin)]
    [InlineData(UserRole.Moderator, UserRole.Moderator)]
    public async Task BanUserAsync_OnPeerOrAdmin_IsForbidden(UserRole actorRole, UserRole targetRole)
    {
        var actor = SetupUser(actorRole);
        var target = SetupUser(targetRole);

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            _sut.BanUserAsync(actor.Id, target.Id, new BanUserRequest("Abuso")));

        Assert.False(target.IsBanned(DateTime.UtcNow));
        _refreshTokenRepository.Verify(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), default), Times.Never);
    }

    [Fact]
    public async Task BanUserAsync_AdminCanBanModerator()
    {
        var admin = SetupUser(UserRole.Admin);
        var target = SetupUser(UserRole.Moderator);

        await _sut.BanUserAsync(admin.Id, target.Id, new BanUserRequest("Abuso de poder"));

        Assert.True(target.IsBanned(DateTime.UtcNow));
    }

    [Fact]
    public async Task BanUserAsync_WithUnknownUser_ThrowsNotFoundException()
    {
        var moderator = SetupUser(UserRole.Moderator);
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((User?)null);
        _userRepository.Setup(r => r.GetByIdAsync(moderator.Id, default)).ReturnsAsync(moderator);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.BanUserAsync(moderator.Id, Guid.NewGuid(), new BanUserRequest("Abuso")));
    }

    [Fact]
    public async Task UnbanUserAsync_LiftsBanInvalidatesCacheAndLogs()
    {
        var moderator = SetupUser(UserRole.Moderator);
        var target = SetupUser();
        target.Ban("Spam", null, moderator.Id, DateTime.UtcNow);

        await _sut.UnbanUserAsync(moderator.Id, target.Id);

        Assert.False(target.IsBanned(DateTime.UtcNow));
        Assert.Null(target.BanReason);
        _banStatusService.Verify(s => s.Invalidate(target.Id), Times.Once);
        _moderationActionRepository.Verify(
            r => r.AddAsync(It.Is<ModerationAction>(a => a.ActionType == ModerationActionType.UserUnbanned), default),
            Times.Once);
    }

    [Fact]
    public async Task DeleteLocationAsync_HidesLocationAndPhotosWithSameInstantAndLogs()
    {
        var moderatorId = Guid.NewGuid();
        var location = new Location("Praia do Rosa", -28.1, -48.6, "Imbituba");
        _locationRepository.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        DateTime? photosStamp = null;
        _photoRepository
            .Setup(r => r.SoftDeleteAllByLocationAsync(location.Id, moderatorId, It.IsAny<DateTime>(), default))
            .Callback<Guid, Guid, DateTime, CancellationToken>((_, _, at, _) => photosStamp = at)
            .ReturnsAsync(3);

        await _sut.DeleteLocationAsync(moderatorId, location.Id);

        Assert.NotNull(location.DeletedAt);
        Assert.Equal(moderatorId, location.DeletedByUserId);
        Assert.Equal(location.DeletedAt, photosStamp);
        Assert.Equal(0, location.DeletedAt!.Value.Ticks % 10);
        _locationRepository.Verify(r => r.SaveChangesAsync(default), Times.Once);
        _moderationActionRepository.Verify(
            r => r.AddAsync(It.Is<ModerationAction>(a => a.ActionType == ModerationActionType.LocationDeleted && a.Notes!.Contains("3 photo")), default),
            Times.Once);
    }

    [Fact]
    public async Task DeleteLocationAsync_WithUnknownLocation_ThrowsNotFoundException()
    {
        _locationRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Location?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.DeleteLocationAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task RestoreLocationAsync_RestoresLocationAndOnlyItsCascadedPhotos()
    {
        var moderatorId = Guid.NewGuid();
        var location = new Location("Praia do Rosa", -28.1, -48.6, "Imbituba");
        var deletedAt = new DateTime(2026, 10, 29, 12, 0, 0, DateTimeKind.Utc);
        location.SoftDelete(moderatorId, deletedAt);
        _locationRepository.Setup(r => r.GetByIdIncludingDeletedAsync(location.Id, default)).ReturnsAsync(location);
        _photoRepository.Setup(r => r.RestoreAllByLocationAsync(location.Id, deletedAt, default)).ReturnsAsync(2);

        await _sut.RestoreLocationAsync(moderatorId, location.Id);

        Assert.Null(location.DeletedAt);
        _photoRepository.Verify(r => r.RestoreAllByLocationAsync(location.Id, deletedAt, default), Times.Once);
        _moderationActionRepository.Verify(
            r => r.AddAsync(It.Is<ModerationAction>(a => a.ActionType == ModerationActionType.LocationRestored), default),
            Times.Once);
    }

    [Fact]
    public async Task RestoreLocationAsync_WhenNotDeleted_ThrowsConflictException()
    {
        var location = new Location("Praia do Rosa", -28.1, -48.6, "Imbituba");
        _locationRepository.Setup(r => r.GetByIdIncludingDeletedAsync(location.Id, default)).ReturnsAsync(location);

        await Assert.ThrowsAsync<ConflictException>(() => _sut.RestoreLocationAsync(Guid.NewGuid(), location.Id));
    }

    [Fact]
    public async Task UpdateLocationCoordinatesAsync_ChangesPinAndLogsOldAndNewValues()
    {
        var moderatorId = Guid.NewGuid();
        var location = new Location("Praia do Rosa", -28.1, -48.6, "Imbituba");
        _locationRepository.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);

        var response = await _sut.UpdateLocationCoordinatesAsync(moderatorId, location.Id, new UpdateLocationCoordinatesRequest(-28.2, -48.7));

        Assert.Equal(-28.2, response.Latitude);
        Assert.Equal(-48.7, location.Longitude);
        _locationRepository.Verify(r => r.SaveChangesAsync(default), Times.Once);
        _moderationActionRepository.Verify(
            r => r.AddAsync(It.Is<ModerationAction>(a =>
                a.ActionType == ModerationActionType.LocationCoordinatesChanged &&
                a.Notes == "(-28.1, -48.6) -> (-28.2, -48.7)"), default),
            Times.Once);
    }

    [Fact]
    public async Task UpdateLocationCoordinatesAsync_WithUnknownLocation_ThrowsNotFoundException()
    {
        _locationRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Location?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.UpdateLocationCoordinatesAsync(Guid.NewGuid(), Guid.NewGuid(), new UpdateLocationCoordinatesRequest(0, 0)));
    }

    [Fact]
    public async Task AnonymizeUserAsync_ErasesAccountBansRevokesAuditsAndNotifiesOriginalAddress()
    {
        var admin = SetupUser(UserRole.Admin);
        var target = SetupUser();
        var originalEmail = target.Email;
        _passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("unusable");

        await _sut.AnonymizeUserAsync(admin.Id, target.Id, new AnonymizeUserRequest("Conteudo ilegal"));

        Assert.Equal("Usuário excluído", target.Name);
        Assert.EndsWith("@sunset.invalid", target.Email);
        Assert.Equal("unusable", target.PasswordHash);
        Assert.True(target.IsBanned(DateTime.UtcNow));
        _refreshTokenRepository.Verify(r => r.RevokeAllForUserAsync(target.Id, default), Times.Once);
        _banStatusService.Verify(s => s.Invalidate(target.Id), Times.Once);
        _photoRepository.Verify(r => r.SoftDeleteAllByUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), default), Times.Never);
        _moderationActionRepository.Verify(
            r => r.AddAsync(It.Is<ModerationAction>(a => a.ActionType == ModerationActionType.UserAnonymized && a.Notes!.Contains("content kept")), default),
            Times.Once);
        _emailSender.Verify(e => e.SendAsync(originalEmail, It.IsAny<string>(), It.IsAny<string>(), default), Times.Once);
    }

    [Fact]
    public async Task AnonymizeUserAsync_WithHideContent_HidesPhotosAndCommentsAdjustingCounters()
    {
        var admin = SetupUser(UserRole.Admin);
        var target = SetupUser();
        _passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("unusable");

        var otherPhoto = new Photo(Guid.NewGuid(), Guid.NewGuid(), "https://x/p.jpg", null);
        otherPhoto.IncrementCommentsCount();
        otherPhoto.IncrementCommentsCount();
        otherPhoto.IncrementCommentsCount();
        var root = new Comment(target.Id, otherPhoto.Id, "root");
        root.IncrementRepliesCount();
        var ownReply = new Comment(target.Id, otherPhoto.Id, "reply", root.Id);
        var strangerReply = new Comment(Guid.NewGuid(), otherPhoto.Id, "outra pessoa", root.Id);
        // root has 2 replies in the denormalized counter: the user's own and a stranger's
        root.IncrementRepliesCount();
        _photoRepository.Setup(r => r.GetCommentsByUserAsync(target.Id, default)).ReturnsAsync(new[] { ownReply, root });
        _photoRepository.Setup(r => r.GetAllRepliesAsync(root.Id, default)).ReturnsAsync(new[] { ownReply, strangerReply });
        _photoRepository.Setup(r => r.GetByIdAsync(otherPhoto.Id, default)).ReturnsAsync(otherPhoto);
        _photoRepository.Setup(r => r.SoftDeleteAllByUserAsync(target.Id, admin.Id, It.IsAny<DateTime>(), default)).ReturnsAsync(4);

        await _sut.AnonymizeUserAsync(admin.Id, target.Id, new AnonymizeUserRequest("Spam em massa", HideContent: true));

        Assert.NotNull(root.DeletedAt);
        Assert.NotNull(ownReply.DeletedAt);
        Assert.NotNull(strangerReply.DeletedAt);
        // 3 comments were counted on the photo (root + 2 replies); all three are gone
        Assert.Equal(0, otherPhoto.CommentsCount);
        _moderationActionRepository.Verify(
            r => r.AddAsync(It.Is<ModerationAction>(a => a.Notes!.Contains("hidden 4 photo(s) and 3 comment(s)")), default),
            Times.Once);
    }

    [Fact]
    public async Task AnonymizeUserAsync_OnAdminOrSelf_IsForbidden()
    {
        var admin = SetupUser(UserRole.Admin);
        var otherAdmin = SetupUser(UserRole.Admin);

        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.AnonymizeUserAsync(admin.Id, otherAdmin.Id, new AnonymizeUserRequest("x")));
        await Assert.ThrowsAsync<ForbiddenException>(() => _sut.AnonymizeUserAsync(admin.Id, admin.Id, new AnonymizeUserRequest("x")));
    }

    [Fact]
    public async Task AnonymizeUserAsync_WhenAlreadyDeleted_ThrowsConflictException()
    {
        var admin = SetupUser(UserRole.Admin);
        var target = SetupUser();
        _passwordHasher.Setup(h => h.Hash(It.IsAny<string>())).Returns("unusable");
        target.Anonymize($"deleted-{target.Id:N}@sunset.invalid", "unusable");

        await Assert.ThrowsAsync<ConflictException>(() => _sut.AnonymizeUserAsync(admin.Id, target.Id, new AnonymizeUserRequest("x")));
    }

    [Fact]
    public async Task CreateReportAsync_OnExistingLocation_AddsReport()
    {
        var reporter = SetupUser();
        var location = new Location("Praia do Rosa", -28.1, -48.6, "Imbituba");
        _locationRepository.Setup(r => r.GetByIdAsync(location.Id, default)).ReturnsAsync(location);
        _reportRepository.Setup(r => r.ExistsAsync(reporter.Id, ReportTargetType.Location, location.Id, default)).ReturnsAsync(false);
        _reportRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid id, CancellationToken _) => CreateReport(reporter.Id, reporter, ReportTargetType.Location, location.Id, ReportReason.IncorrectInfo));

        var response = await _sut.CreateReportAsync(reporter.Id, ReportTargetType.Location, location.Id, new CreateReportRequest(ReportReason.IncorrectInfo));

        Assert.Equal(ReportTargetType.Location, response.TargetType);
        _reportRepository.Verify(r => r.AddAsync(It.Is<Report>(x => x.TargetType == ReportTargetType.Location && x.TargetId == location.Id), default), Times.Once);
    }

    [Fact]
    public async Task CreateReportAsync_OnUnknownOrDeletedLocation_ThrowsNotFoundException()
    {
        var reporter = SetupUser();
        _locationRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Location?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CreateReportAsync(reporter.Id, ReportTargetType.Location, Guid.NewGuid(), new CreateReportRequest(ReportReason.Spam)));
    }

    [Fact]
    public async Task CreateReportAsync_OnExistingUser_AddsReport()
    {
        var reporter = SetupUser();
        var target = SetupUser();
        _reportRepository.Setup(r => r.ExistsAsync(reporter.Id, ReportTargetType.User, target.Id, default)).ReturnsAsync(false);
        _reportRepository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default))
            .ReturnsAsync((Guid id, CancellationToken _) => CreateReport(reporter.Id, reporter, ReportTargetType.User, target.Id, ReportReason.Harassment));

        var response = await _sut.CreateReportAsync(reporter.Id, ReportTargetType.User, target.Id, new CreateReportRequest(ReportReason.Harassment));

        Assert.Equal(ReportTargetType.User, response.TargetType);
        _reportRepository.Verify(r => r.AddAsync(It.Is<Report>(x => x.TargetType == ReportTargetType.User && x.TargetId == target.Id), default), Times.Once);
    }

    [Fact]
    public async Task CreateReportAsync_OnYourself_ThrowsConflictException()
    {
        var reporter = SetupUser();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.CreateReportAsync(reporter.Id, ReportTargetType.User, reporter.Id, new CreateReportRequest(ReportReason.Spam)));
    }

    [Fact]
    public async Task CreateReportAsync_OnAnonymizedUser_ThrowsNotFoundException()
    {
        var reporter = SetupUser();
        var gone = SetupUser();
        gone.Anonymize($"deleted-{gone.Id:N}@sunset.invalid", "unusable");

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CreateReportAsync(reporter.Id, ReportTargetType.User, gone.Id, new CreateReportRequest(ReportReason.Spam)));
    }

    [Fact]
    public async Task CreateReportAsync_OnUnknownUser_ThrowsNotFoundException()
    {
        var reporter = SetupUser();
        _userRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((User?)null);
        _userRepository.Setup(r => r.GetByIdAsync(reporter.Id, default)).ReturnsAsync(reporter);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.CreateReportAsync(reporter.Id, ReportTargetType.User, Guid.NewGuid(), new CreateReportRequest(ReportReason.Spam)));
    }

    [Fact]
    public async Task MergeLocationsAsync_MovesPhotosAndRatingsRecalculatesAndRemovesSource()
    {
        var moderatorId = Guid.NewGuid();
        var source = new Location("Praia Rosa", -28.1, -48.6, "Imbituba");
        var target = new Location("Praia do Rosa", -28.12, -48.64, "Imbituba");
        _locationRepository.Setup(r => r.GetByIdAsync(source.Id, default)).ReturnsAsync(source);
        _locationRepository.Setup(r => r.GetByIdAsync(target.Id, default)).ReturnsAsync(target);
        _photoRepository.Setup(r => r.MoveAllToLocationAsync(source.Id, target.Id, default)).ReturnsAsync(4);
        _locationRepository.Setup(r => r.MoveRatingsAsync(source.Id, target.Id, default)).ReturnsAsync(2);
        _locationRepository.Setup(r => r.GetAverageRatingAsync(target.Id, default)).ReturnsAsync(4.25m);

        var response = await _sut.MergeLocationsAsync(moderatorId, source.Id, new MergeLocationsRequest(target.Id));

        Assert.Equal(target.Id, response.Id);
        Assert.Equal(4.25m, response.AvgRating);
        Assert.NotNull(source.DeletedAt);
        Assert.Equal(moderatorId, source.DeletedByUserId);
        Assert.Null(target.DeletedAt);
        _locationRepository.Verify(r => r.SaveChangesAsync(default), Times.Once);
        _moderationActionRepository.Verify(
            r => r.AddAsync(It.Is<ModerationAction>(a => a.ActionType == ModerationActionType.LocationMerged && a.Notes!.Contains("4 photo(s) and 2 rating(s)")), default),
            Times.Once);
    }

    [Fact]
    public async Task MergeLocationsAsync_IntoItself_ThrowsConflictException()
    {
        var id = Guid.NewGuid();

        await Assert.ThrowsAsync<ConflictException>(() =>
            _sut.MergeLocationsAsync(Guid.NewGuid(), id, new MergeLocationsRequest(id)));
    }

    [Fact]
    public async Task MergeLocationsAsync_WithUnknownSourceOrTarget_ThrowsNotFoundAndMovesNothing()
    {
        var source = new Location("Praia Rosa", -28.1, -48.6, "Imbituba");
        _locationRepository.Setup(r => r.GetByIdAsync(source.Id, default)).ReturnsAsync(source);
        _locationRepository.Setup(r => r.GetByIdAsync(It.Is<Guid>(g => g != source.Id), default)).ReturnsAsync((Location?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.MergeLocationsAsync(Guid.NewGuid(), source.Id, new MergeLocationsRequest(Guid.NewGuid())));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            _sut.MergeLocationsAsync(Guid.NewGuid(), Guid.NewGuid(), new MergeLocationsRequest(source.Id)));

        _photoRepository.Verify(r => r.MoveAllToLocationAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), default), Times.Never);
        Assert.Null(source.DeletedAt);
    }
}
