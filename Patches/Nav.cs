namespace ObeliskAccess.Patches;

/// <summary>
/// Shared list-navigation arithmetic for the screen managers (previously re-declared per file).
/// </summary>
internal static class Nav
{
    /// <summary>Wraps into [0, count); returns 0 for an empty list instead of dividing by zero.</summary>
    public static int Wrap(int v, int count)
    {
        if (count <= 0)
            return 0;
        return ((v % count) + count) % count;
    }

    public static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);
}
