namespace Mediax.Core;

public interface IHandlerRegistry
{
    Type GetHandlerType(Type requestType);
    IReadOnlyDictionary<Type, Type> All { get; }

    /// Returns true only for Scoped handlers that require a per-dispatch IServiceScope.
    /// Singleton and Transient handlers return false (resolved directly from root provider).
    bool NeedsScope(Type handlerType);
}
