using XmlnsPrefixAttribute = Microsoft.Maui.Controls.XmlnsPrefixAttribute;

// Because we control these files we can safely put them in the global namespace. This allows us to use the xmlns without
// having to specify the namespace in XAML files and still be free of name collisions because we can change anything problematic.
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "DivisiBill.ViewModels")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "DivisiBill")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "DivisiBill.Services")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "DivisiBill.Views")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "DivisiBill.Models")]
[assembly: XmlnsDefinition("http://schemas.microsoft.com/dotnet/maui/global", "DivisiBill.Controls")]

// leave this file in its own namespace so as to avoid any possibility of name collisions.
[assembly: XmlnsPrefix("http://schemas.microsoft.com/dotnet/2022/maui/toolkit", "toolkit")]
