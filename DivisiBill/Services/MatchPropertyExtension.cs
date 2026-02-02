using Microsoft.Maui.Controls.Xaml;

namespace DivisiBill.Services
{
    /// <summary>
    /// Markup extension to propagate corresponding property values from one item to another.
    /// Typical XAML usage might be something like <code>FontSize="{services:MatchProperty entryScannedSubtotal}"</code>
    /// which creates a binding to the FontSize property of the referenced element.
    /// </summary>
    [ContentProperty(nameof(ReferenceName))]
    public class MatchPropertyExtension : IMarkupExtension<BindingBase>, IMarkupExtension
    {
        public string ReferenceName { get; set; }

        public MatchPropertyExtension()
        {
        }

        public MatchPropertyExtension(string referenceName)
        {
            ReferenceName = referenceName;
        }

        public BindingBase ProvideValue(IServiceProvider serviceProvider)
        {
            if (string.IsNullOrEmpty(ReferenceName))
                return null;

            // Use ReferenceExtension to resolve the named element
            var reference = new ReferenceExtension { Name = ReferenceName }.ProvideValue(serviceProvider);
            if (reference == null)
                return null;

            var target = (IProvideValueTarget?)serviceProvider.GetService(typeof(IProvideValueTarget));
            if (target is null)
                return null;

            string propertyName = target.TargetProperty switch
            {
                BindableProperty bp => bp.PropertyName,
                System.Reflection.PropertyInfo pi => pi.Name,
                System.Reflection.FieldInfo fi => fi.Name,
                string s => s,
                _ => null
            };

            return new Binding(propertyName) { Source = reference };
        }

        object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider) => ProvideValue(serviceProvider);
    }
}
