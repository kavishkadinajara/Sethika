namespace Sethika.Application.Abstractions;

/// <summary>Switches on a new institution's modules, set-based, inside the caller's transaction.</summary>
public interface IInstitutionModules
{
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
}
