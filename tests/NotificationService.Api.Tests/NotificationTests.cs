using NotificationService.Api.Domain;
using Xunit;

namespace NotificationService.Api.Tests;

public sealed class NotificationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private static Notification Create(int maxAttempts = 3) =>
        new("user@example.com", "Account notification", "Your account has been updated.",
            NotificationChannel.Email, Now, maxAttempts);

    [Fact]
    public void Create_InitializesPendingNotification()
    {
        var notification = Create();

        Assert.NotEqual(Guid.Empty, notification.Id);
        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal(0, notification.AttemptsCount);
        Assert.Equal(3, notification.MaxAttempts);
        Assert.Equal(Now, notification.CreatedAt);
        Assert.Equal(Now, notification.UpdatedAt);
        Assert.Null(notification.SentAt);
        Assert.Null(notification.LastError);
    }

    [Fact]
    public void SuccessfulDelivery_RecordsAttemptAndTimestamp()
    {
        var notification = Create();
        notification.MarkQueued(Now.AddSeconds(1));
        notification.BeginAttempt(Now.AddSeconds(2));
        notification.MarkSent(Now.AddSeconds(3));

        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Equal(1, notification.AttemptsCount);
        Assert.Equal(Now.AddSeconds(3), notification.SentAt);
        Assert.Equal(notification.SentAt, notification.UpdatedAt);
    }

    [Fact]
    public void TemporaryFailure_AllowsRetryAndClearsErrorAfterSuccess()
    {
        var notification = Create();
        notification.MarkQueued(Now);
        notification.BeginAttempt(Now);
        notification.FailAttempt("SMTP unavailable", true, Now.AddSeconds(1));

        Assert.Equal(NotificationStatus.Pending, notification.Status);
        Assert.Equal("SMTP unavailable", notification.LastError);
        Assert.Equal(1, notification.AttemptsCount);
        Assert.Null(notification.SentAt);

        notification.MarkQueued(Now.AddSeconds(2));
        notification.BeginAttempt(Now.AddSeconds(3));
        notification.MarkSent(Now.AddSeconds(4));

        Assert.Equal(2, notification.AttemptsCount);
        Assert.Equal(NotificationStatus.Sent, notification.Status);
        Assert.Null(notification.LastError);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void TemporaryFailures_StopAtAttemptLimit(int maxAttempts)
    {
        var notification = Create(maxAttempts);
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            notification.MarkQueued(Now);
            notification.BeginAttempt(Now);
            notification.FailAttempt("Timeout", true, Now);
        }

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(maxAttempts, notification.AttemptsCount);
        Assert.Equal("Timeout", notification.LastError);
        Assert.Throws<InvalidOperationException>(() => notification.MarkQueued(Now));
    }

    [Fact]
    public void PermanentFailure_DoesNotScheduleRetry()
    {
        var notification = Create();
        notification.MarkQueued(Now);
        notification.BeginAttempt(Now);
        notification.FailAttempt("Recipient rejected", false, Now);

        Assert.Equal(NotificationStatus.Failed, notification.Status);
        Assert.Equal(1, notification.AttemptsCount);
    }

    [Theory]
    [InlineData(NotificationStatus.Pending)]
    [InlineData(NotificationStatus.Queued)]
    [InlineData(NotificationStatus.Processing)]
    [InlineData(NotificationStatus.Sent)]
    [InlineData(NotificationStatus.Failed)]
    public void Transitions_RejectOperationsFromWrongStates(NotificationStatus status)
    {
        var notification = Create();
        if (status != NotificationStatus.Pending)
            notification.MarkQueued(Now);
        if (status is NotificationStatus.Processing or NotificationStatus.Sent or NotificationStatus.Failed)
            notification.BeginAttempt(Now);
        if (status == NotificationStatus.Sent)
            notification.MarkSent(Now);
        if (status == NotificationStatus.Failed)
            notification.FailAttempt("Rejected", false, Now);

        var attemptsBefore = notification.AttemptsCount;
        if (status != NotificationStatus.Pending)
            Assert.Throws<InvalidOperationException>(() => notification.MarkQueued(Now));
        if (status != NotificationStatus.Queued)
            Assert.Throws<InvalidOperationException>(() => notification.BeginAttempt(Now));
        if (status != NotificationStatus.Processing)
        {
            Assert.Throws<InvalidOperationException>(() => notification.MarkSent(Now));
            Assert.Throws<InvalidOperationException>(() => notification.FailAttempt("Error", true, Now));
        }

        Assert.Equal(status, notification.Status);
        Assert.Equal(attemptsBefore, notification.AttemptsCount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("invalid-address")]
    [InlineData("Person <user@example.com>")]
    [InlineData("one@example.com;two@example.com")]
    public void Create_RejectsInvalidRecipient(string? recipient)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new Notification(recipient!, "Subject", "Message", NotificationChannel.Email, Now));
    }

    [Theory]
    [InlineData(null, "Message")]
    [InlineData(" ", "Message")]
    [InlineData("Subject", null)]
    [InlineData("Subject", " ")]
    [InlineData("Injected\r\nHeader", "Message")]
    public void Create_RejectsInvalidContent(string? subject, string? message)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new Notification("user@example.com", subject!, message!, NotificationChannel.Email, Now));
    }

    [Fact]
    public void Create_EnforcesContentLengthLimits()
    {
        _ = new Notification("user@example.com", new string('s', 200), new string('m', 100_000),
            NotificationChannel.Email, Now);

        Assert.Throws<ArgumentException>(() =>
            new Notification("user@example.com", new string('s', 201), "Message", NotificationChannel.Email, Now));
        Assert.Throws<ArgumentException>(() =>
            new Notification("user@example.com", "Subject", new string('m', 100_001), NotificationChannel.Email, Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsInvalidAttemptLimit(int maxAttempts) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(maxAttempts));

    [Fact]
    public void Create_RejectsUnsupportedChannel() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Notification("user@example.com", "Subject", "Message", (NotificationChannel)42, Now));

    [Fact]
    public void EmptyError_DoesNotChangeProcessingNotification()
    {
        var notification = Create();
        notification.MarkQueued(Now);
        notification.BeginAttempt(Now);

        Assert.Throws<ArgumentException>(() => notification.FailAttempt(" ", true, Now));
        Assert.Equal(NotificationStatus.Processing, notification.Status);
        Assert.Equal(1, notification.AttemptsCount);
        Assert.Null(notification.LastError);
    }
}
