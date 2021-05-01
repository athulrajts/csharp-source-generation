using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using SourceGeneration.Mvvm.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace SourceGeneration.Mvvm
{
    [Generator]
    public class ViewModelGenerator : ISourceGenerator
    {
        private GeneratorExecutionContext _context;
        private SourceWriter _source;

        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForPostInitialization(i =>
            {
                i.AddSource("Attibutes.g.cs", Generation.Attributes);
                i.AddSource("DelegateCommand.g.cs", Generation.DelegateCommand);
            });

            context.RegisterForSyntaxNotifications(() => new SyntaxReceiver());
        }

        public void Execute(GeneratorExecutionContext context)
        {
            // retrieve the populated receiver 
            if (!(context.SyntaxContextReceiver is SyntaxReceiver receiver))
            {
                return;
            }

            _context = context;

            foreach (ITypeSymbol classSymbol in receiver.Types)
            {
                StringWriter sw = new StringWriter();
                using (_source = new SourceWriter(sw))
                {
                    GenerateClass(classSymbol);
                    context.AddSource($"{classSymbol.Name}.g.cs", SourceText.From(sw.ToString(), Encoding.UTF8));
                }
            }
        }

        private void GenerateClass(ITypeSymbol classSymbol)
        {
            string namespaceName = classSymbol.ContainingNamespace.ToDisplayString();

            _source.WriteLine("using SourceGeneration.Mvvm;");
            _source.WriteLine("using System;");
            _source.WriteLine("using System.Collections.Generic;");
            _source.WriteLine("using System.ComponentModel;");
            _source.WriteLine("using System.Runtime.CompilerServices;");
            _source.WriteLine("using System.Windows.Input;");
            _source.WriteLine();

            using (_source.StartBlock($"namespace {namespaceName}"))
            {
                using (_source.StartBlock($"public partial class {classSymbol.Name} : INotifyPropertyChanged"))
                {
                    GenerateConstructors(classSymbol);

                    INamedTypeSymbol inpcSymbol = _context.GetInpcSymbol();

                    /// Implement <see cref="System.ComponentModel.INotifyPropertyChanged"/> if not already implemented
                    if (classSymbol.Interfaces.Contains(inpcSymbol) == false)
                    {
                        _source.WriteLine("public event PropertyChangedEventHandler PropertyChanged;");
                        _source.WriteLine("public void RaisePropertyChanged([CallerMemberName] string property = \"\") => PropertyChanged?.Invoke(this , new PropertyChangedEventArgs(property));");
                        _source.WriteLine();
                    }

                    // find all fields with GenerateProperty attribute
                    var fields = classSymbol.GetMembers()
                        .OfType<IFieldSymbol>()
                        .Where(f => f.GetAttributes().Any(x => x.AttributeClass.ToDisplayString() == "SourceGeneration.Mvvm.GeneratePropertyAttribute"));

                    // generate property for all fields with GeneratePropertyAttribute
                    foreach (IFieldSymbol field in fields)
                    {
                        GenerateProperty(field);
                    }

                    // find all methods with GeneratedCommand attribute
                    var methods = classSymbol.GetMembers()
                        .OfType<IMethodSymbol>()
                        .Where(m => m.GetAttributes().Any(x => x.AttributeClass.ToDisplayString() == "SourceGeneration.Mvvm.GenerateCommandAttribute"));

                    Dictionary<string, List<Tuple<bool, string>>> dependencies = new Dictionary<string, List<Tuple<bool, string>>>();

                    // generate commands for all methods with GenerateCommand attribute
                    foreach (IMethodSymbol method in methods)
                    {
                        // get the GenerateProperty attribute from the field, and any associated data
                        AttributeData attributeData = method.GetAttributes().Single(ad => ad.AttributeClass.ToDisplayString() == "SourceGeneration.Mvvm.GenerateCommandAttribute");
                        TypedConstant canExecuteDeps = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "DependsOn").Value;
                        TypedConstant optCommandName = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "CommandName").Value;
                        string commandName = optCommandName.IsNull ? $"{method.Name}Command" : optCommandName.Value.ToString();

                        if (canExecuteDeps.IsNull == false)
                        {
                            string[] deps = canExecuteDeps.Value.ToString().Split(new[] { ',', ';'});
                            foreach (var propName in deps)
                            {
                                if (dependencies.ContainsKey(propName) == false)
                                {
                                    dependencies.Add(propName, new List<Tuple<bool, string>>());
                                }

                                dependencies[propName].Add(new Tuple<bool, string>(true, commandName));
                            }

                        }

                        GenerateCommand(method, attributeData);
                    }

                    // find all properties with DependsOn attribute
                    var props = classSymbol.GetMembers()
                        .OfType<IPropertySymbol>()
                        .Where(p => p.GetAttributes().Any(x => x.AttributeClass.ToDisplayString() == "SourceGeneration.Mvvm.DependsOnAttribute"));

                    foreach (IPropertySymbol prop in props)
                    {
                        AttributeData attributeData = prop.GetAttributes().Single(ad => ad.AttributeClass.ToDisplayString() == "SourceGeneration.Mvvm.DependsOnAttribute");
                        var propNames = attributeData.ConstructorArguments[0];
                        foreach (var item in propNames.Values)
                        {
                            if (item.IsNull)
                            {
                                continue;
                            }

                            string key = item.Value.ToString();
                            
                            if (dependencies.ContainsKey(key) == false)
                            {
                                dependencies.Add(key, new List<Tuple<bool, string>>());
                            }

                            dependencies[key].Add(new Tuple<bool, string>(false, prop.Name));
                        }
                    }

                    GenerateDependencies(dependencies);
                }
            }
        }

        private void GenerateConstructors(ITypeSymbol classSymbol)
        {
            var ctors = classSymbol.GetMembers()
                .OfType<IMethodSymbol>().Where(x => x.Name == "Constructor")
                .ToList();

            if (ctors.Count == 0)
            {
                using (_source.StartBlock($"public {classSymbol.Name}()"))
                {
                    _source.WriteLine("PropertyChanged += OnPropertyChanged;");
                }

                _source.WriteLine();
            }
            else
            {
                foreach (var ctor in ctors)
                {
                    var @params = ctor.Parameters.ToList();

                    if (@params.Count == 0)
                    {
                        using (_source.StartBlock($"public {classSymbol.Name}()"))
                        {
                            _source.WriteLine($"Constructor();");
                            _source.WriteLine("PropertyChanged += OnPropertyChanged;");
                        }

                        _source.WriteLine();
                    }
                    else
                    {
                        string argsDef = string.Join(",", @params.Select(x => $"{x.Type.ToDisplayString()} {x.Name}"));
                        string argsParam = string.Join(",", @params.Select(x => x.Name));

                        using (_source.StartBlock($"public {classSymbol.Name}({argsDef})"))
                        {
                            _source.WriteLine($"Constructor({argsParam});");
                            _source.WriteLine("PropertyChanged += OnPropertyChanged;");
                        }

                        _source.WriteLine();
                    }
                }
            }
        }

        private void GenerateProperty(IFieldSymbol fieldSymbol)
        {
            string chooseName(string fn, TypedConstant optn)
            {
                if (!optn.IsNull)
                {
                    return optn.Value.ToString();
                }

                fn = fn.TrimStart('_');
                if (fn.Length == 0)
                {
                    return string.Empty;
                }

                if (fn.Length == 1)
                {
                    return fn.ToUpper();
                }

                return fn.Substring(0, 1).ToUpper() + fn.Substring(1);
            }

            // get the name and type of the field
            string fieldName = fieldSymbol.Name;
            ITypeSymbol fieldType = fieldSymbol.Type;

            // get the GenerateProperty attribute from the field, and any associated data
            AttributeData attributeData = fieldSymbol.GetAttributes().Single(ad => ad.AttributeClass.ToDisplayString() == "SourceGeneration.Mvvm.GeneratePropertyAttribute");
            TypedConstant optName = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "PropertyName").Value;

            string propertyName = chooseName(fieldName, optName);

            if (propertyName.Length == 0 || propertyName == fieldName)
            {
                //TODO: issue a diagnostic that we can't process this field
                return;
            }

            using (_source.StartProperty(fieldType.ToString(), propertyName))
            {
                // getter
                _source.WriteLine($"get {{ return {fieldName}; }}");

                // setter
                using (_source.StartBlock("set"))
                {
                    using (_source.StartBlock($"if(EqualityComparer<{fieldType}>.Default.Equals(value) == false)"))
                    {
                        _source.WriteLine($"{fieldType} tmp = {fieldName};");
                        _source.WriteLine($"{fieldName} = value;");
                        _source.WriteLine($"PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof({propertyName})));");
                        _source.WriteLine($"On{propertyName}Changed(tmp, {fieldName});");
                    }
                }
            }


            _source.WriteLine();

            // partial method which is called when property is changed, this is ignored by compiler if user doesn't
            // implement the method.
            _source.WriteLine($"partial void On{propertyName}Changed({fieldType} oldValue, {fieldType} newValue);");
            _source.WriteLine();

        }

        private void GenerateCommand(IMethodSymbol method, AttributeData attributeData)
        {
            string createCommand(string execute, string canExecute, bool needCanExeucte)
            {
                if (needCanExeucte)
                {
                    return $"DelegateCommand({execute}, {canExecute});";
                }
                else
                {
                    return $"DelegateCommand({execute});";
                }
            }
            string createGenericCommand(string execute, string canExecute, ITypeSymbol genericType, bool needCanExeucte)
            {
                if (needCanExeucte)
                {
                    return $"DelegateCommand<{genericType}>({execute}, {canExecute});";
                }
                else
                {
                    return $"DelegateCommand<{genericType}>({execute});";
                }
            }

            TypedConstant optCommandName = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "CommandName").Value;
            TypedConstant optCanExecuteName = attributeData.NamedArguments.SingleOrDefault(kvp => kvp.Key == "CanExecuteName").Value;
            string commandName = optCommandName.IsNull ? $"{method.Name}Command" : optCommandName.Value.ToString();
            string canExecuteName = optCanExecuteName.IsNull ? $"Can{method.Name}" : optCanExecuteName.Value.ToString();
            bool hasCanExecute = method.ContainingType.GetMembers($"{canExecuteName}").Length > 0;

            string backingField = $"_{commandName.Substring(0, 1).ToLower()}{commandName.Substring(1)}";
            bool isGeneric = method.Parameters.Count() == 1;
            string executeName = method.Name;


            string commandType = isGeneric
                ? $"DelegateCommand<{method.Parameters[0].Type}>"
                : "DelegateCommand";

            _source.Write($"private {commandType} {backingField};");
            _source.WriteLine();

            using (_source.StartProperty(commandType, commandName))
            {
                using (_source.StartBlock("get"))
                {
                    using (_source.StartBlock($"if({backingField} == null)"))
                    {

                        string command = isGeneric
                            ? createGenericCommand(executeName, canExecuteName, method.Parameters[0].Type, hasCanExecute)
                            : createCommand(executeName, canExecuteName, hasCanExecute);

                        _source.WriteLine($"{backingField} = new {command}");
                    }

                    _source.WriteLine($"return {backingField};");
                }
            }

            _source.WriteLine();
        }

        private void GenerateDependencies(Dictionary<string, List<Tuple<bool, string>>> dependencies)
        {
            using (_source.StartFunction($"void", "OnPropertyChanged", "object sender, PropertyChangedEventArgs e"))
            {
                using (_source.StartBlock($"switch(e.PropertyName)"))
                {
                    foreach (var kv in dependencies)
                    {
                        using (_source.StartBlock($"case \"{kv.Key}\":"))
                        {
                            foreach (var item in kv.Value)
                            {
                                bool isCommand = item.Item1;
                                string name = item.Item2;
                                
                                // if command Raise CanExecuteChanged
                                // else raise PropertyChanged
                                if (isCommand)
                                {
                                    _source.WriteLine($"{name}.RaiseCanExecuteChanged();");
                                }
                                else
                                {
                                    _source.WriteLine($"RaisePropertyChanged(nameof({name}));");
                                }
                            }
                            _source.WriteLine("break;");
                        }
                    }
                }
            }
        }

        class SyntaxReceiver : ISyntaxContextReceiver
        {
            public List<ITypeSymbol> Types { get; } = new List<ITypeSymbol>();

            public void OnVisitSyntaxNode(GeneratorSyntaxContext context)
            {
                if (context.Node is ClassDeclarationSyntax classDeclarationSyntax
                    && classDeclarationSyntax.AttributeLists.Count > 0)
                {
                    ITypeSymbol typeSymbol = context.SemanticModel.GetDeclaredSymbol(classDeclarationSyntax) as ITypeSymbol;

                    if (typeSymbol.GetAttributes().Any(x => x.AttributeClass.ToDisplayString() == "SourceGeneration.Mvvm.ViewModelAttribute"))
                    {
                        Types.Add(typeSymbol);
                    }

                }
            }
        }
    }
}
