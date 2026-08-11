namespace Memento;

/// <summary>
/// Keeps the live registrations in step with the config. Rebinding is applied and
/// validated immediately — a binding that Windows refuses is rolled back on the spot.
/// </summary>
internal sealed class HotkeyManager
{
    private readonly HotkeyWindow window;
    private readonly AppConfig config;

    public HotkeyManager(HotkeyWindow window, AppConfig config)
    {
        this.window = window;
        this.config = config;
    }

    /// <summary>Registers both hotkeys, returning the ids Windows refused.</summary>
    public IReadOnlyList<HotkeyId> RegisterAll()
    {
        var failed = new List<HotkeyId>();
        foreach (var id in new[] { HotkeyId.Region, HotkeyId.FullDisplay })
        {
            if (!window.Register(id, config.Get(id)))
            {
                failed.Add(id);
            }
        }

        return failed;
    }

    /// <summary>
    /// Releases both registrations while a HotkeyBox is armed, so pressing a currently
    /// bound key records it instead of firing a capture over the Settings window.
    /// </summary>
    public void Suspend()
    {
        window.Unregister(HotkeyId.Region);
        window.Unregister(HotkeyId.FullDisplay);
    }

    public void Resume() => RegisterAll();

    /// <summary>
    /// Swaps a binding. On failure the previous binding is put back and re-registered,
    /// so the app is never left with no working hotkey after a rejected edit.
    /// </summary>
    public bool TryRebind(HotkeyId id, HotkeyBinding binding)
    {
        var previous = config.Get(id);
        if (previous == binding)
        {
            return true;
        }

        window.Unregister(id);

        if (window.Register(id, binding))
        {
            config.Set(id, binding);
            config.Save();
            return true;
        }

        window.Register(id, previous);
        return false;
    }
}
