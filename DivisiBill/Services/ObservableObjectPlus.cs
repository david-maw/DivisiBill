using CommunityToolkit.Mvvm.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using System.Xml.Serialization;

namespace DivisiBill.Services;

[DataContract]
[ObservableObject] // Needed because you can't add  [DataContract] to ObservableObject and DataContract Serializers are used in MealSummary and Person classes for now
#pragma warning disable MVVMTK0033 // Inherit from ObservableObject instead of using [ObservableObject]
public partial class ObservableObjectPlus
#pragma warning restore MVVMTK0033 // Inherit from ObservableObject instead of using [ObservableObject]
{
    [ObservableProperty]
    [XmlIgnore]
    [field: XmlIgnore] // Marks System.Xml.Serialization as used, does nothing at run time 
    public partial bool IsBusy { get; set; }

    #region Used to ease migration from earlier implementations that use Action parameter
    protected bool SetProperty<T>(ref T backingStore, T value, Action onChanged,
    [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(backingStore, value))
            return false;

        backingStore = value;
        onChanged?.Invoke();
        OnPropertyChanged(propertyName);
        return true;
    }
    #endregion
}