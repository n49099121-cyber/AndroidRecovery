namespace AndroidRecovery.Adb;

public static class AndroidShellPath
{
    public static string Quote(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path.Contains('\0'))
        {
            throw new ArgumentException("Android paths cannot contain NUL.", nameof(path));
        }

        return "'" + path.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
    }
}