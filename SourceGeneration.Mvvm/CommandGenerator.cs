using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGeneration.Mvvm.Helpers;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

namespace SourceGeneration.Mvvm
{
    [Generator]
    public class CommandGenerator : ISourceGenerator
    {
        private const string attributeText = @"
using System;

namespace SourceGeneration.Mvvm
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class GenerateCommandAttribute : Attribute
    {
        public GenerateCommandAttribute()
        {
        }

        public string CommandName { get; set; }
        public string CanExecuteName { get; set; }
    }
}
";
        private const string commandText = @"
using System;
using System.Windows.Input;

namespace SourceGeneration.Mvvm
{
    public class DelegateCommand : ICommand
    {
        private readonly Func<bool> _canExecute;
        private readonly Action _execute;

        public DelegateCommand(Action execute, Func<bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public DelegateCommand(Action execute)
            : this(execute, null)
        {

        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            if (_canExecute == null)
            {
                return true;
            }

            return _canExecute();
        }

        public void Execute(object parameter)
        {
            _execute();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class DelegateCommand<T> : ICommand
    {
        private readonly Func<T, bool> _canExecute;
        private readonly Action<T> _execute;

        public DelegateCommand(Action<T> execute, Func<T,bool> canExecute)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public DelegateCommand(Action<T> execute)
            : this(execute, null)
        {

        }

        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            if (_canExecute == null)
            {
                return true;
            }

            return _canExecute((T)parameter);
        }

        public void Execute(object parameter)
        {
            _execute((T)parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
";

        [SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1024:Compare symbols correctly", Justification = "MS Bug")]
        public void Execute(GeneratorExecutionContext context)
        {
            // retrieve the populated receiver 
            if (!(context.SyntaxContextReceiver is SyntaxReceiver receiver))
            {
                return;
            }

            // group the fields by class, and generate the source
            foreach (IGrouping<INamedTypeSymbol, IMethodSymbol> group in receiver.Methods.GroupBy(f => f.ContainingType))
            {
                string classSource = GenerateCommands(group.Key, group.ToList(), context);
                context.AddSource($"{group.Key.Name}.g.cs", SourceText.From(classSource, Encoding.UTF8));
            }
        }

        private string GenerateCommands(INamedTypeSymbol classSymbol, List<IMethodSymbol> methodSymbols, GeneratorExecutionContext context)
        {
            string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            StringWriter sw = new StringWriter();
            SourceWriter source = new SourceWriter(sw);

            INamedTypeSymbol attributeSymbol = context.Compilation.GetTypeByMetadataName("SourceGeneration.Mvvm.GenerateCommandAttribute");

            source.WriteLine("using SourceGeneration.Mvvm;");
            source.WriteLine("using System.Windows.Input;");
            source.WriteLine();

            using (source.StartBlock($"namespace {namespaceName}"))
            {
                using (source.StartBlock($"public partial class {classSymbol.Name}"))
                {
                    foreach (IMethodSymbol methodSymbol in methodSymbols)
                    {
                        int paramCount = methodSymbol.Parameters.Count();
                        if (paramCount > 1)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(new DiagnosticDescriptor("SG001",
                                "Invalid method signature",
                                "Method cannot contain more than 1 arguments",
                                "Design",
                                DiagnosticSeverity.Error,
                                true),
                                methodSymbol.Locations.FirstOrDefault(), methodSymbol.Name, methodSymbol.ReturnType.Name));
                        }

                        bool isGeneric = paramCount == 1;

                        string executeName = methodSymbol.Name;
                        string commandName = GetCommandName(methodSymbol, attributeSymbol);
                        string canExecuteName = GetCanExecuteName(methodSymbol, attributeSymbol);
                        string backingField = $"_{commandName.Substring(0, 1).ToLower()}{commandName.Substring(1)}";
                        bool hasCanExecute = classSymbol.GetMembers($"{canExecuteName}").Length > 0;

                        source.Write($"private ICommand {backingField};");
                        source.WriteLine();

                        using (source.StartProperty($"ICommand", commandName))
                        {
                            using (source.StartBlock("get"))
                            {
                                using (source.StartBlock($"if({backingField} == null)"))
                                {

                                    string command = isGeneric
                                        ? CreateGenericCommand(executeName, canExecuteName, methodSymbol.Parameters[0].Type, hasCanExecute)
                                        : CreateCommand(executeName, canExecuteName, hasCanExecute);

                                    source.WriteLine($"{backingField} = new {command}");
                                }

                                source.WriteLine($"return {backingField};");
                            }
                        }

                        source.WriteLine();

                    }
                }
            }

            return sw.ToString();
        }

        private string CreateCommand(string execute, string canExecute, bool hasCanExecute)
        {
            if (hasCanExecute)
            {
                return $"DelegateCommand({execute}, {canExecute});";
            }
            else
            {
                return $"DelegateCommand({execute});";
            }
        }

        private string CreateGenericCommand(string execute, string canExecute, ITypeSymbol genericType, bool hasCanExecute)
        {
            if (hasCanExecute)
            {
                return $"DelegateCommand<{genericType}>({execute}, {canExecute});";
            }
            else
            {
                return $"DelegateCommand<{genericType}>({execute});";
            }
        }

        private string GetCommandName(IMethodSymbol methodSymbol, INamedTypeSymbol attributeSymbol)
        {
            AttributeData attributeData = methodSymbol.GetAttributes().Single(ad => ad.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default));

            TypedConstant commandName = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "CommandName").Value;

            return commandName.IsNull ? $"{methodSymbol.Name}Command" : commandName.Value.ToString();
        }

        private string GetCanExecuteName(IMethodSymbol methodSymbol, INamedTypeSymbol attributeSymbol)
        {
            AttributeData attributeData = methodSymbol.GetAttributes().Single(ad => ad.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default));

            TypedConstant canExecuteName = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "CanExecuteName").Value;

            return canExecuteName.IsNull ? $"Can{methodSymbol.Name}" : canExecuteName.Value.ToString();
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForPostInitialization((i) => 
            {
                i.AddSource("GenerateCommandAttribute.g.cs", attributeText);
                i.AddSource("DelegateCommand.g.cs", commandText);
            });

            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<IMethodSymbol> Methods { get; } = new List<IMethodSymbol>();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                if (context.Node is MethodDeclarationSyntax methodDeclarationSyntax
                    && methodDeclarationSyntax.AttributeLists.Count > 0)
                {
                    IMethodSymbol methodSymbol = context.SemanticModel.GetDeclaredSymbol(methodDeclarationSyntax) as IMethodSymbol;

                    if (methodSymbol.GetAttributes().Any(x => x.AttributeClass.ToDisplayString() == "SourceGeneration.Mvvm.GenerateCommandAttribute"))
                    {
                        Methods.Add(methodSymbol);
                    }

                }
            }
        }
    }

}
