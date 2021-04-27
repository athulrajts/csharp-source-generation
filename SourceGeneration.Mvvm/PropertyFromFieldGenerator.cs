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
    public class PropertyFromFieldGenerator : ISourceGenerator
    {

        private const string attributeText = @"
using System;
namespace SourceGeneration.Mvvm
{
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public class GeneratePropertyAttribute : Attribute
    {
        public GeneratePropertyAttribute()
        {
        }
        public string PropertyName { get; set; }
    }
}
";

        [System.Diagnostics.CodeAnalysis.SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1024:Compare symbols correctly", Justification = "MS Bug")]
        public void Execute(GeneratorExecutionContext context)
        {
            // retrieve the populated receiver 
            if (!(context.SyntaxContextReceiver is SyntaxReceiver receiver))
            {
                return;
            }

            // get the added attribute, and INotifyPropertyChanged
            INamedTypeSymbol attributeSymbol = context.Compilation.GetTypeByMetadataName("SourceGeneration.Mvvm.GeneratePropertyAttribute");
            INamedTypeSymbol notifySymbol = context.Compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");

            // group the fields by class, and generate the source
            foreach (IGrouping<INamedTypeSymbol, IFieldSymbol> group in receiver.Fields.GroupBy(f => f.ContainingType))
            {
                string classSource = GenerateClass(group.Key, group.ToList(), attributeSymbol, notifySymbol, context);
                context.AddSource($"{group.Key.Name}.g.cs", SourceText.From(classSource, Encoding.UTF8));
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            // Register the attribute source
            context.RegisterForPostInitialization((i) => i.AddSource("GeneratePropertyAttribute.g.cs", attributeText));

            // Register a syntax receiver that will be created for each generation pass
            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1024:Compare symbols correctly", Justification = "MS Bug")]
        private string GenerateClass(INamedTypeSymbol classSymbol, List<IFieldSymbol> fields, ISymbol attributeSymbol, ISymbol notifySymbol, GeneratorExecutionContext context)
        {
            if (classSymbol.ContainingSymbol.Equals(classSymbol.ContainingNamespace, SymbolEqualityComparer.Default) == false)
            {
                return null; //TODO: issue a diagnostic that it must be top level
            }

            string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            StringWriter sw = new StringWriter();
            SourceWriter source = new SourceWriter(sw);

            // begin building the generated source
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

                    // create properties for each field 
                    foreach (IFieldSymbol fieldSymbol in fields)
                    {
                        GenerateProperty(source, fieldSymbol, attributeSymbol);
                    }
                }
            }

            return sw.ToString();
        }

        private void GenerateProperty(SourceWriter source, IFieldSymbol fieldSymbol, ISymbol attributeSymbol)
        {
            // get the name and type of the field
            string fieldName = fieldSymbol.Name;
            ITypeSymbol fieldType = fieldSymbol.Type;

            // get the AutoNotify attribute from the field, and any associated data
            AttributeData attributeData = fieldSymbol.GetAttributes().Single(ad => ad.AttributeClass.Equals(attributeSymbol, SymbolEqualityComparer.Default));
            TypedConstant overridenNameOpt = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "PropertyName").Value;

            string propertyName = ChooseName(fieldName, overridenNameOpt);
            if (propertyName.Length == 0 || propertyName == fieldName)
            {
                //TODO: issue a diagnostic that we can't process this field
                return;
            }

            //  === Property Accessors === //

            using (source.StartProperty(fieldType.ToString(), propertyName))
            {
                source.WriteLine($"get {{ return {fieldName}; }}");
                using (source.StartBlock("set"))
                {
                    using (source.StartBlock($"if(EqualityComparer<{fieldType}>.Default.Equals(value) == false)"))
                    {
                        source.WriteLine($"{fieldType} tmp = {fieldName};");
                        source.WriteLine($"{fieldName} = value;");
                        source.WriteLine($"PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof({propertyName})));");
                        source.WriteLine($"On{propertyName}Changed(tmp, {fieldName});");
                    }
                }
            }

            source.WriteLine();

            // === Property Accessors === /

            source.WriteLine($"partial void On{propertyName}Changed({fieldType} oldValue, {fieldType} newValue);");
            source.WriteLine();

        }

        string ChooseName(string fieldName, TypedConstant overridenNameOpt)
        {
            if (!overridenNameOpt.IsNull)
            {
                return overridenNameOpt.Value.ToString();
            }

            fieldName = fieldName.TrimStart('_');
            if (fieldName.Length == 0)
            {
                return string.Empty;
            }

            if (fieldName.Length == 1)
            {
                return fieldName.ToUpper();
            }

            return fieldName.Substring(0, 1).ToUpper() + fieldName.Substring(1);
        }

        class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<IFieldSymbol> Fields { get; } = new List<IFieldSymbol>();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                // any field with at least one attribute is a candidate for property generation
                if (context.Node is FieldDeclarationSyntax fieldDeclarationSyntax
                    && fieldDeclarationSyntax.AttributeLists.Count > 0)
                {
                    foreach (VariableDeclaratorSyntax variable in fieldDeclarationSyntax.Declaration.Variables)
                    {
                        // Get the symbol being declared by the field, and keep it if its annotated
                        IFieldSymbol fieldSymbol = context.SemanticModel.GetDeclaredSymbol(variable) as IFieldSymbol;
                        if (fieldSymbol.GetAttributes().Any(ad => ad.AttributeClass.ToDisplayString() == "SourceGeneration.Mvvm.GeneratePropertyAttribute"))
                        {
                            Fields.Add(fieldSymbol);
                        }
                    }
                }
            }
        }
    }
}
