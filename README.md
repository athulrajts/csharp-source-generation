# Samples for C# 9's Source Generation.

```
using SourceGeneration.Mvvm;

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
  void Move()
  { 
  }

  bool CanMove() => true;
  
}

```

The above code will generate the following class

```
using System.ComponentModel;
using System.Windows.Input;

public partial class Person : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    public string FirstName
    {
        get { return firstName; }
        set
        {
            if(EqualityComparer<string>.Default.Equals(value) == false)
            {
                string tmp = firstName;
                firstName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FirstName)));
                OnFirstNameChanged(tmp, firstName);
            }
        }
    }

    partial void OnFirstNameChanged(string oldValue, string newValue);

    public string LastName
    {
        get { return lastName; }
        set
        {
            if(EqualityComparer<string>.Default.Equals(value) == false)
            {
                string tmp = lastName;
                lastName = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastName)));
                OnLastNameChanged(tmp, lastName);
            }
        }
    }
  
    partial void OnLastNameChanged(string oldValue, string newValue);

    private ICommand _moveCommand;
    public ICommand MoveCommand
    {
      get
      {
          if(_moveCommand == null)
          {
              _moveCommand = new DelegateCommand(Move, CanMove);
          }
          return _moveCommand;
      }
    }
  
    void ListenForPropertyChange()
    {
        PropertyChanged += OnPropertyChanged;
    }

    void OnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        switch(e.PropertyName)
        {
            case "FirstName":
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullName)));
                break;
            }
            case "LastName":
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FullName)));
                break;
            }
        }
    }
}

```
