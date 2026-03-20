using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Mediax.SourceGenerator
{
    [Generator]
    public sealed class HandlerGenerator : IIncrementalGenerator
    {
        private const string HandlerAttributeFqn        = "Mediax.Core.HandlerAttribute";
        private const string GlobalBehaviorAttributeFqn = "Mediax.Core.GlobalBehaviorAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var handlerClasses = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    HandlerAttributeFqn,
                    predicate: static (node, _) => node is ClassDeclarationSyntax,
                    transform: static (ctx, ct) => ExtractHandlerInfo(ctx, ct))
                .Where(static h => h is not null)
                .Collect();

            // Collect [assembly: GlobalBehavior(...)] declarations
            var globalBehaviors = context.CompilationProvider
                .Select(static (compilation, _) => ExtractGlobalBehaviors(compilation));

            var combined = handlerClasses.Combine(globalBehaviors);

            context.RegisterSourceOutput(combined, static (spc, pair) =>
                EmitAll(spc, pair.Left, pair.Right));
        }

        private static ImmutableArray<GlobalBehaviorInfo> ExtractGlobalBehaviors(Compilation compilation)
        {
            var result = new List<GlobalBehaviorInfo>();
            int declIdx = 0;
            foreach (AttributeData attr in compilation.Assembly.GetAttributes())
            {
                if (attr.AttributeClass?.ToDisplayString() != GlobalBehaviorAttributeFqn) continue;
                if (attr.ConstructorArguments.Length == 0) continue;
                if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol bSym) continue;

                INamedTypeSymbol origDef = bSym.IsUnboundGenericType ? bSym.OriginalDefinition
                                        : (bSym.IsGenericType ? bSym.OriginalDefinition : bSym);

                string baseName = origDef.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                int ltIdx = baseName.IndexOf('<');
                if (ltIdx >= 0) baseName = baseName.Substring(0, ltIdx);

                int order = 0;
                foreach (KeyValuePair<string, TypedConstant> kv in attr.NamedArguments)
                {
                    if (kv.Key == "Order" && kv.Value.Value is int o)
                        order = o;
                }

                result.Add(new GlobalBehaviorInfo(order, declIdx++, baseName));
            }

            // Sort by Order ascending, declaration index for ties
            result.Sort((a, b) =>
            {
                int cmp = a.Order.CompareTo(b.Order);
                return cmp != 0 ? cmp : a.DeclIndex.CompareTo(b.DeclIndex);
            });

            return result.ToImmutableArray();
        }

        private static void EmitAll(
            SourceProductionContext spc,
            ImmutableArray<HandlerInfo?> raw,
            ImmutableArray<GlobalBehaviorInfo> globalBehaviors)
        {
            var handlers = new List<HandlerInfo>();
            foreach (HandlerInfo? h in raw)
                if (h != null) handlers.Add(h);
            if (handlers.Count == 0) return;

            // Inject global behaviors (prepended, before per-handler behaviors) into each HandlerInfo
            if (globalBehaviors.Length > 0)
            {
                for (int i = 0; i < handlers.Count; i++)
                    handlers[i] = handlers[i].WithGlobalBehaviors(globalBehaviors);
            }

            EmitDispatchTable(spc, handlers);
            EmitDispatcher(spc, handlers);
            EmitStaticDispatch(spc, handlers);
        }

        // ── ExtractHandlerInfo ────────────────────────────────────────────────

        private static HandlerInfo? ExtractHandlerInfo(
            GeneratorAttributeSyntaxContext ctx, System.Threading.CancellationToken ct)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol handlerType) return null;

            // Read Lifetime (int value: 0=Singleton, 1=Scoped, 2=Transient)
            int lifetime = 0;
            foreach (var attr in ctx.Attributes)
            {
                if (attr.AttributeClass?.ToDisplayString() != HandlerAttributeFqn) continue;
                foreach (var kv in attr.NamedArguments)
                    if (kv.Key == "Lifetime" && kv.Value.Value is int lt)
                        lifetime = lt;
            }

        // Read [UseBehavior] open-generic base names using FullyQualifiedFormat
        // Tracks (order, declarationIndex, baseName) so behaviors can be sorted by Order
        // while preserving declaration order for ties.
        var behaviorEntries = new List<(int Order, int Index, string BaseName)>();
        int declIndex = 0;
        foreach (var attr in ctx.TargetSymbol.GetAttributes())
        {
            if (attr.AttributeClass?.MetadataName != "UseBehaviorAttribute") continue;
            if (attr.ConstructorArguments.Length == 0) continue;
                if (attr.ConstructorArguments[0].Value is not INamedTypeSymbol bSym) continue;

                var origDef = bSym.IsUnboundGenericType ? bSym.OriginalDefinition
                            : (bSym.IsGenericType ? bSym.OriginalDefinition : bSym);

                var baseName = origDef.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                // Strip trailing <...> if present (FullyQualifiedFormat includes type param names)
                var ltIdx = baseName.IndexOf('<');
                if (ltIdx >= 0) baseName = baseName.Substring(0, ltIdx);

                // Read the Order named argument (defaults to 0)
                int order = 0;
                foreach (KeyValuePair<string, TypedConstant> kv in attr.NamedArguments)
                {
                    if (kv.Key == "Order" && kv.Value.Value is int o)
                        order = o;
                }

                behaviorEntries.Add((order, declIndex++, baseName));
            }

        // Sort by Order ascending, then by declaration index for stable ties
        behaviorEntries.Sort((a, b) =>
        {
            int cmp = a.Order.CompareTo(b.Order);
            return cmp != 0 ? cmp : a.Index.CompareTo(b.Index);
        });

        var behaviorBaseNames = new List<string>(behaviorEntries.Count);
        foreach ((int _, int _, string name) in behaviorEntries)
            behaviorBaseNames.Add(name);

            // Find IHandler<TReq, TRes> or IEventHandler<TEvent>
            foreach (var iface in handlerType.AllInterfaces)
            {
                if (!iface.IsGenericType) continue;
                var def = iface.OriginalDefinition;
                var name = def.Name;
                var metadataName = def.MetadataName;
                var ns = def.ContainingNamespace.ToDisplayString();

                bool isHandler       = (name == "IHandler"       || metadataName.StartsWith("IHandler`"))       && ns.EndsWith("Mediax.Core");
                bool isStreamHandler = (name == "IStreamHandler"  || metadataName.StartsWith("IStreamHandler`")) && ns.EndsWith("Mediax.Core");
                bool isEventHandler  = (name == "IEventHandler"   || metadataName.StartsWith("IEventHandler`"))  && ns.EndsWith("Mediax.Core");

                if (!isHandler && !isStreamHandler && !isEventHandler) continue;

                string reqType, resType;
                if (isEventHandler)
                {
                    // IEventHandler<TEvent> has only one type arg; response is always Unit
                    reqType = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    resType = "global::Mediax.Core.Unit";
                }
                else
                {
                    reqType = iface.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    resType = iface.TypeArguments[1].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                }

                var handlerFqn = handlerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

                var isEvent = isEventHandler || iface.TypeArguments[0].AllInterfaces.Any(i =>
                    i.Name == "IEvent" || i.MetadataName == "IEvent");

                var isStream = isStreamHandler;

                // Auto-wire [Validate] → ValidationBehavior and [Cache] → CacheBehavior
                // by scanning attributes on the request type symbol
                var autoBehaviors = new List<string>();
                var reqSymbol = iface.TypeArguments[0];
                bool hasValidate = false;
                bool hasCache = false;
                foreach (var reqAttr in reqSymbol.GetAttributes())
                {
                    var attrName = reqAttr.AttributeClass?.MetadataName;
                    if (attrName == "ValidateAttribute") hasValidate = true;
                    if (attrName == "CacheAttribute")    hasCache    = true;
                }
                if (hasValidate)
                    autoBehaviors.Add("global::Mediax.Behaviors.ValidationBehavior");
                if (hasCache)
                    autoBehaviors.Add("global::Mediax.Behaviors.CacheBehavior");

                // Merge auto-wired behaviors (prepended) + explicit [UseBehavior] (appended)
                var closedBehaviors = new List<string>(autoBehaviors.Count + behaviorBaseNames.Count);
                foreach (var b in autoBehaviors)
                    closedBehaviors.Add(b + "<" + reqType + ", " + resType + ">");
                foreach (var b in behaviorBaseNames)
                    closedBehaviors.Add(b + "<" + reqType + ", " + resType + ">");

                return new HandlerInfo(
                    handlerFullName:    handlerFqn,
                    requestFullName:    reqType,
                    responseFullName:   resType,
                    lifetime:           lifetime,
                    closedBehaviorNames: closedBehaviors,
                    isEvent:            isEvent,
                    isStream:           isStream,
                    isEventHandler:     isEventHandler);
            }
            return null;
        }

        // ── EmitDispatchTable ─────────────────────────────────────────────────
        // NOTE: HandlerFullName, RequestFullName, ResponseFullName are already global::-prefixed

        private static void EmitDispatchTable(SourceProductionContext spc, List<HandlerInfo> handlers)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("// Mediax.SourceGenerator — do not edit");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections.Frozen;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();
            sb.AppendLine("internal static class DispatchTable");
            sb.AppendLine("{");
            sb.AppendLine("    internal static readonly FrozenDictionary<Type, Type> Handlers =");
            sb.AppendLine("        new Dictionary<Type, Type>");
            sb.AppendLine("        {");
            foreach (var h in handlers)
                if (!h.IsEvent)
                    sb.AppendLine("            [typeof(" + h.RequestFullName + ")] = typeof(" + h.HandlerFullName + "),");
            sb.AppendLine("        }.ToFrozenDictionary();");
            sb.AppendLine();
            sb.AppendLine("    internal static void RegisterAll(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)");
            sb.AppendLine("    {");
            foreach (var h in handlers)
            {
                var add = h.Lifetime == 1 ? "AddScoped" : h.Lifetime == 2 ? "AddTransient" : "AddSingleton";
                sb.AppendLine("        services." + add + "<" + h.HandlerFullName + ">();");
            }
            foreach (var h in handlers)
            {
                // Singleton handlers get Singleton behaviors so the pipeline can be
                // pre-built once in the constructor (zero per-call allocation).
                // Scoped/Transient handlers keep Scoped behaviors for correct per-request isolation.
                var behaviorAdd = h.Lifetime == 0 ? "AddSingleton" : "AddScoped";
                foreach (var b in h.ClosedBehaviorNames)
                    sb.AppendLine("        services." + behaviorAdd + "<" + b + ">();");
            }
            sb.AppendLine("        services.AddSingleton<global::Mediax.Core.IMediaxDispatcher, global::MediaxDispatcher>();");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            spc.AddSource("DispatchTable.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        // ── EmitDispatcher ────────────────────────────────────────────────────

        private static void EmitDispatcher(SourceProductionContext spc, List<HandlerInfo> handlers)
        {
            bool needsSp = false;
            foreach (var h in handlers)
                if (h.Lifetime != 0 || h.ClosedBehaviorNames.Count > 0) { needsSp = true; break; }

            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("// Mediax.SourceGenerator — do not edit");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using Mediax.Core;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();
            // No [CompilerGenerated] — keeps MediaxDispatcher visible in the debugger so
            // developers can set breakpoints in Dispatch_* methods and inspect behavior pipelines.
            sb.AppendLine("internal sealed class MediaxDispatcher : global::Mediax.Core.IMediaxDispatcher");
            sb.AppendLine("{");

            // Fields: Singleton handler instances + pre-built pipeline delegates (Singleton+Behavior)
            for (int i = 0; i < handlers.Count; i++)
            {
                var h = handlers[i];
                if (h.Lifetime != 0) continue;
                sb.AppendLine("    private readonly " + h.HandlerFullName + " _h" + i + ";");
                // Singleton behaviors are injected as fields so the pipeline is built once
                for (int b = 0; b < h.ClosedBehaviorNames.Count; b++)
                    sb.AppendLine("    private readonly " + h.ClosedBehaviorNames[b] + " _b" + i + "_" + b + ";");
                // Pre-built pipeline delegate: built once in ctor, zero allocation per dispatch
                if (!h.IsStream && h.ClosedBehaviorNames.Count > 0)
                    sb.AppendLine($"    private readonly global::Mediax.Core.HandlerDelegate<{h.RequestFullName}, {h.ResponseFullName}> _pipeline{i};");
                if (h.IsStream && h.ClosedBehaviorNames.Count > 0)
                    sb.AppendLine($"    private readonly global::Mediax.Core.StreamHandlerDelegate<{h.RequestFullName}, {h.ResponseFullName}> _pipeline{i};");
            }
            if (needsSp)
                sb.AppendLine("    private readonly global::System.IServiceProvider _sp;");

            // Constructor: inject Singleton handlers + their behaviors; build pipeline chains
            sb.AppendLine();
            var ctorParts = new List<string>();
            for (int i = 0; i < handlers.Count; i++)
            {
                var h = handlers[i];
                if (h.Lifetime != 0) continue;
                ctorParts.Add(h.HandlerFullName + " h" + i);
                for (int b = 0; b < h.ClosedBehaviorNames.Count; b++)
                    ctorParts.Add(h.ClosedBehaviorNames[b] + " b" + i + "_" + b);
            }
            if (needsSp) ctorParts.Add("global::System.IServiceProvider sp");
            sb.AppendLine("    public MediaxDispatcher(" + string.Join(", ", ctorParts) + ")");
            sb.AppendLine("    {");
            for (int i = 0; i < handlers.Count; i++)
            {
                var h = handlers[i];
                if (h.Lifetime != 0) continue;
                sb.AppendLine("        _h" + i + " = h" + i + ";");
                for (int b = 0; b < h.ClosedBehaviorNames.Count; b++)
                    sb.AppendLine($"        _b{i}_{b} = b{i}_{b};");
                // Build the pipeline inside-out ONCE here; stored as a field
                if (h.ClosedBehaviorNames.Count > 0 && !h.IsStream)
                {
                    var innerCall = h.IsEventHandler
                        ? $"(({h.HandlerFullName})_h{i}).Handle(req, c)"
                        : $"_h{i}.Handle(req, c)";
                    sb.AppendLine($"        {{");
                    sb.AppendLine($"            global::Mediax.Core.HandlerDelegate<{h.RequestFullName}, {h.ResponseFullName}> _p = (req, c) => {innerCall};");
                    for (int b = h.ClosedBehaviorNames.Count - 1; b >= 0; b--)
                    {
                        sb.AppendLine($"            var _prev{i}_{b} = _p;");
                        sb.AppendLine($"            _p = (req, c) => _b{i}_{b}.Handle(req, _prev{i}_{b}, c);");
                    }
                    sb.AppendLine($"            _pipeline{i} = _p;");
                    sb.AppendLine($"        }}");
                }
                if (h.ClosedBehaviorNames.Count > 0 && h.IsStream)
                {
                    sb.AppendLine($"        {{");
                    sb.AppendLine($"            global::Mediax.Core.StreamHandlerDelegate<{h.RequestFullName}, {h.ResponseFullName}> _p = (req, c) => _h{i}.Handle(req, c);");
                    for (int b = h.ClosedBehaviorNames.Count - 1; b >= 0; b--)
                    {
                        sb.AppendLine($"            var _prev{i}_{b} = _p;");
                        sb.AppendLine($"            _p = (req, c) => _b{i}_{b}.Handle(req, _prev{i}_{b}, c);");
                    }
                    sb.AppendLine($"            _pipeline{i} = _p;");
                    sb.AppendLine($"        }}");
                }
            }
            if (needsSp) sb.AppendLine("        _sp = sp;");
            sb.AppendLine("    }");

            // IMediaxDispatcher.Publish
            // Strategy: emit fully-specialized code per event type and subscriber count.
            // - Single subscriber  → direct await, no List, no .AsTask()
            // - N subscribers seq  → N inline awaits, no List, no Task allocation
            // - N subscribers para → fixed-size array (exact count known at codegen time), no List
            sb.AppendLine();
            sb.AppendLine("    [System.Diagnostics.DebuggerStepThrough]");
            sb.AppendLine("    public global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<global::Mediax.Core.Unit>> Publish(");
            sb.AppendLine("        global::Mediax.Core.IEvent @event, global::Mediax.Core.EventStrategy strategy, global::System.Threading.CancellationToken ct)");
            sb.AppendLine("    {");
            sb.AppendLine("        if (@event is null)");
            sb.AppendLine("            return global::System.Threading.Tasks.ValueTask.FromResult(global::Mediax.Core.Result<global::Mediax.Core.Unit>.Fail(global::Mediax.Core.Error.Internal(\"MX_NULL_EVENT\", \"Event cannot be null\")));");
            sb.AppendLine();
            var eventHandlers = handlers.Where(h => h.IsEvent).ToList();
            var groupedEventHandlers = eventHandlers.GroupBy(h => h.RequestFullName).ToList();
            if (groupedEventHandlers.Count > 0)
            {
                sb.AppendLine("        switch (@event)");
                sb.AppendLine("        {");
                foreach (IGrouping<string, HandlerInfo> group in groupedEventHandlers)
                {
                    var groupList = group.ToList();
                    int n = groupList.Count;
                    string _sfx = groupList[0].RequestFullName
                        .Replace("global::", "").Replace("::", "_")
                        .Replace(".", "_").Replace("<", "_").Replace(">", "_")
                        .Replace(",", "_").Replace(" ", "");
                    sb.AppendLine("            case " + group.Key + " _r:");
                    sb.AppendLine("                return PublishEvent_" + _sfx + "(_r, strategy, ct);");
                }
                sb.AppendLine("        }");
            }
            sb.AppendLine("        return global::System.Threading.Tasks.ValueTask.FromResult(global::Mediax.Core.Result<global::Mediax.Core.Unit>.Ok(global::Mediax.Core.Unit.Value));");
            sb.AppendLine("    }");
            sb.AppendLine();
            // Emit one specialized publish helper per event type
            foreach (var group in groupedEventHandlers)
            {
                var groupList = group.ToList();
                int n = groupList.Count;
                string reqType = group.Key;
                string methodSuffix = reqType
                    .Replace("global::", "")
                    .Replace("::", "_")
                    .Replace(".", "_")
                    .Replace("<", "_")
                    .Replace(">", "_")
                    .Replace(",", "_")
                    .Replace(" ", "");
                string unitResult = "global::Mediax.Core.Result<global::Mediax.Core.Unit>";
                string ok = "global::System.Threading.Tasks.ValueTask.FromResult(global::Mediax.Core.Result<global::Mediax.Core.Unit>.Ok(global::Mediax.Core.Unit.Value))";

                sb.AppendLine("    [System.Diagnostics.DebuggerStepThrough]");
                sb.AppendLine("    internal global::System.Threading.Tasks.ValueTask<" + unitResult + "> PublishEvent_" + methodSuffix + "(");
                sb.AppendLine("        " + reqType + " r, global::Mediax.Core.EventStrategy strategy, global::System.Threading.CancellationToken ct)");
                sb.AppendLine("    {");
                if (n == 1)
                {
                    // Single subscriber: direct dispatch.
                    // IEventHandler<T> already returns Result<Unit> — no AsUnit needed.
                    // IHandler<TEvent, Unit> still needs AsUnit to strip the generic parameter.
                    var h = groupList[0];
                    string dispatch = h.IsEventHandler
                        ? "Dispatch_" + h.HandlerSimpleName + "(r, ct)"
                        : "global::Mediax.Core.ResultExtensions.AsUnit(Dispatch_" + h.HandlerSimpleName + "(r, ct))";
                    sb.AppendLine("        // Single subscriber — direct dispatch, zero List/Task allocation");
                    sb.AppendLine("        return " + dispatch + ";");
                }
                else
                {
                    // Multiple subscribers: branch on strategy
                    sb.AppendLine("        if (strategy == global::Mediax.Core.EventStrategy.Sequential)");
                    sb.AppendLine("            return PublishEvent_" + methodSuffix + "_Sequential(r, ct);");
                    sb.AppendLine("        if (strategy == global::Mediax.Core.EventStrategy.ParallelFireAndForget)");
                    sb.AppendLine("        {");
                    for (int i = 0; i < n; i++)
                        sb.AppendLine("            _ = Dispatch_" + groupList[i].HandlerSimpleName + "(r, ct).AsTask();");
                    sb.AppendLine("            return " + ok + ";");
                    sb.AppendLine("        }");
                    sb.AppendLine("        // ParallelWhenAll — fixed-size array, exact count known at codegen");
                    sb.AppendLine("        return PublishEvent_" + methodSuffix + "_Parallel(r, ct);");
                }
                sb.AppendLine("    }");
                sb.AppendLine();

                if (n > 1)
                {
                    // Sequential helper: N inline awaits, no List<Task>, no .AsTask().
                    // IEventHandler<T> handlers are called directly (no AsUnit wrapper).
                    sb.AppendLine("    [System.Diagnostics.DebuggerStepThrough]");
                    sb.AppendLine("    private async global::System.Threading.Tasks.ValueTask<" + unitResult + "> PublishEvent_" + methodSuffix + "_Sequential(");
                    sb.AppendLine("        " + reqType + " r, global::System.Threading.CancellationToken ct)");
                    sb.AppendLine("    {");
                    for (int i = 0; i < n; i++)
                    {
                        string awaitExpr = groupList[i].IsEventHandler
                            ? "await Dispatch_" + groupList[i].HandlerSimpleName + "(r, ct).ConfigureAwait(false)"
                            : "await global::Mediax.Core.ResultExtensions.AsUnit(Dispatch_" + groupList[i].HandlerSimpleName + "(r, ct)).ConfigureAwait(false)";
                        sb.AppendLine("        var _r" + i + " = " + awaitExpr + ";");
                        sb.AppendLine("        if (_r" + i + ".IsFailure) return _r" + i + ";");
                    }
                    sb.AppendLine("        return global::Mediax.Core.Result<global::Mediax.Core.Unit>.Ok(global::Mediax.Core.Unit.Value);");
                    sb.AppendLine("    }");
                    sb.AppendLine();

                    // Parallel helper: starts all handlers first, then checks results.
                    // Fast-path: if all Singleton handlers complete synchronously, no Task
                    // allocation occurs at all — just N ValueTask reads on the stack.
                    // Slow-path: falls back to Task.WhenAll only when at least one handler
                    // is genuinely async (Scoped/Transient or I/O-bound).
                    sb.AppendLine("    [System.Diagnostics.DebuggerStepThrough]");
                    sb.AppendLine("    private global::System.Threading.Tasks.ValueTask<" + unitResult + "> PublishEvent_" + methodSuffix + "_Parallel(");
                    sb.AppendLine("        " + reqType + " r, global::System.Threading.CancellationToken ct)");
                    sb.AppendLine("    {");
                    // Kick off all handlers immediately (before any await).
                    // IEventHandler<T> called directly, IHandler<TEvent,Unit> via AsUnit.
                    for (int i = 0; i < n; i++)
                    {
                        string vtExpr = groupList[i].IsEventHandler
                            ? "Dispatch_" + groupList[i].HandlerSimpleName + "(r, ct)"
                            : "global::Mediax.Core.ResultExtensions.AsUnit(Dispatch_" + groupList[i].HandlerSimpleName + "(r, ct))";
                        sb.AppendLine("        global::System.Threading.Tasks.ValueTask<" + unitResult + "> _vt" + i + " = " + vtExpr + ";");
                    }
                    // Fast-path: all already completed synchronously
                    sb.Append("        if (");
                    for (int i = 0; i < n; i++)
                    {
                        if (i > 0) sb.Append(" && ");
                        sb.Append("_vt" + i + ".IsCompletedSuccessfully");
                    }
                    sb.AppendLine(")");
                    sb.AppendLine("        {");
                    for (int i = 0; i < n; i++)
                        sb.AppendLine("            { var _r = _vt" + i + ".GetAwaiter().GetResult(); if (_r.IsFailure) return global::System.Threading.Tasks.ValueTask.FromResult(_r); }");
                    sb.AppendLine("            return global::System.Threading.Tasks.ValueTask.FromResult(global::Mediax.Core.Result<global::Mediax.Core.Unit>.Ok(global::Mediax.Core.Unit.Value));");
                    sb.AppendLine("        }");
                    // Slow-path: at least one handler is async
                    sb.AppendLine("        return PublishEvent_" + methodSuffix + "_Parallel_Async(");
                    sb.Append("            ");
                    for (int i = 0; i < n; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append("_vt" + i);
                    }
                    sb.AppendLine(", ct);");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                    sb.AppendLine("    [System.Diagnostics.DebuggerStepThrough]");
                    sb.AppendLine("    private static async global::System.Threading.Tasks.ValueTask<" + unitResult + "> PublishEvent_" + methodSuffix + "_Parallel_Async(");
                    sb.Append("        ");
                    for (int i = 0; i < n; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append("global::System.Threading.Tasks.ValueTask<" + unitResult + "> _vt" + i);
                    }
                    sb.AppendLine(", global::System.Threading.CancellationToken ct)");
                    sb.AppendLine("    {");
                    sb.AppendLine("        var _tasks = new global::System.Threading.Tasks.Task<" + unitResult + ">[" + n + "];");
                    for (int i = 0; i < n; i++)
                        sb.AppendLine("        _tasks[" + i + "] = _vt" + i + ".AsTask();");
                    sb.AppendLine("        " + unitResult + "[] _results = await global::System.Threading.Tasks.Task.WhenAll(_tasks).ConfigureAwait(false);");
                    sb.AppendLine("        foreach (" + unitResult + " _res in _results) if (_res.IsFailure) return _res;");
                    sb.AppendLine("        return global::Mediax.Core.Result<global::Mediax.Core.Unit>.Ok(global::Mediax.Core.Unit.Value);");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                }
            }

            // IMediaxDispatcher.Dispatch<T>
            sb.AppendLine();
            sb.AppendLine("    [System.Diagnostics.DebuggerStepThrough]");
            sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveOptimization)]");
            sb.AppendLine("    public global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<T>> Dispatch<T>(");
            sb.AppendLine("        global::Mediax.Core.IRequest<T> request, global::System.Threading.CancellationToken ct)");
            sb.AppendLine("    {");
            sb.AppendLine("        switch (request)");
            sb.AppendLine("        {");
            for (int i = 0; i < handlers.Count; i++)
            {
                var h = handlers[i];
                if (!h.IsEvent && !h.IsStream)
                {
                    sb.AppendLine("            case " + h.RequestFullName + " r" + i + ":");
                    sb.AppendLine("            {");
                    bool simple = h.Lifetime == 0 && h.ClosedBehaviorNames.Count == 0;
                    if (simple)
                    {
                        sb.AppendLine("                var vt" + i + " = _h" + i + ".Handle(r" + i + ", ct);");
                        sb.AppendLine("                return global::System.Runtime.CompilerServices.Unsafe.As<");
                        sb.AppendLine("                    global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<" + h.ResponseFullName + ">>,");
                        sb.AppendLine("                    global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<T>>>(ref vt" + i + ");");
                    }
                    else
                    {
                        sb.AppendLine("                return Dispatch_" + h.HandlerSimpleName + "_Cast<T>(r" + i + ", ct);");
                    }
                    sb.AppendLine("            }");
                }
            }
            sb.AppendLine("            default:");
            sb.AppendLine("                return global::System.Threading.Tasks.ValueTask.FromResult(");
            sb.AppendLine("                    global::Mediax.Core.Result<T>.Fail(global::Mediax.Core.Error.Internal(");
            sb.AppendLine("                        \"MX_NO_HANDLER\", $\"No handler for '{request.GetType().Name}'\")));");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            // IMediaxDispatcher.Stream<T>
            sb.AppendLine();
            sb.AppendLine("    [System.Diagnostics.DebuggerStepThrough]");
            sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveOptimization)]");
            sb.AppendLine("    public global::System.Collections.Generic.IAsyncEnumerable<T> Stream<T>(");
            sb.AppendLine("        global::Mediax.Core.IStreamRequest<T> request, global::System.Threading.CancellationToken ct)");
            sb.AppendLine("    {");
            sb.AppendLine("        switch (request)");
            sb.AppendLine("        {");
            for (int i = 0; i < handlers.Count; i++)
            {
                var h = handlers[i];
                if (h.IsStream)
                {
                    sb.AppendLine("            case " + h.RequestFullName + " r" + i + ":");
                    sb.AppendLine("            {");
                    bool simple = h.Lifetime == 0 && h.ClosedBehaviorNames.Count == 0;
                    if (simple)
                    {
                        sb.AppendLine("                var vt" + i + " = _h" + i + ".Handle(r" + i + ", ct);");
                        sb.AppendLine("                return (global::System.Collections.Generic.IAsyncEnumerable<T>)vt" + i + ";");
                    }
                    else
                    {
                        sb.AppendLine("                return Dispatch_" + h.HandlerSimpleName + "_Stream_Cast<T>(r" + i + ", ct);");
                    }
                    sb.AppendLine("            }");
                }
            }
            sb.AppendLine("            default: return global::System.Linq.AsyncEnumerable.Empty<T>();");
            sb.AppendLine("        }");
            sb.AppendLine("    }");

            // Typed Dispatch_N methods + Cast wrappers
            for (int i = 0; i < handlers.Count; i++)
            {
                var h = handlers[i];
                sb.AppendLine();
                bool simple = h.Lifetime == 0 && h.ClosedBehaviorNames.Count == 0;

                var sn = h.HandlerSimpleName;

                if (h.IsStream)
                {
                    // Stream Handler
                    if (simple)
                    {
                        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                        sb.AppendLine("    internal global::System.Collections.Generic.IAsyncEnumerable<" + h.ResponseFullName + "> Dispatch_" + sn + "(");
                        sb.AppendLine("        " + h.RequestFullName + " request, global::System.Threading.CancellationToken ct)");
                        sb.AppendLine("        => _h" + i + ".Handle(request, ct);");
                    }
                    else if (h.Lifetime == 0)
                    {
                        // Singleton Stream + behaviors: pipeline pre-built in ctor, just call the field
                        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                        sb.AppendLine("    internal global::System.Collections.Generic.IAsyncEnumerable<" + h.ResponseFullName + "> Dispatch_" + sn + "(");
                        sb.AppendLine("        " + h.RequestFullName + " request, global::System.Threading.CancellationToken ct)");
                        sb.AppendLine($"        => _pipeline{i}(request, ct);");
                    }
                    else
                    {
                        // Scoped/Transient Stream: build pipeline per-call (scope needed for behaviors)
                        sb.AppendLine("    internal async global::System.Collections.Generic.IAsyncEnumerable<" + h.ResponseFullName + "> Dispatch_" + sn + "(");
                        sb.AppendLine("        " + h.RequestFullName + " request, [global::System.Runtime.CompilerServices.EnumeratorCancellation] global::System.Threading.CancellationToken ct)");
                        sb.AppendLine("    {");
                        sb.AppendLine("        using var scope = _sp.CreateScope();");
                        sb.AppendLine("        var handler = scope.ServiceProvider.GetRequiredService<" + h.HandlerFullName + ">();");
                        if (h.ClosedBehaviorNames.Count == 0)
                        {
                            sb.AppendLine("        await foreach (var item in handler.Handle(request, ct).WithCancellation(ct).ConfigureAwait(false))");
                            sb.AppendLine("        {");
                            sb.AppendLine("            yield return item;");
                            sb.AppendLine("        }");
                        }
                        else
                        {
                            for (int b = 0; b < h.ClosedBehaviorNames.Count; b++)
                                sb.AppendLine($"        var _b{i}_{b} = scope.ServiceProvider.GetRequiredService<{h.ClosedBehaviorNames[b]}>();");
                            sb.AppendLine($"        global::Mediax.Core.StreamHandlerDelegate<{h.RequestFullName}, {h.ResponseFullName}> _pipeline = (req, c) => handler.Handle(req, c);");
                            for (int b = h.ClosedBehaviorNames.Count - 1; b >= 0; b--)
                            {
                                sb.AppendLine($"        var _prev_{b} = _pipeline;");
                                sb.AppendLine($"        _pipeline = (req, c) => _b{i}_{b}.Handle(req, _prev_{b}, c);");
                            }
                            sb.AppendLine("        await foreach (var item in _pipeline(request, ct).WithCancellation(ct).ConfigureAwait(false))");
                            sb.AppendLine("        {");
                            sb.AppendLine("            yield return item;");
                            sb.AppendLine("        }");
                        }
                        sb.AppendLine("    }");
                    }

                    if (!simple)
                    {
                        sb.AppendLine();
                        sb.AppendLine("    [System.Diagnostics.DebuggerStepThrough]");
                        sb.AppendLine("    private global::System.Collections.Generic.IAsyncEnumerable<T> Dispatch_" + sn + "_Stream_Cast<T>(");
                        sb.AppendLine("        " + h.RequestFullName + " request, global::System.Threading.CancellationToken ct)");
                        sb.AppendLine("        => (global::System.Collections.Generic.IAsyncEnumerable<T>)Dispatch_" + sn + "(request, ct);");
                    }
                }
                else // Regular Handler
                {
                    // For IEventHandler<T>, the call is handler.Handle(@event, ct) — same signature,
                    // but the handler type doesn't implement IHandler<T,Unit>, so we cast it explicitly.
                    var handleCall = h.IsEventHandler
                        ? $"(({h.HandlerFullName})_h{i}).Handle(request, ct)"
                        : $"_h{i}.Handle(request, ct)";
                    var handleCallScoped = h.IsEventHandler
                        ? $"(({h.HandlerFullName})handler).Handle(request, ct)"
                        : "handler.Handle(request, ct)";

                    if (simple)
                    {
                        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                        sb.AppendLine("    internal global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<" + h.ResponseFullName + ">> Dispatch_" + sn + "(");
                        sb.AppendLine("        " + h.RequestFullName + " request, global::System.Threading.CancellationToken ct)");
                        sb.AppendLine("        => " + handleCall + ";");
                    }
                    else if (h.Lifetime == 0)
                    {
                        // Singleton + behaviors: pipeline pre-built in ctor, just call the field
                        sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                        sb.AppendLine("    internal global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<" + h.ResponseFullName + ">> Dispatch_" + sn + "(");
                        sb.AppendLine("        " + h.RequestFullName + " request, global::System.Threading.CancellationToken ct)");
                        sb.AppendLine($"        => _pipeline{i}(request, ct);");
                    }
                    else
                    {
                        // Scoped/Transient: build pipeline per-call (scope needed for behaviors).
                        // When there are NO behaviors we avoid async/await entirely — the C# compiler
                        // implicitly adds [DebuggerStepThrough] to every async state machine, which
                        // would prevent developers from setting breakpoints inside the method.
                        // Instead we chain scope disposal onto the ValueTask via a helper, keeping
                        // the method synchronous and debuggable.
                        if (h.ClosedBehaviorNames.Count == 0)
                        {
                            sb.AppendLine("    internal global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<" + h.ResponseFullName + ">> Dispatch_" + sn + "(");
                            sb.AppendLine("        " + h.RequestFullName + " request, global::System.Threading.CancellationToken ct)");
                            sb.AppendLine("    {");
                            sb.AppendLine("        var scope = _sp.CreateScope();");
                            sb.AppendLine("        var handler = scope.ServiceProvider.GetRequiredService<" + h.HandlerFullName + ">();");
                            sb.AppendLine("        var vt = " + handleCallScoped + ";");
                            sb.AppendLine("        if (vt.IsCompletedSuccessfully)");
                            sb.AppendLine("        {");
                            sb.AppendLine("            scope.Dispose();");
                            sb.AppendLine("            return vt;");
                            sb.AppendLine("        }");
                            sb.AppendLine("        return AwaitAndDispose(vt, scope);");
                            sb.AppendLine("        static async global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<" + h.ResponseFullName + ">> AwaitAndDispose(");
                            sb.AppendLine("            global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<" + h.ResponseFullName + ">> task,");
                            sb.AppendLine("            global::System.IDisposable s)");
                            sb.AppendLine("        {");
                            sb.AppendLine("            try { return await task.ConfigureAwait(false); }");
                            sb.AppendLine("            finally { s.Dispose(); }");
                            sb.AppendLine("        }");
                            sb.AppendLine("    }");
                        }
                        else
                        {
                            // With behaviors: build the pipeline synchronously, then hand the
                            // resulting ValueTask off to a local static async helper so that the
                            // Dispatch_X method itself is NOT async — avoiding the implicit
                            // [DebuggerStepThrough] the C# compiler attaches to all async state machines.
                            sb.AppendLine("    internal global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<" + h.ResponseFullName + ">> Dispatch_" + sn + "(");
                            sb.AppendLine("        " + h.RequestFullName + " request, global::System.Threading.CancellationToken ct)");
                            sb.AppendLine("    {");
                            sb.AppendLine("        var scope = _sp.CreateScope();");
                            sb.AppendLine("        var handler = scope.ServiceProvider.GetRequiredService<" + h.HandlerFullName + ">();");
                            for (int b = 0; b < h.ClosedBehaviorNames.Count; b++)
                                sb.AppendLine($"        var _b{i}_{b} = scope.ServiceProvider.GetRequiredService<{h.ClosedBehaviorNames[b]}>();");
                            sb.AppendLine($"        global::Mediax.Core.HandlerDelegate<{h.RequestFullName}, {h.ResponseFullName}> _pipeline = (req, c) => {handleCallScoped};");
                            for (int b = h.ClosedBehaviorNames.Count - 1; b >= 0; b--)
                            {
                                sb.AppendLine($"        var _prev_{b} = _pipeline;");
                                sb.AppendLine($"        _pipeline = (req, c) => _b{i}_{b}.Handle(req, _prev_{b}, c);");
                            }
                            sb.AppendLine("        var vt = _pipeline(request, ct);");
                            sb.AppendLine("        if (vt.IsCompletedSuccessfully)");
                            sb.AppendLine("        {");
                            sb.AppendLine("            scope.Dispose();");
                            sb.AppendLine("            return vt;");
                            sb.AppendLine("        }");
                            sb.AppendLine("        return AwaitAndDispose(vt, scope);");
                            sb.AppendLine("        static async global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<" + h.ResponseFullName + ">> AwaitAndDispose(");
                            sb.AppendLine("            global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<" + h.ResponseFullName + ">> task,");
                            sb.AppendLine("            global::System.IDisposable s)");
                            sb.AppendLine("        {");
                            sb.AppendLine("            try { return await task.ConfigureAwait(false); }");
                            sb.AppendLine("            finally { s.Dispose(); }");
                            sb.AppendLine("        }");
                            sb.AppendLine("    }");
                        }
                    }

                    // Cast wrapper for interface Dispatch<T> (only for non-simple handlers)
                    if (!simple)
                    {
                        sb.AppendLine();
                        sb.AppendLine("    [System.Diagnostics.DebuggerStepThrough]");
                        sb.AppendLine("    private global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<T>> Dispatch_" + sn + "_Cast<T>(");
                        sb.AppendLine("        " + h.RequestFullName + " request, global::System.Threading.CancellationToken ct)");
                        sb.AppendLine("        => global::Mediax.Core.ResultExtensions.As<T, " + h.ResponseFullName + ">(Dispatch_" + sn + "(request, ct));");
                    }
                }
            }

            sb.AppendLine("}");
            spc.AddSource("MediaxDispatcher.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }

        // ── EmitStaticDispatch ────────────────────────────────────────────────

        private static void EmitStaticDispatch(SourceProductionContext spc, List<HandlerInfo> handlers)
        {
            var sb = new StringBuilder();
            sb.AppendLine("// <auto-generated />");
            sb.AppendLine("// Mediax.SourceGenerator — do not edit");
            sb.AppendLine("#nullable enable");
            sb.AppendLine();
            sb.AppendLine("using System.Runtime.CompilerServices;");
            sb.AppendLine("using Microsoft.Extensions.DependencyInjection;");
            sb.AppendLine();
            sb.AppendLine("[CompilerGenerated]");
            sb.AppendLine("internal static class MediaxStaticHandlers");
            sb.AppendLine("{");
            sb.AppendLine("    internal static volatile global::MediaxDispatcher? _dispatcher;");
            sb.AppendLine("    internal static global::System.IServiceProvider? _sp;");
            sb.AppendLine();
            sb.AppendLine("    /// <summary>");
            sb.AppendLine("    /// Returns the test-double for the current async context (if any),");
            sb.AppendLine("    /// otherwise the production dispatcher. Branch prediction eliminates");
            sb.AppendLine("    /// this check in steady-state production (AsyncLocal.Value is null).");
            sb.AppendLine("    /// </summary>");
            sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("    internal static global::Mediax.Core.IMediaxDispatcher? GetDispatcher()");
            sb.AppendLine("        => global::Mediax.Core.MediaxRuntimeAccessor._testOverride.Value");
            sb.AppendLine("           ?? (global::Mediax.Core.IMediaxDispatcher?)_dispatcher;");
            sb.AppendLine();
            sb.AppendLine("    [ModuleInitializer]");
            sb.AppendLine("    internal static void RegisterHook()");
            sb.AppendLine("    {");
            sb.AppendLine("        global::Mediax.Runtime.MediaxStartupHooks.Register(");
            sb.AppendLine("            static sp =>");
            sb.AppendLine("            {");
            sb.AppendLine("                _sp = sp;");
            sb.AppendLine("                _dispatcher = sp.GetService<global::Mediax.Core.IMediaxDispatcher>() as global::MediaxDispatcher;");
            sb.AppendLine("            },");
            sb.AppendLine("            static () => { _dispatcher = null; _sp = null; });");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("[CompilerGenerated]");
            sb.AppendLine("internal static class MediaxGeneratedExtensions");
            sb.AppendLine("{");
            for (int i = 0; i < handlers.Count; i++)
            {
                var h = handlers[i];
                if (h.IsStream)
                {
                    sb.AppendLine("    extension(" + h.RequestFullName + " request)");
                    sb.AppendLine("    {");
                    sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    sb.AppendLine("        public global::System.Collections.Generic.IAsyncEnumerable<" + h.ResponseFullName + "> Stream(");
                    sb.AppendLine("            global::System.Threading.CancellationToken ct = default)");
                    sb.AppendLine("            => global::Mediax.Core.MediaxRuntimeAccessor.IsTestMode && global::Mediax.Core.MediaxRuntimeAccessor._testOverride.Value is { } td");
                    sb.AppendLine("                ? td.Stream(request, ct)");
                    sb.AppendLine("                : global::MediaxStaticHandlers._dispatcher is { } d");
                    sb.AppendLine("                    ? d.Dispatch_" + h.HandlerSimpleName + "(request, ct)");
                    sb.AppendLine("                    : global::Mediax.Core.MediaxRuntimeAccessor.Dispatcher.Stream(request, ct);");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                }
                else if (!h.IsEvent) // Regular handler (events use .Publish(), not .Send())
                {
                    sb.AppendLine("    extension(" + h.RequestFullName + " request)");
                    sb.AppendLine("    {");
                    sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                    sb.AppendLine("        public global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<" + h.ResponseFullName + ">> Send(");
                    sb.AppendLine("            global::System.Threading.CancellationToken ct = default)");
                    sb.AppendLine("            => global::Mediax.Core.MediaxRuntimeAccessor.IsTestMode && global::Mediax.Core.MediaxRuntimeAccessor._testOverride.Value is { } td");
                    sb.AppendLine("                ? td.Dispatch(request, ct)");
                    sb.AppendLine("                : global::MediaxStaticHandlers._dispatcher is { } d");
                    sb.AppendLine("                    ? d.Dispatch_" + h.HandlerSimpleName + "(request, ct)");
                    sb.AppendLine("                    : global::Mediax.Core.MediaxRuntimeAccessor.Dispatcher.Dispatch(request, ct);");
                    sb.AppendLine("    }");
                    sb.AppendLine();
                }
            }

            // Per-event-type typed Publish() extensions: call PublishEvent_X directly on the
            // concrete dispatcher, bypassing the switch(@event) in the interface Publish().
            // One extension per distinct event request type (grouped — multiple IEventHandlers
            // for the same type share one PublishEvent_X method).
            var emittedEventTypes = new HashSet<string>();
            foreach (HandlerInfo h in handlers)
            {
                if (!h.IsEvent) continue;
                if (!emittedEventTypes.Add(h.RequestFullName)) continue; // one per event type

                string methodSuffix = h.RequestFullName
                    .Replace("global::", "").Replace("::", "_")
                    .Replace(".", "_").Replace("<", "_").Replace(">", "_")
                    .Replace(",", "_").Replace(" ", "");

                sb.AppendLine("    extension(" + h.RequestFullName + " @event)");
                sb.AppendLine("    {");
                sb.AppendLine("        [MethodImpl(MethodImplOptions.AggressiveInlining)]");
                sb.AppendLine("        public global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<global::Mediax.Core.Unit>> Publish(");
                sb.AppendLine("            global::Mediax.Core.EventStrategy strategy = global::Mediax.Core.EventStrategy.Sequential,");
                sb.AppendLine("            global::System.Threading.CancellationToken ct = default)");
                sb.AppendLine("            => global::Mediax.Core.MediaxRuntimeAccessor.IsTestMode && global::Mediax.Core.MediaxRuntimeAccessor._testOverride.Value is { } td");
                sb.AppendLine("                ? td.Publish(@event, strategy, ct)");
                sb.AppendLine("                : global::MediaxStaticHandlers._dispatcher is { } d");
                // Direct call to PublishEvent_X — no switch, no boxing, no virtual dispatch
                sb.AppendLine("                    ? d.PublishEvent_" + methodSuffix + "(@event, strategy, ct)");
                sb.AppendLine("                    : global::Mediax.Core.MediaxRuntimeAccessor.Dispatcher.Publish(@event, strategy, ct);");
                sb.AppendLine("    }");
                sb.AppendLine();
            }

            // Fallback generic Publish for IEvent (used by test doubles and unknown types)
            sb.AppendLine("    [MethodImpl(MethodImplOptions.AggressiveInlining)]");
            sb.AppendLine("    public static global::System.Threading.Tasks.ValueTask<global::Mediax.Core.Result<global::Mediax.Core.Unit>> Publish(");
            sb.AppendLine("        this global::Mediax.Core.IEvent @event,");
            sb.AppendLine("        global::Mediax.Core.EventStrategy strategy = global::Mediax.Core.EventStrategy.Sequential,");
            sb.AppendLine("        global::System.Threading.CancellationToken ct = default)");
            sb.AppendLine("        => global::Mediax.Core.MediaxRuntimeAccessor.IsTestMode && global::Mediax.Core.MediaxRuntimeAccessor._testOverride.Value is { } td");
            sb.AppendLine("            ? td.Publish(@event, strategy, ct)");
            sb.AppendLine("            : global::MediaxStaticHandlers._dispatcher is { } d");
            sb.AppendLine("                ? d.Publish(@event, strategy, ct)");
            sb.AppendLine("                : global::Mediax.Core.MediaxRuntimeAccessor.Dispatcher.Publish(@event, strategy, ct);");

            sb.AppendLine("}");
            spc.AddSource("MediaxStaticDispatch.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
        }
    }

    internal sealed class GlobalBehaviorInfo
    {
        public int    Order     { get; }
        public int    DeclIndex { get; }
        public string BaseName  { get; }

        public GlobalBehaviorInfo(int order, int declIndex, string baseName)
        {
            Order     = order;
            DeclIndex = declIndex;
            BaseName  = baseName;
        }
    }

    internal sealed class HandlerInfo
    {
        public string HandlerFullName  { get; }
        /// <summary>Short class name used in generated method names (e.g. "EchoQueryHandler").</summary>
        public string HandlerSimpleName { get; }
        public string RequestFullName  { get; }
        public string ResponseFullName { get; }
        public int    Lifetime         { get; }
        public bool IsEvent { get; }
        public bool IsStream { get; }
        /// <summary>True when the handler implements IEventHandler&lt;TEvent&gt; (multi-subscriber pattern).</summary>
        public bool IsEventHandler { get; }
        public IReadOnlyList<string> ClosedBehaviorNames { get; }

        public HandlerInfo(string handlerFullName, string requestFullName, string responseFullName,
            int lifetime, List<string> closedBehaviorNames, bool isEvent, bool isStream, bool isEventHandler = false)
        {
            HandlerFullName   = handlerFullName;
            HandlerSimpleName = ExtractSimpleName(handlerFullName);
            RequestFullName   = requestFullName;
            ResponseFullName  = responseFullName;
            Lifetime          = lifetime;
            ClosedBehaviorNames = closedBehaviorNames;
            IsEvent           = isEvent;
            IsStream          = isStream;
            IsEventHandler    = isEventHandler;
        }

        /// <summary>
        /// Returns a new HandlerInfo with global behaviors prepended to the pipeline,
        /// each closed over the handler's TRequest/TResponse type arguments.
        /// </summary>
        public HandlerInfo WithGlobalBehaviors(ImmutableArray<GlobalBehaviorInfo> globals)
        {
            var merged = new List<string>(globals.Length + ClosedBehaviorNames.Count);
            foreach (GlobalBehaviorInfo g in globals)
                merged.Add(g.BaseName + "<" + RequestFullName + ", " + ResponseFullName + ">");
            foreach (string b in ClosedBehaviorNames)
                merged.Add(b);
            return new HandlerInfo(
                HandlerFullName, RequestFullName, ResponseFullName,
                Lifetime, merged, IsEvent, IsStream, IsEventHandler);
        }

        /// <summary>
        /// Extracts the unqualified class name from a fully-qualified name.
        /// "global::My.Namespace.EchoQueryHandler" → "EchoQueryHandler"
        /// </summary>
        private static string ExtractSimpleName(string fqn)
        {
            string s = fqn.StartsWith("global::") ? fqn.Substring("global::".Length) : fqn;
            int dot = s.LastIndexOf('.');
            return dot >= 0 ? s.Substring(dot + 1) : s;
        }
    }
}
