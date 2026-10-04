namespace Sethika.Application.Access.Language;

/// <summary>The outcome of setting a user's preferred language.</summary>
public enum PreferredLanguageResult
{
    /// <summary>The language is stored; it may already have been.</summary>
    Saved = 1,

    /// <summary>Not one of <see cref="SupportedLanguages"/>. Nothing was written.</summary>
    UnsupportedLanguage = 2,

    /// <summary>The token names a user who no longer exists or has been deleted.</summary>
    UserNotFound = 3,

    /// <summary>The row kept changing underneath the write. Nothing was overwritten.</summary>
    Conflict = 4,
}
