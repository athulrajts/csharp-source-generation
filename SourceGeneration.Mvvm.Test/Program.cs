using System;
using SourceGeneration.Mvvm;
using System.Windows.Input;

namespace SourceGeneration.Mvvm.Test
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
            var p = new Person();
            p.PropertyChanged += (_, e) => Console.WriteLine(e.PropertyName);

            p.FirstName = "Athul";
            p.LastName = "Raj";
        }
    }
    
    public partial class Person
    {
        [GenerateProperty]
        string firstName;

        [GenerateProperty]
        string lastName;

        [DependsOn("FirstName", "LastName")]
        public string FullName => $"{firstName} {lastName}";

        public Person()
        {
            ListenForPropertyChange();
        }

        [GenerateCommand]
        void Run()
        {
            Console.WriteLine("Running");
        }

        bool CanRun()
        {
            return false;
        }

        [GenerateCommand(CommandName = "TheOpenCommand", CanExecuteName = "Exists")]
        void Open(int index)
        { 
        }
        bool Exists(int index)
        {
            return index % 2 == 0;
        }

    }
}
