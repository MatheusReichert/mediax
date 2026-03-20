using Xunit;

// Disable parallelism across ALL test classes because MediaxRuntime has static state.
// Tests that share the static runtime must run sequentially to avoid flakiness.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
