using Scheduling.Domain.Notifications;
using static Scheduling.UnitTests.TestData;

namespace Scheduling.UnitTests.Domain.Notifications;

public sealed class NotificationTests
{
    private static Notification Pending() =>
        Notification.Create(Guid.NewGuid(), Guid.NewGuid(), NotificationType.AppointmentCreated, "Assunto", "Corpo", Now);

    [Fact]
    public void Create_IsPendingAndDueImmediately()
    {
        var n = Pending();
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(0, n.Attempts);
        Assert.Equal(Now, n.NextAttemptAt);
        Assert.Equal(NotificationChannel.Email, n.Channel);
    }

    [Fact]
    public void MarkSent()
    {
        var n = Pending();
        n.MarkSent(Now);
        Assert.Equal(NotificationStatus.Sent, n.Status);
        Assert.Equal(Now, n.SentAt);
        Assert.Equal(1, n.Attempts);
        Assert.Null(n.NextAttemptAt);
    }

    [Fact]
    public void FailedAttempts_UseBackoffThenFail()
    {
        var n = Pending();

        n.MarkFailedAttempt("erro", Now, 3);
        Assert.Equal(NotificationStatus.Pending, n.Status);
        Assert.Equal(Now.AddMinutes(1), n.NextAttemptAt);

        n.MarkFailedAttempt("erro", Now, 3);
        Assert.Equal(Now.AddMinutes(5), n.NextAttemptAt);

        n.MarkFailedAttempt("erro", Now, 3);
        Assert.Equal(NotificationStatus.Failed, n.Status);
        Assert.Equal(3, n.Attempts);
        Assert.Null(n.NextAttemptAt);
        Assert.Equal("erro", n.LastError);
    }

    [Fact]
    public void Cancel_OnlyAffectsPending()
    {
        var pending = Pending();
        pending.Cancel();
        Assert.Equal(NotificationStatus.Cancelled, pending.Status);

        var sent = Pending();
        sent.MarkSent(Now);
        sent.Cancel();
        Assert.Equal(NotificationStatus.Sent, sent.Status);
    }

    [Fact]
    public void LongSubjectAndError_AreTruncated()
    {
        var n = Notification.Create(Guid.NewGuid(), Guid.NewGuid(), NotificationType.AppointmentCreated, new string('a', 300), "Corpo", Now);
        Assert.Equal(Notification.SubjectMaxLength, n.Subject.Length);

        n.MarkFailedAttempt(new string('e', 2000), Now, 3);
        Assert.Equal(Notification.LastErrorMaxLength, n.LastError!.Length);
    }
}
