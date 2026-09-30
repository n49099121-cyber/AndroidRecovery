using AndroidRecovery.Models;

namespace AndroidRecovery.Adb;

public sealed record AdbDeviceEntry(string Id, AdbDeviceState State, string Product, string Model);

public sealed record AdbDeviceListParseResult(IReadOnlyList<AdbDeviceEntry> Devices, int MalformedLineCount);

public static class AdbDeviceListParser
{
    public static AdbDeviceListParseResult Parse(string output)
    {
        var devices = new List<AdbDeviceEntry>();
        var malformedLines = 0;

        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.Equals("List of devices attached", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fields = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
            {
                malformedLines++;
                continue;
            }

            var stateIndex = 1;
            var stateText = fields[stateIndex];
            if (stateText == "no" && fields.Length > 2 && fields[2] == "permissions")
            {
                stateText = "no permissions";
                stateIndex++;
            }

            var state = stateText switch
            {
                "device" => AdbDeviceState.Device,
                "unauthorized" => AdbDeviceState.Unauthorized,
                "offline" => AdbDeviceState.Offline,
                "no permissions" => AdbDeviceState.NoPermissions,
                _ => AdbDeviceState.Unknown
            };

            var metadata = fields.Skip(stateIndex + 1);
            var product = ReadMetadata(metadata, "product");
            var model = ReadMetadata(metadata, "model").Replace('_', ' ');
            devices.Add(new(fields[0], state, product, model));
        }

        return new(devices, malformedLines);
    }

    private static string ReadMetadata(IEnumerable<string> fields, string key)
    {
        var prefix = key + ":";
        var value = fields.FirstOrDefault(field => field.StartsWith(prefix, StringComparison.Ordinal));
        return value is null ? "" : value[prefix.Length..];
    }
}