using AndroidRecovery.Adb;
using AndroidRecovery.Models;
using Xunit;

namespace AndroidRecovery.UnitTests;

public sealed class AdbDeviceListParserTests
{
    [Fact]
    public void Parse_IdentifiesMultipleDevicesAndAuthorizationStates()
    {
        const string output = """
            List of devices attached
            serial-one device product:pixel model:Pixel_8
            serial-two unauthorized usb:1-2
            serial-three offline
            """;

        var result = AdbDeviceListParser.Parse(output);

        Assert.Equal(3, result.Devices.Count);
        Assert.Equal("serial-one", result.Devices[0].Id);
        Assert.Equal("Pixel 8", result.Devices[0].Model);
        Assert.Equal(AdbDeviceState.Device, result.Devices[0].State);
        Assert.Equal(AdbDeviceState.Unauthorized, result.Devices[1].State);
        Assert.Equal(AdbDeviceState.Offline, result.Devices[2].State);
        Assert.Equal(0, result.MalformedLineCount);
    }

    [Fact]
    public void Parse_ReportsMalformedLinesWithoutDiscardingValidDevices()
    {
        var result = AdbDeviceListParser.Parse("List of devices attached\ninvalid-line\nserial device\n");

        Assert.Single(result.Devices);
        Assert.Equal(1, result.MalformedLineCount);
    }
}