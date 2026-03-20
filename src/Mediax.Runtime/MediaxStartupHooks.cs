namespace Mediax.Runtime;

/// <summary>
/// Bridge between source-generated consumer code and MediaxRuntime.
/// Generated [ModuleInitializer] methods register hooks here; MediaxRuntime.Init runs them.
/// </summary>
public static class MediaxStartupHooks
{
    private static readonly List<(Action<IServiceProvider> Init, Action Clear)> _hooks = new();

    /// <summary>Called from [ModuleInitializer] in generated code to register init/clear callbacks.</summary>
    public static void Register(Action<IServiceProvider> init, Action clear)
        => _hooks.Add((init, clear));

    internal static void RunAll(IServiceProvider sp)
    {
        foreach (var (init, _) in _hooks) init(sp);
    }

    /// <summary>Nulls out all static handler fields. Called by MediaxRuntime.UseTestDouble.</summary>
    public static void ClearAll()
    {
        foreach (var (_, clear) in _hooks) clear();
    }
}
