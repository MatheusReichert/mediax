using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Mediax.SourceGenerator
{
    /// <summary>
    /// Roslyn analyzer that emits Mediax diagnostics at compile time:
    /// <list type="bullet">
    ///   <item>MX0001 — No [Handler] registered for a request type that has .Send() called on it.</item>
    ///   <item>MX0002 — Multiple [Handler] classes registered for the same non-event request type.</item>
    ///   <item>MX0003 — A behavior type appears more than once in the [UseBehavior] chain (loop detected).</item>
    ///   <item>MX0004 — A Singleton handler depends on a Scoped or Transient behavior (lifetime mismatch).</item>
    /// </list>
    /// </summary>
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class MissingHandlerAnalyzer : DiagnosticAnalyzer
    {
        // ── Rule descriptors ──────────────────────────────────────────────────

        public static readonly DiagnosticDescriptor MissingHandlerRule = new(
            id: "MX0001",
            title: "Missing handler for request",
            messageFormat: "No [Handler] registered for request type '{0}'. Dispatch will fail at runtime.",
            category: "Mediax",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            helpLinkUri: "https://github.com/mediax/docs/MX0001",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        public static readonly DiagnosticDescriptor DuplicateHandlerRule = new(
            id: "MX0002",
            title: "Duplicate handler for request",
            messageFormat: "More than one [Handler] is registered for non-event request type '{0}'. Only one handler is allowed; use IEventHandler<T> for multi-subscriber events.",
            category: "Mediax",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            helpLinkUri: "https://github.com/mediax/docs/MX0002",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        public static readonly DiagnosticDescriptor BehaviorLoopRule = new(
            id: "MX0003",
            title: "Behavior loop detected",
            messageFormat: "Handler '{0}' declares behavior '{1}' more than once in its [UseBehavior] chain. This creates a redundant pipeline loop.",
            category: "Mediax",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            helpLinkUri: "https://github.com/mediax/docs/MX0003",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        public static readonly DiagnosticDescriptor LifetimeMismatchRule = new(
            id: "MX0004",
            title: "Behavior lifetime mismatch",
            messageFormat: "Handler '{0}' is Singleton but behavior '{1}' is registered as Scoped/Transient. This will cause a captive-dependency bug at runtime. Change the handler lifetime to Scoped, or the behavior to Singleton.",
            category: "Mediax",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            helpLinkUri: "https://github.com/mediax/docs/MX0004",
            customTags: WellKnownDiagnosticTags.CompilationEnd);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(MissingHandlerRule, DuplicateHandlerRule, BehaviorLoopRule, LifetimeMismatchRule);

        // ── Initialization ────────────────────────────────────────────────────

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            context.RegisterCompilationStartAction(compilationStart =>
            {
                var handlerAttrSymbol   = compilationStart.Compilation.GetTypeByMetadataName("Mediax.Core.HandlerAttribute");
                var useBehaviorAttr     = compilationStart.Compilation.GetTypeByMetadataName("Mediax.Core.UseBehaviorAttribute");
                var iHandlerSymbol      = compilationStart.Compilation.GetTypeByMetadataName("Mediax.Core.IHandler`2");
                var iRequestSymbol      = compilationStart.Compilation.GetTypeByMetadataName("Mediax.Core.IRequest`1");
                var iEventSymbol        = compilationStart.Compilation.GetTypeByMetadataName("Mediax.Core.IEvent");

                if (handlerAttrSymbol == null || iHandlerSymbol == null || iRequestSymbol == null)
                    return;

                // request type → list of (handlerClass, location, lifetime, behaviorTypes)
                var registeredHandlers = new ConcurrentDictionary<ITypeSymbol, System.Collections.Generic.List<HandlerRegistration>>(SymbolEqualityComparer.Default);
                var unhandledCallSites = new ConcurrentBag<(ITypeSymbol Type, Location Location)>();

                compilationStart.RegisterSemanticModelAction(semanticModel =>
                {
                    var root = semanticModel.SemanticModel.SyntaxTree.GetRoot();

                    // Collect [Handler] classes
                    foreach (var classDecl in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
                    {
                        if (semanticModel.SemanticModel.GetDeclaredSymbol(classDecl) is not INamedTypeSymbol namedType)
                            continue;

                        int? handlerLifetime = null;
                        foreach (var attr in namedType.GetAttributes())
                        {
                            if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, handlerAttrSymbol))
                                continue;
                            int lt = 0;
                            foreach (var kv in attr.NamedArguments)
                                if (kv.Key == "Lifetime" && kv.Value.Value is int v) lt = v;
                            handlerLifetime = lt;
                        }
                        if (handlerLifetime == null) continue;

                        // Collect [UseBehavior] types declared on the handler
                        var behaviorTypes = new System.Collections.Generic.List<INamedTypeSymbol>();
                        if (useBehaviorAttr != null)
                        {
                            foreach (var attr in namedType.GetAttributes())
                            {
                                if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, useBehaviorAttr)) continue;
                                if (attr.ConstructorArguments.Length == 0) continue;
                                if (attr.ConstructorArguments[0].Value is INamedTypeSymbol bType)
                                    behaviorTypes.Add(bType);
                            }
                        }

                        foreach (var iface in namedType.AllInterfaces)
                        {
                            if (!iface.IsGenericType) continue;
                            if (!SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, iHandlerSymbol)) continue;

                            var reqType = iface.TypeArguments[0];
                            var reg = new HandlerRegistration(namedType, classDecl.GetLocation(), handlerLifetime.Value, behaviorTypes);
                            registeredHandlers.AddOrUpdate(
                                reqType,
                                _ => new System.Collections.Generic.List<HandlerRegistration> { reg },
                                (_, list) => { lock (list) { list.Add(reg); } return list; });
                        }
                    }

                    // Collect .Send() call sites
                    foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
                    {
                        if (invocation.Expression is not MemberAccessExpressionSyntax member) continue;
                        if (member.Name.Identifier.Text != "Send") continue;

                        var receiverType = semanticModel.SemanticModel.GetTypeInfo(member.Expression).Type;
                        if (receiverType == null) continue;

                        if (ImplementsIRequest(receiverType, iRequestSymbol))
                            unhandledCallSites.Add((receiverType, invocation.GetLocation()));
                    }
                });

                compilationStart.RegisterCompilationEndAction(compilationEnd =>
                {
                    // MX0001 — missing handler
                    foreach (var (type, location) in unhandledCallSites)
                    {
                        if (!registeredHandlers.ContainsKey(type))
                        {
                            compilationEnd.ReportDiagnostic(Diagnostic.Create(
                                MissingHandlerRule, location, type.ToDisplayString()));
                        }
                    }

                    foreach (var kvp in registeredHandlers)
                    {
                        var reqType = kvp.Key;
                        var regs    = kvp.Value;

                        // MX0002 — duplicate non-event handler
                        bool isEvent = iEventSymbol != null && ImplementsInterface(reqType, iEventSymbol);
                        if (!isEvent && regs.Count > 1)
                        {
                            foreach (var reg in regs)
                                compilationEnd.ReportDiagnostic(Diagnostic.Create(
                                    DuplicateHandlerRule, reg.Location, reqType.ToDisplayString()));
                        }

                        foreach (var reg in regs)
                        {
                            // MX0003 — behavior loop (same type appears twice)
                            var seen = new System.Collections.Generic.HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
                            foreach (var bType in reg.BehaviorTypes)
                            {
                                if (!seen.Add(bType))
                                    compilationEnd.ReportDiagnostic(Diagnostic.Create(
                                        BehaviorLoopRule, reg.Location,
                                        reg.HandlerType.Name, bType.Name));
                            }

                            // MX0004 — captive dependency: Singleton handler + non-Singleton behavior
                            // We can only detect this when the behavior type itself has a known DI registration
                            // attribute. Here we use a heuristic: if the handler is Singleton (lifetime == 0)
                            // and any behavior is NOT sealed/static (i.e. looks Scoped), warn.
                            // Full detection would require scanning DI registrations which isn't feasible in an analyzer.
                            if (reg.Lifetime == 0 /* Singleton */ && reg.BehaviorTypes.Count > 0)
                            {
                                foreach (var bType in reg.BehaviorTypes)
                                {
                                    // Heuristic: behaviors that hold IServiceScope, IDbContext, or HttpClient
                                    // are almost certainly Scoped. Check constructor parameters.
                                    foreach (var ctor in bType.InstanceConstructors)
                                    {
                                        foreach (var param in ctor.Parameters)
                                        {
                                            var paramTypeName = param.Type.ToDisplayString();
                                            if (paramTypeName.Contains("DbContext") ||
                                                paramTypeName.Contains("IServiceScope") ||
                                                paramTypeName.Contains("HttpClient") ||
                                                paramTypeName.Contains("IHttpContextAccessor"))
                                            {
                                                compilationEnd.ReportDiagnostic(Diagnostic.Create(
                                                    LifetimeMismatchRule, reg.Location,
                                                    reg.HandlerType.Name, bType.Name));
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                });
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static bool ImplementsIRequest(ITypeSymbol type, INamedTypeSymbol iRequestSymbol)
        {
            foreach (var iface in type.AllInterfaces)
            {
                if (iface.IsGenericType &&
                    SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, iRequestSymbol))
                    return true;
            }
            return false;
        }

        private static bool ImplementsInterface(ITypeSymbol type, INamedTypeSymbol target)
        {
            foreach (var iface in type.AllInterfaces)
                if (SymbolEqualityComparer.Default.Equals(iface.OriginalDefinition, target) ||
                    SymbolEqualityComparer.Default.Equals(iface, target))
                    return true;
            return false;
        }

        private sealed class HandlerRegistration
        {
            public INamedTypeSymbol HandlerType  { get; }
            public Location         Location     { get; }
            public int              Lifetime     { get; }
            public System.Collections.Generic.List<INamedTypeSymbol> BehaviorTypes { get; }

            public HandlerRegistration(INamedTypeSymbol handlerType, Location location, int lifetime,
                System.Collections.Generic.List<INamedTypeSymbol> behaviorTypes)
            {
                HandlerType  = handlerType;
                Location     = location;
                Lifetime     = lifetime;
                BehaviorTypes = behaviorTypes;
            }
        }
    }
}
