namespace Mediax.Core;

/// <summary>Defines the strategy to employ when publishing multiple events.</summary>
public enum EventStrategy
{
    /// <summary>Publish events one by one in sequence.</summary>
    Sequential = 0,

    /// <summary>Publish all events in parallel and wait for all to complete.</summary>
    ParallelWhenAll = 1,

    /// <summary>Publish all events in parallel but do not wait for completion (Fire and Forget).</summary>
    ParallelFireAndForget = 2
}
