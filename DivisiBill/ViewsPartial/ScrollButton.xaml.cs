namespace DivisiBill.ViewsPartial;

public partial class ScrollButton : ContentView
{
    public ScrollButton() => InitializeComponent();
    public bool Down
    {
        get => (bool)GetValue(DownProperty);
        set => SetValue(DownProperty, value);
    }

    public static readonly BindableProperty DownProperty =
        BindableProperty.Create(nameof(Down), typeof(bool), typeof(ScrollButton), false);
}