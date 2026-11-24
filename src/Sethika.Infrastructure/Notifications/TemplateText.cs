namespace Sethika.Infrastructure.Notifications;

/// <summary>Fills in a <c>notify.Templates</c> text.</summary>
internal static class TemplateText
{
    /// <summary><paramref name="text"/> with each <c>{name}</c> in <paramref name="values"/> replaced.</summary>
    public static string Fill(string text, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (name, value) in values)
        {
            text = text.Replace("{" + name + "}", value, StringComparison.Ordinal);
        }

        return text;
    }
}
