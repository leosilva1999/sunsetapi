using Moq;
using Sunset.Application.Common;
using Sunset.Application.Exceptions;
using Sunset.Application.Interfaces.Repositories;
using Sunset.Application.Services;
using Sunset.Domain.Entities;
using Sunset.Domain.Enums;

namespace Sunset.UnitTests.Services;

public class NotificationServiceTests
{
    private readonly Mock<INotificationRepository> _notificationRepository = new();
    private readonly NotificationService _sut;

    public NotificationServiceTests()
    {
        _sut = new NotificationService(_notificationRepository.Object);
    }

    private static Notification CreateNotification(Guid recipientId, Guid actorId, User actor)
    {
        var notification = new Notification(recipientId, NotificationType.NewFollower, actorId, $"User:{actorId}");
        typeof(Notification).GetProperty(nameof(Notification.Actor))!.SetValue(notification, actor);
        return notification;
    }

    [Fact]
    public async Task GetNotificationsAsync_ReturnsMappedPage()
    {
        var recipientId = Guid.NewGuid();
        var actor = new User("Ana", "ana@sunset.com", "hashed");
        var notification = CreateNotification(recipientId, actor.Id, actor);

        _notificationRepository
            .Setup(r => r.GetByUserIdAsync(recipientId, null, 20, default))
            .ReturnsAsync(new CursorPagedResult<Notification>([notification], null, false));

        var page = await _sut.GetNotificationsAsync(recipientId, null, 20);

        Assert.Single(page.Items);
        Assert.Equal(actor.Name, page.Items[0].ActorName);
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsCountFromRepository()
    {
        var recipientId = Guid.NewGuid();
        _notificationRepository.Setup(r => r.GetUnreadCountAsync(recipientId, default)).ReturnsAsync(3);

        var response = await _sut.GetUnreadCountAsync(recipientId);

        Assert.Equal(3, response.Count);
    }

    [Fact]
    public async Task MarkReadAsync_WithUnknownNotification_ThrowsNotFoundException()
    {
        _notificationRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), default)).ReturnsAsync((Notification?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _sut.MarkReadAsync(Guid.NewGuid(), Guid.NewGuid()));
    }

    [Fact]
    public async Task MarkReadAsync_WhenNotTheRecipient_ThrowsUnauthorizedActionException()
    {
        var actor = new User("Ana", "ana@sunset.com", "hashed");
        var notification = CreateNotification(Guid.NewGuid(), actor.Id, actor);
        _notificationRepository.Setup(r => r.GetByIdAsync(notification.Id, default)).ReturnsAsync(notification);

        await Assert.ThrowsAsync<UnauthorizedActionException>(() => _sut.MarkReadAsync(Guid.NewGuid(), notification.Id));
        _notificationRepository.Verify(r => r.SaveChangesAsync(default), Times.Never);
    }

    [Fact]
    public async Task MarkReadAsync_WhenTheRecipient_MarksReadAndSaves()
    {
        var actor = new User("Ana", "ana@sunset.com", "hashed");
        var recipientId = Guid.NewGuid();
        var notification = CreateNotification(recipientId, actor.Id, actor);
        _notificationRepository.Setup(r => r.GetByIdAsync(notification.Id, default)).ReturnsAsync(notification);

        await _sut.MarkReadAsync(recipientId, notification.Id);

        Assert.NotNull(notification.ReadAt);
        _notificationRepository.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task MarkAllReadAsync_DelegatesToRepository()
    {
        var recipientId = Guid.NewGuid();

        await _sut.MarkAllReadAsync(recipientId);

        _notificationRepository.Verify(r => r.MarkAllReadAsync(recipientId, default), Times.Once);
    }
}
