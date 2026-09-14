namespace Murmur.Abstractions;

/// <summary>Opens the operating system's keyboard window menu.</summary>
public interface IWindowMenu
{
    /// <summary>Shows the system menu for the supplied native window.</summary>
    /// <returns>The system command the user chose (for example <c>0xF020</c>, minimise), or 0 if none.</returns>
    int Show(nint handle);

    /// <summary>Why the last menu did not show, for the log. Null when it showed or was simply dismissed.</summary>
    string? LastError => null;
}
