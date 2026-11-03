namespace NotificationService.Api.Domain;

public enum NotificationStatus
{
    Pending,
    Queued,
    Processing,
    Sent,
    Failed
}
