using AndroidRecovery.Recovery;
using Xunit;

namespace AndroidRecovery.RecoveryTests;

public sealed class EvidencePathUtilityTests
{
    [Theory]
    [InlineData("/sdcard/DCIM/Camera/photo.jpg", "DCIM\\Camera\\photo.jpg")]
    [InlineData("/sdcard/Pictures/My Holiday/photo one.jpg", "Pictures\\My Holiday\\photo one.jpg")]
    [InlineData("/sdcard/Documents/資料/record.txt", "Documents\\資料\\record.txt")]
    public void ToEvidenceRelativePath_PreservesSafeNestedAndUnicodeSegments(string androidPath, string expectedRelativePath)
    {
        var relativePath = EvidencePathUtility.ToEvidenceRelativePath(androidPath);

        Assert.Equal(expectedRelativePath, relativePath);
    }

    [Theory]
    [InlineData("/sdcard/../private/data.db")]
    [InlineData("/sdcard/DCIM/../../private/data.db")]
    [InlineData("/data/user/0/app/private.db")]
    public void ToEvidenceRelativePath_RejectsTraversalAndNonSharedStorage(string path)
    {
        Assert.Throws<ArgumentException>(() => EvidencePathUtility.ToEvidenceRelativePath(path));
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("..\\outside.txt")]
    [InlineData("C:\\outside.txt")]
    [InlineData("\\\\server\\share\\file.txt")]
    public void ResolveInsideRoot_RejectsRootedAndTraversingPaths(string relativePath)
    {
        Assert.Throws<ArgumentException>(() => EvidencePathUtility.ResolveInsideRoot(Path.GetTempPath(), relativePath));
    }

    [Fact]
    public void ResolveInsideRoot_KeepsDestinationWithinEvidenceDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

        var destination = EvidencePathUtility.ResolveInsideRoot(root, "DCIM/Camera/IMG_01.jpg");

        Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, destination, StringComparison.OrdinalIgnoreCase);
    }
}