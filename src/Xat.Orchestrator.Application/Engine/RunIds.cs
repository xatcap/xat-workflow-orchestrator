namespace Xat.Orchestrator.Application.Engine;

/// <summary>Makes the IDs of runs.</summary>
public static class RunIds
{
    /// <summary>Makes a new run ID, for example <c>run_3f9a...</c>.</summary>
    /// <returns>The ID.</returns>
    public static string New() => "run_" + Guid.NewGuid().ToString("N")[..20];
}
