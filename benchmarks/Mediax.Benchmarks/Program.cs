using BenchmarkDotNet.Running;
using Mediax.Benchmarks.Benchmarks;

// Run with:  dotnet run -c Release -- [filter]
//
// Examples:
//   dotnet run -c Release                          (all benchmarks)
//   dotnet run -c Release -- --filter *EchoQuery*  (only EchoQuery category)
//   dotnet run -c Release -- --filter *MediatR*    (only MediatR methods)
//   dotnet run -c Release -- --list flat           (list all benchmark names)
//
// NOTE: always run in Release configuration — Debug results are meaningless.

BenchmarkSwitcher
    .FromAssembly(typeof(SimpleDispatchBenchmarks).Assembly)
    .Run(args);
