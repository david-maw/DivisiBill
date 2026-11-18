using System.Windows.Input;

namespace DivisiBill.Controls;

public partial class SharerButton : ContentView
{
    public static readonly BindableProperty TextProperty = BindableProperty.Create(
        nameof(Text),
        typeof(string),
        typeof(SharerButton),
        string.Empty);

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        nameof(Command),
        typeof(ICommand),
        typeof(SharerButton));

    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(
        nameof(CommandParameter),
        typeof(object),
        typeof(SharerButton));

    public static readonly BindableProperty LongPressCommandProperty = BindableProperty.Create(
        nameof(LongPressCommand),
        typeof(ICommand),
        typeof(SharerButton));

    public static readonly BindableProperty LongPressCommandParameterProperty = BindableProperty.Create(
        nameof(LongPressCommandParameter),
        typeof(object),
        typeof(SharerButton));

    public static readonly BindableProperty BorderWidthProperty = BindableProperty.Create(
        nameof(BorderWidth),
        typeof(double),
        typeof(SharerButton),
        0d);

    public new static readonly BindableProperty BackgroundColorProperty = BindableProperty.Create(
        nameof(BackgroundColor),
        typeof(Color),
        typeof(SharerButton),
        Colors.Transparent);

    public static readonly BindableProperty TextColorProperty = BindableProperty.Create(
        nameof(TextColor),
        typeof(Color),
        typeof(SharerButton),
        Colors.Black);

    public static readonly BindableProperty BorderColorProperty = BindableProperty.Create(
        nameof(BorderColor),
        typeof(Color),
        typeof(SharerButton),
        Colors.Gray);

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius),
        typeof(double),
        typeof(SharerButton),
        10d,
        propertyChanged: OnCornerRadiusChanged);

    public SharerButton()
    {
        InitializeComponent();
        UpdateCornerRadius();
    }

    public double BorderWidth
    {
        get => (double)GetValue(BorderWidthProperty);
        set => SetValue(BorderWidthProperty, value);
    }

    public new Color BackgroundColor
    {
        get => (Color)GetValue(BackgroundColorProperty);
        set => SetValue(BackgroundColorProperty, value);
    }

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public Color BorderColor
    {
        get => (Color)GetValue(BorderColorProperty);
        set => SetValue(BorderColorProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    private static void OnCornerRadiusChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((SharerButton)bindable).UpdateCornerRadius();
    }

    private void UpdateCornerRadius()
    {
        if (ButtonBorder is not null)
        {
            ButtonBorder.StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
            {
                CornerRadius = CornerRadius
            };
        }
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public ICommand? LongPressCommand
    {
        get => (ICommand?)GetValue(LongPressCommandProperty);
        set => SetValue(LongPressCommandProperty, value);
    }

    public object? LongPressCommandParameter
    {
        get => GetValue(LongPressCommandParameterProperty);
        set => SetValue(LongPressCommandParameterProperty, value);
    }
}
