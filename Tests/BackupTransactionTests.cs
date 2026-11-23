using DareuLocal.Core;
using Xunit;

namespace DareuLocal.Tests;

public sealed class BackupTransactionTests
{
    [Fact]
    public void Execute_RunsAllStepsAndReportsProgress_WithoutRollback()
    {
        var executed = new List<string>();
        var progress = new List<(int Step, int Total, string Name)>();
        bool rolledBack = false;

        var result = BackupTransaction.Execute(
            new[]
            {
                new RestoreStep("DPI", () => executed.Add("DPI")),
                new RestoreStep("Keys", () => executed.Add("Keys")),
                new RestoreStep("Macros", () => executed.Add("Macros"))
            },
            () => rolledBack = true,
            (s, t, n) => progress.Add((s, t, n)));

        Assert.True(result.Succeeded);
        Assert.Null(result.FailedStep);
        Assert.False(result.RolledBack);
        Assert.False(rolledBack);
        Assert.Equal(new[] { "DPI", "Keys", "Macros" }, executed);
        Assert.Equal(new[] { (1, 3, "DPI"), (2, 3, "Keys"), (3, 3, "Macros") }, progress);
    }

    [Fact]
    public void Execute_TriggersRollbackOnMidStepFailure()
    {
        var executed = new List<string>();
        bool rolledBack = false;

        var result = BackupTransaction.Execute(
            new[]
            {
                new RestoreStep("DPI", () => executed.Add("DPI")),
                new RestoreStep("Keys", () => throw new IOException("write failed")),
                new RestoreStep("Macros", () => executed.Add("Macros"))
            },
            () => rolledBack = true);

        Assert.False(result.Succeeded);
        Assert.Equal("Keys", result.FailedStep);
        Assert.IsType<IOException>(result.Error);
        Assert.True(result.RolledBack);
        Assert.Null(result.RollbackError);
        Assert.True(rolledBack);
        Assert.Equal(new[] { "DPI" }, executed);
    }

    [Fact]
    public void Execute_ReportsDoubleFaultIfRollbackThrows()
    {
        var result = BackupTransaction.Execute(
            new[] { new RestoreStep("Lights", () => throw new IOException("step fail")) },
            () => throw new InvalidOperationException("rollback fail"));

        Assert.False(result.Succeeded);
        Assert.Equal("Lights", result.FailedStep);
        Assert.IsType<IOException>(result.Error);
        Assert.False(result.RolledBack);
        Assert.IsType<InvalidOperationException>(result.RollbackError);
    }
}
