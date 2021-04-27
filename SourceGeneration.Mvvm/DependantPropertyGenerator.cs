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
    public class DependantPropertyGenerator : ISourceGenerator
    {
        private const string attributeText = @"
using System;

namespace SourceGeneration.Mvvm
{
    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public class DependsOnAttribute : Attribute
    {
        public DependsOnAttribute(params string[] properties)
        {
            Properties = properties;
        }

        public string[] Properties { get; }
    }
}
";

        [SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1024:Compare symbols correctly", Justification = "<Pending>")]
        public void Execute(GeneratorExecutionContext context)
        {
            // retrieve the populated receiver 
            if (!(context.SyntaxContextReceiver is SyntaxReceiver receiver))
            {
                return;
            }

            if (receiver.Properties.Count == 0)
            {
                return;
            }

            INamedTypeSymbol attributeSymbol = context.Compilation.GetTypeByMetadataName("SourceGeneration.Mvvm.DependsOnAttribute");
            INamedTypeSymbol notifyAttribute = context.Compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");

            // group the fields by class, and generate the source
            foreach (IGrouping<INamedTypeSymbol, IPropertySymbol> group in receiver.Properties.GroupBy(f => f.ContainingType))
            {
                string source = GenerateDependencies(group.Key, group.ToList(), attributeSymbol, context);
                context.AddSource($"{group.Key.Name}.g.cs", SourceText.From(source, Encoding.UTF8));
            }

        }

        private string GenerateDependencies(INamedTypeSymbol classSymbol, List<IPropertySymbol> propertySymbols, INamedTypeSymbol attributeSymbol, GeneratorExecutionContext context)
        {

            Dictionary<string, List<string>> props = new Dictionary<string, List<string>>();

            foreach (IPropertySymbol propertySymbol in propertySymbols)
            {
                AttributeData attributeData = propertySymbol.GetAttributes().Single(ad => ad.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default));
                var dependencies = attributeData.ConstructorArguments[0];

                foreach (var item in dependencies.Values)
                {
                    if (item.IsNull)
                    {
                        continue;
                    }

                    string key = item.Value.ToString();

                    if (props.ContainsKey(key) == false)
                    {
                        props.Add(key, new List<string>());
                    }

                    props[key].Add(propertySymbol.Name);
                }
            }


            string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            string onPropertyChanged = "OnPropertyChanged";

            StringWriter sw = new StringWriter();
            SourceWriter source = new SourceWriter(sw);

            source.WriteLine("using System.ComponentModel;");
            source.WriteLine();

            using (source.StartBlock($"namespace {namespaceName}"))
            {
                using (source.StartBlock($"public partial class {classSymbol.Name}"))
                {
                    using (source.StartFunction("void", "ListenForPropertyChange"))
                    {
                        source.WriteLine($"PropertyChanged += {onPropertyChanged};");
                    }

                    source.WriteLine();

                    using (source.StartFunction($"void", onPropertyChanged, "object sender, PropertyChangedEventArgs e"))
                    {
                        using (source.StartBlock($"switch(e.PropertyName)"))
                        {
                            foreach (var kv in props)
                            {
                                using (source.StartBlock($"case \"{kv.Key}\":"))
                                {
                                    foreach (var propName in kv.Value)
                                    {
                                        source.WriteLine($"PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof({propName})));");
                                    }
                                    source.WriteLine("break;");
                                }
                            }
                        }
                    }
                }
            }

            return sw.ToString();
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForPostInitialization(i => i.AddSource("DependsOnAttribute.g.cs", attributeText));

            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<IPropertySymbol> Properties { get; } = new List<IPropertySymbol>();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                if (context.Node is PropertyDeclarationSyntax propertyDeclarationSyntax
                    && propertyDeclarationSyntax.AttributeLists.Count > 0)
                {
                    IPropertySymbol propertySymbol = context.SemanticModel.GetDeclaredSymbol(propertyDeclarationSyntax) as IPropertySymbol;

                    if (propertySymbol.GetAttributes().Any(x => x.AttributeClass.ToDisplayString() == "SourceGeneration.Mvvm.DependsOnAttribute"))
                    {
                        Properties.Add(propertySymbol);
                    }

                }
            }
        }
    }
}
