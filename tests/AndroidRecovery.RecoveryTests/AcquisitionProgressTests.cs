using AndroidRecovery.Recovery;
using Xunit;

namespace AndroidRecovery.RecoveryTests;

public sealed class AcquisitionProgressTests
{
    [Fact]
    public void PercentComplete_UsesBytesWhenTotalIsReliable()
    {
        var progress = new AcquisitionProgress
        {
            SessionId = "session",
            Status = AcquisitionStatus.Running,
            FilesDiscovered = 10,
            FilesAcquired = 1,
            BytesDiscovered = 200,
            BytesAcquired = 100
        };

        Assert.Equal(50, progress.PercentComplete);
    }

    [Fact]
    public void PercentComplete_FallsBackToFileCountWhenSomeSizesAreUnknown()
    {
        var progress = new AcquisitionProgress
        {
            SessionId = "session",
            Status = AcquisitionStatus.Running,
            FilesDiscovered = 4,
            FilesAcquired = 2,
            BytesDiscovered = 100,
            BytesAcquired = 100,
            IsByteTotalReliable = false
        };

        Assert.Equal(50, progress.PercentComplete);
    }

    [Fact]
    public void PercentComplete_HandlesEmptyAndCompletedSessions()
    {
        var empty = new AcquisitionProgress { SessionId = "session", Status = AcquisitionStatus.Running };
        var completed = empty with { Status = AcquisitionStatus.Completed };

        Assert.Equal(0, empty.PercentComplete);
        Assert.Equal(100, completed.PercentComplete);
    }

    [Fact]
    public void PercentComplete_ClampsUnexpectedCounts()
    {
        var progress = new AcquisitionProgress
        {
            SessionId = "session",
            Status = AcquisitionStatus.Running,
            FilesDiscovered = 1,
            FilesAcquired = 2,
            IsByteTotalReliable = false
        };

        Assert.Equal(100, progress.PercentComplete);
    }
}