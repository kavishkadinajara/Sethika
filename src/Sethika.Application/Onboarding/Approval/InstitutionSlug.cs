using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sethika.Application.Onboarding.Approval;

/// <summary>
/// The URL name of a new institution (plan 02, B6 step 2): <c>sri-sudharmaramaya-galle</c>.
/// </summary>
public static partial class InstitutionSlug
{
    /// <summary>Short of the column's 150, to leave room for a <c>-2</c> and beyond.</summary>
    public const int MaxLength = 120;

    /// <summary>
    /// Lower-case ASCII letters and digits from the name and the city, joined by single hyphens, with
    /// accents dropped. <see langword="null"/> when the name has no Latin letters at all: a slug of
    /// the city alone would say nothing about which institution it is, so the caller uses the code.
    /// </summary>
    public static string? From(string name, string? city)
    {
        ArgumentNullException.ThrowIfNull(name);

        var namePart = Simplify(name);
        if (!namePart.Any(char.IsAsciiLetterLower))
        {
            return null;
        }

        var slug = string.IsNullOrWhiteSpace(city) ? namePart : $"{namePart}-{Simplify(city)}".TrimEnd('-');

        return slug.Length <= MaxLength ? slug : slug[..MaxLength].TrimEnd('-');
    }

    /// <summary>The first of <paramref name="slug"/>, <c>slug-2</c>, <c>slug-3</c>, ... not in <paramref name="taken"/>.</summary>
    public static string FirstFree(string slug, IReadOnlyCollection<string> taken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentNullException.ThrowIfNull(taken);

        var used = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (!used.Contains(slug))
        {
            return slug;
        }

        for (var n = 2; ; n++)
        {
            var candidate = string.Create(CultureInfo.InvariantCulture, $"{slug}-{n}");
            if (!used.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    private static string Simplify(string text)
    {
        var builder = new StringBuilder(text.Length);

        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            builder.Append(char.IsAsciiLetterLower(lower) || char.IsAsciiDigit(lower) ? lower : '-');
        }

        return Hyphens().Replace(builder.ToString(), "-").Trim('-');
    }

    [GeneratedRegex("-{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Hyphens();
}
