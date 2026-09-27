namespace Sethika.Application.Access;

public sealed class PermissionSet
{
    private readonly HashSet<string> _codes;
    private readonly HashSet<string> _mfaCodes;

    public PermissionSet(IEnumerable<string> codes, IEnumerable<string> codesRequiringMfa)
    {
        ArgumentNullException.ThrowIfNull(codes);
        ArgumentNullException.ThrowIfNull(codesRequiringMfa);

        _codes = new HashSet<string>(codes, StringComparer.OrdinalIgnoreCase);
        _mfaCodes = new HashSet<string>(codesRequiringMfa, StringComparer.OrdinalIgnoreCase);
    }

    public static PermissionSet Empty { get; } = new([], []);
    public IReadOnlyCollection<string> Codes => _codes;


    public bool Allows(string permissionCode) =>
        !string.IsNullOrWhiteSpace(permissionCode) && _codes.Contains(permissionCode);


    public bool RequiresMfa(string permissionCode) =>
        !string.IsNullOrWhiteSpace(permissionCode) && _mfaCodes.Contains(permissionCode);
}
