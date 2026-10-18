namespace Sethika.Application.Access;

/// <summary>
/// The codes in <c>core.LegalDocuments</c>, and which of them a person accepts where. Each flow asks
/// only for its own: a donor signing up never sees the declaration an institution's applicant makes.
/// </summary>
public static class LegalDocumentCodes
{
    public const string Terms = "TERMS";
    public const string Privacy = "PRIVACY";

    /// <summary>Accepted when an institution application is submitted (plan 02, B3).</summary>
    public const string InstitutionDeclaration = "INSTITUTION_DECLARATION";

    /// <summary>What a donor accepts at signup.</summary>
    public static IReadOnlyList<string> Signup { get; } = [Terms, Privacy];
}
