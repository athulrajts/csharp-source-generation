using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGeneration.Mvvm.Helpers;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace SourceGeneration.Mvvm
{
    [Generator]
    public class PropertyFromAttributeGenerator : ISourceGenerator
    {
        private const string attributeText = @"
using System;
namespace SourceGeneration.Mvvm
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public class DefinePropertyAttribute : Attribute
    {
        public DefinePropertyAttribute()
        {
        }

        public string PropertyName { get; set; }
        public string PropertyType { get; set; }
    }
}
";

        public void Execute(GeneratorExecutionContext context)
        {
            // retrieve the populated receiver 
            if (!(context.SyntaxContextReceiver is SyntaxReceiver receiver))
            {
                return;
            }

            // get the added attribute, and INotifyPropertyChanged
            INamedTypeSymbol attributeSymbol = context.Compilation.GetTypeByMetadataName("SourceGeneration.Mvvm.DefinePropertyAttribute");
            INamedTypeSymbol notifySymbol = context.Compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");

            // group the fields by class, and generate the source
            foreach (var classSymbol in receiver.Types)
            {
                string classSource = GenerateClass(classSymbol, attributeSymbol, notifySymbol, context);
                context.AddSource($"{classSymbol.Name}.g.cs", SourceText.From(classSource, Encoding.UTF8));
            }
        }

        private string GenerateClass(ITypeSymbol classSymbol, INamedTypeSymbol attributeSymbol, INamedTypeSymbol notifySymbol, GeneratorExecutionContext context)
        {
            if (classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default) == false)
            {
                return null; //TODO: issue a diagnostic that it must be top level
            }

            StringWriter sw = new StringWriter();
            SourceWriter source = new SourceWriter(sw);

            string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            source.WriteLine("using System.Collections.Generic;");
            source.WriteLine("using System.ComponentModel;");
            source.WriteLine();

            using (source.StartBlock($"namespace {namespaceName}"))
            {
                using (source.StartBlock($"public partial class {classSymbol.Name} : INotifyPropertyChanged"))
                {
                    // if the class doesn't implement INotifyPropertyChanged already, add it
                    if (!classSymbol.Interfaces.Contains(notifySymbol))
                    {
                        source.WriteLine("public event PropertyChangedEventHandler PropertyChanged;");
                        source.WriteLine();
                    }

                    GenerateProperties(source, classSymbol, attributeSymbol);
                }
            }

            return sw.ToString();
        }

        private void GenerateProperties(SourceWriter source, ITypeSymbol type, INamedTypeSymbol attributeSymbol)
        {
            // get the AutoNotify attribute from the field, and any associated data
            var attributes = type.GetAttributes().Where(ad => ad.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default));

            foreach (var attributeData in attributes)
            {
                TypedConstant propertyNameAttributeValue = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "PropertyName").Value;
                TypedConstant propertyTypeAttributeValue = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "PropertyType").Value;

                if (propertyNameAttributeValue.IsNull || propertyTypeAttributeValue.IsNull)
                {
                    continue;
                }

                string propertyName = propertyNameAttributeValue.Value.ToString();
                string fieldName = $"_{propertyName.Substring(0, 1).ToLower()}{propertyName.Substring(1)}";
                string propertyType = propertyTypeAttributeValue.Value.ToString();

                using (source.StartProperty(propertyType, propertyName))
                {
                    source.WriteLine($"get {{ return {fieldName}; }}");
                    using (source.StartBlock("set"))
                    {
                        using (source.StartBlock($"if(EqualityComparer<{propertyType}>.Default.Equals(value) == false)"))
                        {
                            source.WriteLine($"{propertyType} tmp = {fieldName};");
                            source.WriteLine($"{fieldName} = value;");
                            source.WriteLine($"PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof({propertyName})));");
                            source.WriteLine($"On{propertyName}Changed(tmp, {fieldName});");
                        }
                    }
                }

                source.WriteLine($"partial void On{propertyName}Changed({propertyType} oldValue, {propertyType} newValue);");
                source.WriteLine();
            }


        }

        public void Initialize(GeneratorInitializationContext context)
        {
            // Register the attribute source
            context.RegisterForPostInitialization((i) => i.AddSource("DefinePropertyAttribute.g.cs", attributeText));

            // Register a syntax receiver that will be created for each generation pass
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<ITypeSymbol> Types { get; } = new List<ITypeSymbol>();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                // any type with at least one attribute is a candidate for property generation
                if (context.Node is ClassDeclarationSyntax classDeclarationSyntax
                    && classDeclarationSyntax.AttributeLists.Count > 0)
                {
                    ITypeSymbol typeSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax) as ITypeSymbol;

                    if(typeSymbol.GetAttributes().Any(x => x.AttributeClass.ToDisplayString() == "SourceGeneration.Mvvm.DefinePropertyAttribute"))
                    {
                        Types.Add(typeSymbol);
                    }

                }
            }
        }
    }
}
