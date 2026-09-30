namespace AndroidRecovery.UI;

public sealed class WindowHandleReference
{
    public nint Handle { get; private set; }

    public void Set(nint handle) => Handle = handle;
}