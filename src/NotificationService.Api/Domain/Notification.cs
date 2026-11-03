using System.Net.Mail;

namespace NotificationService.Api.Domain;

/// <summary>
/// Уведомление и правила его обработки. Не выполняет сетевых запросов.
/// </summary>
public sealed class Notification
{
    public Guid Id { get; }
    public string Recipient { get; }
    public string Subject { get; }
    public string Message { get; }
    public NotificationChannel Channel { get; }
    public NotificationStatus Status { get; private set; }
    public int AttemptsCount { get; private set; }
    public int MaxAttempts { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? SentAt { get; private set; }
    public string? LastError { get; private set; }

    public Notification(
        string recipient,
        string subject,
        string message,
        NotificationChannel channel,
        DateTimeOffset createdAt,
        int maxAttempts = 3)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (channel != NotificationChannel.Email)
            throw new ArgumentOutOfRangeException(nameof(channel), "Поддерживается только Email.");

        recipient = recipient.Trim();
        if (recipient.Length > 254 ||
            !MailAddress.TryCreate(recipient, out var address) ||
            !string.Equals(address.Address, recipient, StringComparison.Ordinal))
            throw new ArgumentException("Укажите один email-адрес без имени получателя.", nameof(recipient));

        if (subject.Length > 200 || subject.Contains('\r') || subject.Contains('\n'))
            throw new ArgumentException("Тема должна содержать до 200 символов без переносов строк.", nameof(subject));

        if (message.Length > 100_000)
            throw new ArgumentException("Текст должен содержать не более 100000 символов.", nameof(message));

        if (maxAttempts < 1)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts), "Нужна хотя бы одна попытка.");

        Id = Guid.NewGuid();
        Recipient = recipient;
        Subject = subject;
        Message = message;
        Channel = channel;
        Status = NotificationStatus.Pending;
        MaxAttempts = maxAttempts;
        CreatedAt = createdAt.ToUniversalTime();
        UpdatedAt = CreatedAt;
    }

    public void MarkQueued(DateTimeOffset now)
    {
        RequireStatus(NotificationStatus.Pending);
        Status = NotificationStatus.Queued;
        UpdatedAt = now.ToUniversalTime();
    }

    public void BeginAttempt(DateTimeOffset now)
    {
        RequireStatus(NotificationStatus.Queued);
        if (AttemptsCount >= MaxAttempts)
            throw new InvalidOperationException("Лимит попыток исчерпан.");

        AttemptsCount++;
        Status = NotificationStatus.Processing;
        UpdatedAt = now.ToUniversalTime();
    }

    public void MarkSent(DateTimeOffset now)
    {
        RequireStatus(NotificationStatus.Processing);
        Status = NotificationStatus.Sent;
        SentAt = now.ToUniversalTime();
        UpdatedAt = SentAt.Value;
        LastError = null;
    }

    /// <summary>
    /// Провайдер определяет, является ли ошибка временной.
    /// Планирование задержки и запись Outbox добавятся в компоненте обработки.
    /// </summary>
    public void FailAttempt(string error, bool retryable, DateTimeOffset now)
    {
        RequireStatus(NotificationStatus.Processing);
        ArgumentException.ThrowIfNullOrWhiteSpace(error);

        LastError = error;
        Status = retryable && AttemptsCount < MaxAttempts
            ? NotificationStatus.Pending
            : NotificationStatus.Failed;
        UpdatedAt = now.ToUniversalTime();
    }

    private void RequireStatus(NotificationStatus expected)
    {
        if (Status != expected)
            throw new InvalidOperationException(
                $"Операция требует статуса {expected}; текущий статус — {Status}.");
    }
}
