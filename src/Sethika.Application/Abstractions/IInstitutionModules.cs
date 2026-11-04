namespace Sethika.Application.Abstractions;

/// <summary>Switches on a new institution's modules, set-based, inside the caller's transaction, and says which are on.</summary>
public interface IInstitutionModules
{
    /// <summary>The module <c>SERVICES</c>: religious services (Phase E, V12). Not core.</summary>
    const string Services = "SERVICES";

    /// <summary>Whether the institution has the active module <paramref name="moduleCode"/> switched on.</summary>
    Task<bool> IsOnAsync(int institutionId, string moduleCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables every core module of the institution portal and the institution type's defaults
    /// (<c>Source = 'Default'</c>), and the plan's modules (<c>'Plan'</c>) - the requested plan, or
    /// FREE when none was asked for (plan 02, B6 step 5).
    /// </summary>
    /// <returns>How many modules were enabled.</returns>
    Task<int> EnableForNewInstitutionAsync(
        int institutionId,
        short institutionTypeId,
        short? planId,
        Guid enabledBy,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables the core modules and the type's defaults the institution has no row for: a module added
    /// to the catalogue after it was approved (plan 09, Q4). A module with a row is left as it is, so
    /// one the institution or the platform switched off stays off. Plan modules are not touched.
    /// </summary>
    /// <returns>How many modules were enabled.</returns>
    Task<int> EnableMissingDefaultsAsync(
        int institutionId,
        short institutionTypeId,
        Guid enabledBy,
        string reason,
        CancellationToken cancellationToken = default);
}
