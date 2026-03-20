namespace Mediax.Core;

public enum HandlerLifetime { Singleton = 0, Scoped = 1, Transient = 2 }

/// <summary>Marks a class or method as a Mediax request handler, enabling source-generator discovery.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class HandlerAttribute : Attribute
{
    public HandlerLifetime Lifetime { get; set; } = HandlerLifetime.Singleton;
}

/// <summary>Signals that the ValidationBehavior should run for this request type.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ValidateAttribute : Attribute { }

/// <summary>Signals that the CacheBehavior should cache responses for this request type.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CacheAttribute : Attribute
{
    /// <summary>Time-to-live in seconds. Defaults to 60.</summary>
    public int Ttl { get; set; } = 60;
}

/// <summary>
/// Registers a behavior to run globally for every handler in the generated pipeline.
/// Apply at assembly level. Lower <see cref="Order"/> values run first (outermost wrap).
/// </summary>
/// <example>
/// [assembly: GlobalBehavior(typeof(LogBehavior&lt;,&gt;), Order = -100)]
/// [assembly: GlobalBehavior(typeof(ValidationBehavior&lt;,&gt;), Order = -50)]
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class GlobalBehaviorAttribute(Type behaviorType) : Attribute
{
    public Type BehaviorType { get; } = behaviorType;

    /// <summary>
    /// Execution order in the pipeline. Lower values run first (outermost wrap).
    /// Defaults to 0. Per-handler behaviors (via <see cref="UseBehaviorAttribute"/>)
    /// always run after all global behaviors regardless of order values.
    /// </summary>
    public int Order { get; set; } = 0;
}

/// <summary>Declares a behavior to run in the generated dispatch pipeline for this handler.</summary>
/// <remarks>
/// Use <see cref="Order"/> to control execution order when multiple behaviors are applied.
/// Lower values run first (outermost in the pipeline). Behaviors with the same order
/// value maintain declaration order. Defaults to 0.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class UseBehaviorAttribute : Attribute
{
    public Type BehaviorType { get; }

    /// <summary>
    /// Execution order in the pipeline. Lower values run first (outermost wrap).
    /// Defaults to 0. Behaviors with the same order maintain declaration order.
    /// </summary>
    public int Order { get; set; } = 0;

    public UseBehaviorAttribute(Type behaviorType) => BehaviorType = behaviorType;
}
