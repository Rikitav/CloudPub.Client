namespace CloudPub.Components;

/// <summary>
/// Stores an loads client info
/// </summary>
public interface IAuthFacility
{
    /// <summary>
    /// Loads client info.
    /// </summary>
    /// <param name="userDir"></param>
    /// <returns></returns>
    Task<string?> TryLoadAgentIdAsync(bool userDir);

    /// <summary>
    /// Stores client info.
    /// </summary>
    /// <param name="userDir"></param>
    /// <param name="agentId"></param>
    /// <returns></returns>
    Task TrySaveAgentIdAsync(bool userDir, string? agentId);
}
