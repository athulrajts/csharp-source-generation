using System.Windows;
using SourceGeneration.Mvvm;

namespace Sample
{
    [ViewModel]
    public partial class MainWindowViewModel
    {
        [GenerateProperty]
        string _firstName;

        [GenerateProperty]
        string _lastName;

        [DependsOn("FirstName", "LastName")]
        public string FullName => $"{_firstName} {_lastName}";

        [GenerateCommand(DependsOn = "FirstName,LastName")]
        void Submit() => MessageBox.Show($"First Name = {_firstName}\r\nLast Name ={_lastName}");
        
        bool CanSubmit() => string.IsNullOrEmpty(_firstName) == false && string.IsNullOrEmpty(_lastName) == false;
    }
}
