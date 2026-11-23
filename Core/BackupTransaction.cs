namespace DareuLocal.Core;

public sealed record RestoreStep(string Name, Action Execute);

public sealed record BackupTransactionResult(
    bool Succeeded,
    string? FailedStep,
    Exception? Error,
    bool RolledBack,
    Exception? RollbackError);

/// <summary>
/// Executes multi-step device restore operations with progress reporting and automatic rollback on failure.
/// </summary>
public static class BackupTransaction
{
    public static BackupTransactionResult Execute(
        IReadOnlyList<RestoreStep> steps,
        Action rollback,
        Action<int, int, string>? onProgress = null)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(rollback);

        int total = steps.Count;
        for (int i = 0; i < total; i++)
        {
            var step = steps[i];
            onProgress?.Invoke(i + 1, total, step.Name);
            try
            {
                step.Execute();
            }
            catch (Exception ex)
            {
                try
                {
                    rollback();
                    return new BackupTransactionResult(false, step.Name, ex, true, null);
                }
                catch (Exception rollbackEx)
                {
                    return new BackupTransactionResult(false, step.Name, ex, false, rollbackEx);
                }
            }
        }

        return new BackupTransactionResult(true, null, null, false, null);
    }
}
