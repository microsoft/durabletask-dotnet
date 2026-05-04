// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.DurableTask.Analyzers.Orchestration;

/// <summary>
/// Analyzer that reports a warning when a non-contextual ILogger is used in an orchestration method.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LoggerOrchestrationAnalyzer : OrchestrationAnalyzer<LoggerOrchestrationAnalyzer.LoggerOrchestrationVisitor>
{
    /// <summary>
    /// Diagnostic ID supported for the analyzer.
    /// </summary>
    public const string DiagnosticId = "DURABLE0010";

    static readonly LocalizableString Title = new LocalizableResourceString(nameof(Resources.LoggerOrchestrationAnalyzerTitle), Resources.ResourceManager, typeof(Resources));
    static readonly LocalizableString MessageFormat = new LocalizableResourceString(nameof(Resources.LoggerOrchestrationAnalyzerMessageFormat), Resources.ResourceManager, typeof(Resources));

    static readonly DiagnosticDescriptor Rule = new(
        DiagnosticId,
        Title,
        MessageFormat,
        AnalyzersCategories.Orchestration,
        DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [Rule];

    /// <summary>
    /// Visitor that performs a per-orchestration data-flow analysis to identify ILogger usages
    /// that do not originate from <c>TaskOrchestrationContext.CreateReplaySafeLogger</c>.
    /// </summary>
    public sealed class LoggerOrchestrationVisitor : OrchestrationVisitor
    {
        INamedTypeSymbol? iLoggerSymbol;
        INamedTypeSymbol? taskOrchestrationContextSymbol;

        /// <inheritdoc/>
        public override bool Initialize()
        {
            this.iLoggerSymbol = this.KnownTypeSymbols.ILogger;
            this.taskOrchestrationContextSymbol = this.KnownTypeSymbols.TaskOrchestrationContext;
            return this.iLoggerSymbol != null;
        }

        /// <inheritdoc/>
        public override void VisitDurableFunction(SemanticModel semanticModel, MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            this.Analyze(semanticModel, methodSyntax, methodSymbol, orchestrationName, reportDiagnostic, reportEntryParamDecls: true);
        }

        /// <inheritdoc/>
        public override void VisitTaskOrchestrator(SemanticModel semanticModel, MethodDeclarationSyntax methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            this.Analyze(semanticModel, methodSyntax, methodSymbol, orchestrationName, reportDiagnostic, reportEntryParamDecls: true);
        }

        /// <inheritdoc/>
        public override void VisitFuncOrchestrator(SemanticModel semanticModel, SyntaxNode methodSyntax, IMethodSymbol methodSymbol, string orchestrationName, Action<Diagnostic> reportDiagnostic)
        {
            // For AddOrchestratorFunc with a method reference, methodSyntax is the referenced method's declaration.
            // For AddOrchestratorFunc with a lambda, methodSyntax is the lambda syntax and methodSymbol is the
            // containing method (e.g. Main). In the lambda case we do not want to report on the containing method's
            // ILogger parameter declarations because those parameters are not part of the orchestrator.
            bool reportEntryParamDecls = methodSyntax is MethodDeclarationSyntax;
            this.Analyze(semanticModel, methodSyntax, methodSymbol, orchestrationName, reportDiagnostic, reportEntryParamDecls);
        }

        void Analyze(
            SemanticModel semanticModel,
            SyntaxNode rootSyntax,
            IMethodSymbol rootSymbol,
            string orchestrationName,
            Action<Diagnostic> reportDiagnostic,
            bool reportEntryParamDecls)
        {
            ReplaySafeLoggerFlowAnalysis analysis = new(this.iLoggerSymbol!, this.taskOrchestrationContextSymbol, this.Compilation);
            analysis.Run(semanticModel, rootSyntax, rootSymbol);
            analysis.Report(Rule, orchestrationName, reportDiagnostic, reportEntryParamDecls);
        }
    }

    /// <summary>
    /// Performs a per-orchestration analysis to determine which ILogger references inside
    /// the orchestration's reachable code originate from <c>TaskOrchestrationContext.CreateReplaySafeLogger</c>.
    /// </summary>
    sealed class ReplaySafeLoggerFlowAnalysis
    {
        readonly INamedTypeSymbol iLoggerSymbol;
        readonly INamedTypeSymbol? taskOrchestrationContextSymbol;
        readonly Compilation compilation;

        readonly HashSet<IMethodSymbol> walked = new(SymbolEqualityComparer.Default);
        readonly Dictionary<IParameterSymbol, List<IOperation>> callSites = new(SymbolEqualityComparer.Default);
        readonly Dictionary<ISymbol, List<IOperation>> assignments = new(SymbolEqualityComparer.Default);
        readonly List<Candidate> candidates = new();

        IMethodSymbol? entryMethod;

        public ReplaySafeLoggerFlowAnalysis(
            INamedTypeSymbol iLoggerSymbol,
            INamedTypeSymbol? taskOrchestrationContextSymbol,
            Compilation compilation)
        {
            this.iLoggerSymbol = iLoggerSymbol;
            this.taskOrchestrationContextSymbol = taskOrchestrationContextSymbol;
            this.compilation = compilation;
        }

        public void Run(SemanticModel semanticModel, SyntaxNode rootSyntax, IMethodSymbol rootSymbol)
        {
            this.entryMethod = rootSymbol;
            this.Walk(semanticModel, rootSyntax, rootSymbol);
        }

        public void Report(DiagnosticDescriptor rule, string orchestrationName, Action<Diagnostic> report, bool reportEntryParamDecls)
        {
            HashSet<IParameterSymbol> reportedParamDecls = new(SymbolEqualityComparer.Default);

            if (reportEntryParamDecls && this.entryMethod != null)
            {
                foreach (IParameterSymbol p in this.entryMethod.Parameters)
                {
                    if (!this.IsILoggerType(p.Type) || p.DeclaringSyntaxReferences.Length == 0)
                    {
                        continue;
                    }

                    if (this.IsSymbolSafe(p, NewVisitingSet()))
                    {
                        continue;
                    }

                    SyntaxNode parameterSyntax = p.DeclaringSyntaxReferences[0].GetSyntax();
                    report(RoslynExtensions.BuildDiagnostic(rule, parameterSyntax, this.entryMethod.Name, orchestrationName));
                    reportedParamDecls.Add(p);
                }
            }

            foreach (Candidate c in this.candidates)
            {
                if (this.IsSymbolSafe(c.Symbol, NewVisitingSet()))
                {
                    continue;
                }

                // Avoid duplicate reporting for entry-method parameter references already reported at their declaration.
                if (c.Symbol is IParameterSymbol pr && reportedParamDecls.Contains(pr))
                {
                    continue;
                }

                report(RoslynExtensions.BuildDiagnostic(rule, c.Syntax, c.MethodName, orchestrationName));
            }
        }

        static HashSet<ISymbol> NewVisitingSet() => new(SymbolEqualityComparer.Default);

        // True iff the reference is the target of a simple assignment (e.g. LHS of `x = ...`).
        // Such references are write-only and shouldn't be analyzed as logger usages.
        // Compound assignments (`+=`), increments, ref/out passes, and reads are intentionally
        // not treated as write-only because they all observe the current value.
        static bool IsWriteOnlyTarget(IOperation reference)
        {
            return reference.Parent is ISimpleAssignmentOperation assignment
                && ReferenceEquals(assignment.Target, reference);
        }

        static IOperation Unwrap(IOperation operation)
        {
            IOperation current = operation;
            while (true)
            {
                switch (current)
                {
                    case IConversionOperation conv:
                        current = conv.Operand;
                        break;
                    case IParenthesizedOperation paren:
                        current = paren.Operand;
                        break;
                    default:
                        return current;
                }
            }
        }

        static IEnumerable<SyntaxNode> GetMethodBodySyntaxes(IMethodSymbol methodSymbol)
        {
            foreach (SyntaxReference reference in methodSymbol.DeclaringSyntaxReferences)
            {
                SyntaxNode syntax = reference.GetSyntax();
                if (syntax is MethodDeclarationSyntax or LocalFunctionStatementSyntax)
                {
                    yield return syntax;
                }
            }
        }

        // Phase 1: walk every reachable method, collecting ILogger candidates and call-site arguments.
        // We need this pass (not pure on-demand) to know all call sites of a given parameter, since a
        // helper's parameter is safe only if every call site within the orchestration passes a safe value.
        void Walk(SemanticModel semanticModel, SyntaxNode methodSyntax, IMethodSymbol methodSymbol)
        {
            if (!this.walked.Add(methodSymbol))
            {
                return;
            }

            IOperation? methodOperation = semanticModel.GetOperation(methodSyntax);
            if (methodOperation is null)
            {
                return;
            }

            string methodName = methodSymbol.Name;
            foreach (IOperation descendant in methodOperation.Descendants())
            {
                switch (descendant)
                {
                    case IFieldReferenceOperation fr when this.IsILoggerType(fr.Field.Type) && !IsWriteOnlyTarget(fr):
                        this.candidates.Add(new Candidate(methodName, fr.Syntax, fr.Field));
                        break;

                    case IPropertyReferenceOperation pr when this.IsILoggerType(pr.Property.Type) && !IsWriteOnlyTarget(pr):
                        this.candidates.Add(new Candidate(methodName, pr.Syntax, pr.Property));
                        break;

                    case IParameterReferenceOperation pr when this.IsILoggerType(pr.Parameter.Type) && !IsWriteOnlyTarget(pr):
                        this.candidates.Add(new Candidate(methodName, pr.Syntax, pr.Parameter));
                        break;

                    case ILocalReferenceOperation lr when this.IsILoggerType(lr.Local.Type) && !IsWriteOnlyTarget(lr):
                        this.candidates.Add(new Candidate(methodName, lr.Syntax, lr.Local));
                        break;

                    case IVariableDeclaratorOperation declarator
                        when this.IsILoggerType(declarator.Symbol.Type) && declarator.Initializer?.Value is IOperation initValue:
                        this.AddAssignment(declarator.Symbol, initValue);
                        break;

                    case ISimpleAssignmentOperation assignment:
                        this.RecordAssignmentToLocalOrParameter(assignment);
                        break;

                    case IInvocationOperation invocation:
                        this.RecordCallSiteArguments(invocation);
                        this.RecurseIntoCallee(semanticModel, invocation.TargetMethod);
                        break;
                }
            }
        }

        void RecordCallSiteArguments(IInvocationOperation invocation)
        {
            foreach (IArgumentOperation argument in invocation.Arguments)
            {
                if (argument.Parameter is IParameterSymbol calleeParam && this.IsILoggerType(calleeParam.Type))
                {
                    if (!this.callSites.TryGetValue(calleeParam, out List<IOperation>? args))
                    {
                        args = new List<IOperation>();
                        this.callSites[calleeParam] = args;
                    }

                    args.Add(argument.Value);
                }
            }
        }

        void RecurseIntoCallee(SemanticModel semanticModel, IMethodSymbol callee)
        {
            if (callee is null)
            {
                return;
            }

            foreach (SyntaxNode calleeSyntax in GetMethodBodySyntaxes(callee))
            {
                if (!this.compilation.ContainsSyntaxTree(calleeSyntax.SyntaxTree))
                {
                    continue;
                }

                SemanticModel calleeSemanticModel = semanticModel.SyntaxTree == calleeSyntax.SyntaxTree
                    ? semanticModel
                    : this.compilation.GetSemanticModel(calleeSyntax.SyntaxTree);

                this.Walk(calleeSemanticModel, calleeSyntax, callee);
            }
        }

        void RecordAssignmentToLocalOrParameter(ISimpleAssignmentOperation assignment)
        {
            // Track reassignments to locals and parameters of ILogger type. Field/property targets
            // are intentionally ignored — those are handled by the always-unsafe rule for fields/properties.
            switch (assignment.Target)
            {
                case ILocalReferenceOperation lr when this.IsILoggerType(lr.Local.Type):
                    this.AddAssignment(lr.Local, assignment.Value);
                    break;

                case IParameterReferenceOperation pr when this.IsILoggerType(pr.Parameter.Type):
                    this.AddAssignment(pr.Parameter, assignment.Value);
                    break;
            }
        }

        void AddAssignment(ISymbol symbol, IOperation value)
        {
            if (!this.assignments.TryGetValue(symbol, out List<IOperation>? values))
            {
                values = new List<IOperation>();
                this.assignments[symbol] = values;
            }

            values.Add(value);
        }

        // Phase 2: demand-driven safety resolution.
        // A symbol is "safe" iff its value is provably derived from CreateReplaySafeLogger.
        // The visiting set guards against cycles in the call graph; on revisit we return optimistic
        // (true). This matches the original fixed-point analysis: in a pure cycle, only an unsafe
        // call site outside the cycle can flip the result to unsafe.
        bool IsSymbolSafe(ISymbol symbol, HashSet<ISymbol> visiting)
        {
            if (!visiting.Add(symbol))
            {
                return true;
            }

            try
            {
                return symbol switch
                {
                    ILocalSymbol local => this.IsLocalSafe(local, visiting),
                    IParameterSymbol parameter => this.IsParameterSafe(parameter, visiting),
                    _ => false,
                };
            }
            finally
            {
                visiting.Remove(symbol);
            }
        }

        bool IsLocalSafe(ILocalSymbol local, HashSet<ISymbol> visiting)
        {
            // A local is safe iff every assignment to it (initializer + reassignments) has a safe value.
            // This is flow-insensitive: we don't reason about which assignment is "current" at any
            // particular use site. Using OR semantics here would be unsound — a single reassignment
            // to an unsafe value reaches every subsequent read.
            if (!this.assignments.TryGetValue(local, out List<IOperation>? values) || values.Count == 0)
            {
                return false;
            }

            foreach (IOperation value in values)
            {
                if (!this.IsExpressionSafe(value, visiting))
                {
                    return false;
                }
            }

            return true;
        }

        bool IsParameterSafe(IParameterSymbol parameter, HashSet<ISymbol> visiting)
        {
            // Externally-supplied parameters of the orchestrator entry point are never safe.
            if (SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, this.entryMethod))
            {
                return false;
            }

            // For helper parameters we require every observed call site AND every internal
            // reassignment to be safe. No observed sources at all means the helper isn't called
            // from this orchestration's reachable graph — be conservative and treat as unsafe.
            bool hasObservedSource = false;

            if (this.callSites.TryGetValue(parameter, out List<IOperation>? callArgs))
            {
                hasObservedSource = true;
                foreach (IOperation arg in callArgs)
                {
                    if (!this.IsExpressionSafe(arg, visiting))
                    {
                        return false;
                    }
                }
            }

            if (this.assignments.TryGetValue(parameter, out List<IOperation>? reassignments))
            {
                hasObservedSource = true;
                foreach (IOperation value in reassignments)
                {
                    if (!this.IsExpressionSafe(value, visiting))
                    {
                        return false;
                    }
                }
            }

            return hasObservedSource;
        }

        bool IsExpressionSafe(IOperation expression, HashSet<ISymbol> visiting)
        {
            IOperation expr = Unwrap(expression);
            return expr switch
            {
                IInvocationOperation invocation => this.IsCreateReplaySafeLoggerInvocation(invocation),
                ILocalReferenceOperation localRef => this.IsSymbolSafe(localRef.Local, visiting),
                IParameterReferenceOperation paramRef => this.IsSymbolSafe(paramRef.Parameter, visiting),
                IConditionalOperation cond => cond.WhenFalse != null
                    && this.IsExpressionSafe(cond.WhenTrue, visiting)
                    && this.IsExpressionSafe(cond.WhenFalse, visiting),
                ICoalesceOperation coalesce => this.IsExpressionSafe(coalesce.Value, visiting)
                    && this.IsExpressionSafe(coalesce.WhenNull, visiting),
                _ => false,
            };
        }

        bool IsCreateReplaySafeLoggerInvocation(IInvocationOperation invocation)
        {
            if (this.taskOrchestrationContextSymbol == null)
            {
                return false;
            }

            IMethodSymbol method = invocation.TargetMethod;
            if (method.Name != "CreateReplaySafeLogger")
            {
                return false;
            }

            IMethodSymbol original = method;
            while (original.OverriddenMethod != null)
            {
                original = original.OverriddenMethod;
            }

            INamedTypeSymbol? containing = original.ContainingType?.OriginalDefinition;
            return containing != null
                && SymbolEqualityComparer.Default.Equals(containing, this.taskOrchestrationContextSymbol);
        }

        bool IsILoggerType(ITypeSymbol type)
        {
            if (SymbolEqualityComparer.Default.Equals(type, this.iLoggerSymbol))
            {
                return true;
            }

            return type is INamedTypeSymbol named
                && named.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, this.iLoggerSymbol));
        }

        sealed class Candidate
        {
            public Candidate(string methodName, SyntaxNode syntax, ISymbol symbol)
            {
                this.MethodName = methodName;
                this.Syntax = syntax;
                this.Symbol = symbol;
            }

            public string MethodName { get; }

            public SyntaxNode Syntax { get; }

            public ISymbol Symbol { get; }
        }
    }
}
