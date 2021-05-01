using Microsoft.CodeAnalysis;

namespace SourceGeneration.Mvvm.Helpers
{
    internal static class Generation
    {
        public const string Attributes = @"
using System;

namespace SourceGeneration.Mvvm
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class ViewModelAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public class GenerateCommandAttribute : Attribute
    {
        public GenerateCommandAttribute()
        {
        }

        public string CommandName { get; set; }
        public string CanExecuteName { get; set; }
        public string DependsOn { get; set; }
    }

    [AttributeUsage(AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public class DependsOnAttribute : Attribute
    {
        public DependsOnAttribute(params string[] properties)
        {
            Properties = properties;
        }

        public string[] Properties { get; }
    }

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
        public const string DelegateCommand = @"
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

        public static INamedTypeSymbol GetInpcSymbol(this GeneratorExecutionContext context)
        {
            return context.Compilation.GetTypeByMetadataName("System.ComponentModel.INotifyPropertyChanged");
        }

        public static INamedTypeSymbol GetGeneratePropertySymbol(this GeneratorExecutionContext context)
        {
            return context.Compilation.GetTypeByMetadataName("SourceGeneration.Mvvm.GeneratePropertyAttribute");
        }

    }
}
