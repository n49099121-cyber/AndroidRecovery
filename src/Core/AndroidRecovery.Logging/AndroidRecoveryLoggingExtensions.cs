using Microsoft.Extensions.Hosting;
using Serilog;

namespace AndroidRecovery.Logging;

public static class AndroidRecoveryLoggingExtensions
{
    public static IHostBuilder UseAndroidRecoveryLogging(this IHostBuilder hostBuilder)
    {
        var logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AndroidRecovery",
            "logs");
        Directory.CreateDirectory(logDirectory);

        return hostBuilder.UseSerilog((_, configuration) => configuration
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(Path.Combine(logDirectory, "android-recovery-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true));
    }
}