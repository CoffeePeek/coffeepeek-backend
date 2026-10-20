using CoffeePeek.Account.Domain.Entities.Notifications;
using CoffeePeek.Shared.Kernel.Exceptions;

namespace CoffeePeek.Account.Application.Features.Notifications;

public static class NotificationTemplateRenderer
{
    public static PushNotificationTemplate Default(PushNotificationCategory category) => category switch
    {
        PushNotificationCategory.Moderation => new()
        {
            Category = category, Title = "Решение модерации",
            Body = "Решение по модерации {subject} изменилось. Откройте приложение, чтобы посмотреть подробности."
        },
        PushNotificationCategory.Inactivity => new()
        {
            Category = category, Title = "Время для кофе?",
            Body = "Давно не виделись! Загляните в CoffeePeek и поделитесь новым чекином."
        },
        _ => throw new ValidationException("Only Moderation and Inactivity have automatic templates.")
    };

    public static (string Title, string Body) Render(PushNotificationTemplate template, string subject = "", string status = "")
    {
        var title = Replace(template.Title);
        var body = Replace(template.Body);
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120 || string.IsNullOrWhiteSpace(body) || body.Length > 500)
            throw new ValidationException("Rendered title and body must contain 1–120 and 1–500 characters.");
        return (title.Trim(), body.Trim());

        string Replace(string value)
        {
            if (template.Category == PushNotificationCategory.Moderation)
                value = value.Replace("{subject}", subject, StringComparison.Ordinal).Replace("{status}", status, StringComparison.Ordinal);
            if (value.Contains('{') || value.Contains('}'))
                throw new ValidationException("Unknown template placeholder. Moderation supports {subject} and {status}; Inactivity has no placeholders.");
            return value;
        }
    }

    public static void Validate(PushNotificationTemplate template)
    {
        _ = Default(template.Category);
        // The longest supported subject/status must also fit after placeholder expansion.
        _ = Render(template, "исправления", "не подтверждено");
    }

    public static string StatusText(string status) => status switch
    {
        "Approved" => "одобрено", "Rejected" => "отклонено", "Pending" => "на проверке",
        "Reviewed" => "рассмотрено", "Fixed" => "исправлено", "Invalid" => "не подтверждено",
        "CheckInDeleted" => "чекин удалён", "Dismissed" => "отклонено", _ => "изменено"
    };
}
