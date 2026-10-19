using System.Diagnostics;
using NetHop.Core.Networking;
using NetHop.Core.Profiles;

namespace NetHop.Core.Planning;

/// <summary>Reads and changes the real network configuration. Implemented with CIM/Win32 in NetHop.Engine.</summary>
public interface INetworkBackend
{
    NetworkSnapshot GetSnapshot();

    /// <summary>Executes one step. Must be idempotent: adding an existing address or removing a missing one is not an error.</summary>
    void Execute(PlanStep step, CancellationToken cancellationToken);

    void RenameAdapter(Guid adapterId, string newName);
}

public sealed record ApplyOutcome(
    ApplyPlan Plan,
    IReadOnlyList<PlanStep> Executed,
    bool Success,
    string? Error,
    bool RolledBack,
    TimeSpan Duration);

/// <summary>Plans and executes a profile; if a step fails, restores the adapters it touched.</summary>
public sealed class ProfileApplier(INetworkBackend backend)
{
    public event Action<string>? Log;

    public ApplyOutcome Apply(Profile profile, bool dryRun, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var before = backend.GetSnapshot();
        var plan = ProfilePlanner.Plan(profile, before);

        if (dryRun || plan.HasErrors)
            return new(plan, [], !plan.HasErrors, plan.HasErrors ? Text.Get("Apply_ProfileHasErrors") : null, false, stopwatch.Elapsed);

        var executed = new List<PlanStep>();
        foreach (var step in plan.Steps)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Log?.Invoke(step.Describe());
                backend.Execute(step, cancellationToken);
                executed.Add(step);
            }
            catch (Exception ex)
            {
                Log?.Invoke($"Failed: {step.Describe()}: {ex.Message}");
                var rolledBack = TryRollback(before, plan);
                return new(plan, executed, false, $"{step.Describe()}: {ex.Message}", rolledBack, stopwatch.Elapsed);
            }
        }

        return new(plan, executed, true, null, false, stopwatch.Elapsed);
    }

    private bool TryRollback(NetworkSnapshot before, ApplyPlan plan)
    {
        try
        {
            var restore = SnapshotProfile.Create(before, plan.AffectedAdapters.Select(a => a.Id), "Rollback");
            var rollbackPlan = ProfilePlanner.Plan(restore, backend.GetSnapshot());
            var ok = true;
            foreach (var step in rollbackPlan.Steps)
            {
                try
                {
                    Log?.Invoke($"Rollback: {step.Describe()}");
                    backend.Execute(step, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    // Keep going: restoring most of the configuration beats stopping halfway.
                    Log?.Invoke($"Rollback step failed: {step.Describe()}: {ex.Message}");
                    ok = false;
                }
            }
            return ok;
        }
        catch (Exception ex)
        {
            Log?.Invoke($"Rollback failed: {ex.Message}");
            return false;
        }
    }
}
