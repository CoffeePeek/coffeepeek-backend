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
        PushNotificationCategory.NewShop => new()
        {
            Category = category, Title = "Новая кофейня на карте",
            Body = "На карте появилась «{name}». Загляните за чашкой кофе!"
        },
        _ => throw new ValidationException("Only Moderation, Inactivity and NewShop have automatic templates.")
    };

    public static (string Title, string Body) Render(PushNotificationTemplate template, string subject = "", string status = "", string shopName = "")
    {
        var title = Replace(template.Title);
        var body = Replace(template.Body);
        if (string.IsNullOrWhiteSpace(title) || title.Length > 120 || string.IsNullOrWhiteSpace(body) || body.Length > 500)
            throw new ValidationException("Rendered title and body must contain 1–120 and 1–500 characters.");
        return (title.Trim(), body.Trim());

        string Replace(string value)
        {
            var known = template.Category switch
            {
                PushNotificationCategory.Moderation => value.Replace("{subject}", "", StringComparison.Ordinal).Replace("{status}", "", StringComparison.Ordinal),
                PushNotificationCategory.NewShop => value.Replace("{name}", "", StringComparison.Ordinal),
                _ => value
            };
            if (known.Contains('{') || known.Contains('}'))
                throw new ValidationException("Unknown template placeholder. Moderation supports {subject} and {status}; NewShop supports {name}; Inactivity has no placeholders.");
            if (template.Category == PushNotificationCategory.Moderation)
                value = value.Replace("{subject}", subject, StringComparison.Ordinal).Replace("{status}", status, StringComparison.Ordinal);
            if (template.Category == PushNotificationCategory.NewShop)
                value = value.Replace("{name}", shopName, StringComparison.Ordinal);
            return value;
        }
    }

    public static void Validate(PushNotificationTemplate template)
    {
        _ = Default(template.Category);
        // The longest supported subject/status must also fit after placeholder expansion.
        _ = Render(template, "исправления", "не подтверждено", new string('я', 300));
    }

    public static string StatusText(string status) => status switch
    {
        "Approved" => "одобрено", "Rejected" => "отклонено", "Pending" => "на проверке",
        "Reviewed" => "рассмотрено", "Fixed" => "исправлено", "Invalid" => "не подтверждено",
        "CheckInDeleted" => "чекин удалён", "Dismissed" => "отклонено", _ => "изменено"
    };
}
