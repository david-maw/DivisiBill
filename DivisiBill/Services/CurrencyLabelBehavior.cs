#nullable enable

using System.Globalization;

namespace DivisiBill.Services;

internal class CurrencyLabelBehavior : Behavior<Label>
{
    public static readonly BindableProperty ValidStyleProperty =
        BindableProperty.Create(nameof(ValidStyle), typeof(Style), typeof(CurrencyLabelBehavior));

    public static readonly BindableProperty UnequalStyleProperty =
        BindableProperty.Create(nameof(UnequalStyle), typeof(Style), typeof(CurrencyLabelBehavior));

    public static readonly BindableProperty IsEqualProperty =
        BindableProperty.Create(nameof(IsEqual), typeof(bool), typeof(CurrencyLabelBehavior), true, BindingMode.OneWayToSource);

    public static readonly BindableProperty TargetValueProperty =
        BindableProperty.Create(nameof(TargetValue), typeof(decimal), typeof(CurrencyLabelBehavior), propertyChanged: OnSomePropertyChanged);

    public static readonly BindableProperty EqualValueProperty =
        BindableProperty.Create(nameof(EqualValue), typeof(decimal), typeof(CurrencyLabelBehavior), propertyChanged: OnSomePropertyChanged);

    public static readonly BindableProperty TestEqualityProperty =
        BindableProperty.Create(nameof(TestEquality), typeof(bool), typeof(CurrencyLabelBehavior), true, propertyChanged: OnSomePropertyChanged);
    private bool bindingWasSet = false;
    private Label? savedLabel;
    protected override void OnAttachedTo(Label label)
    {
        savedLabel = label;
        if (bindingWasSet = (BindingContext is null))
            SetBinding(BindingContextProperty,
            new Binding
            {
                Path = BindingContextProperty.PropertyName,
                Source = label,
            });
        label.PropertyChanged += Label_PropertyChanged;
        base.OnAttachedTo(label);
    }
    protected override void OnDetachingFrom(Label label)
    {
        if (bindingWasSet)
            BindingContext = null;
        savedLabel = null;
        base.OnDetachingFrom(label);
    }
    private void Label_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e?.PropertyName?.Equals("Text") ?? false)
            ValidateLabel();
    }

    private void ValidateLabel()
    {
        if (savedLabel is null)
        {
            IsEqual = false;
            return;
        }
        if (!(TestEquality && IsSet(UnequalStyleProperty) && IsSet(EqualValueProperty)))
            IsEqual = true; // if we are not testing just treat it as matching
        else IsEqual = IsSet(TargetValueProperty)
            ? TargetValue == EqualValue
            : decimal.TryParse(savedLabel.Text, NumberStyles.Currency, CultureInfo.CurrentCulture, out decimal d) && d == EqualValue;
        savedLabel.Style = IsEqual ? ValidStyle : UnequalStyle;
    }

    /// <summary>
    /// The <see cref="Style"/> to apply to the element when validation is successful. This is a bindable property.
    /// </summary>
    public Style? ValidStyle
    {
        get => (Style?)GetValue(ValidStyleProperty);
        set => SetValue(ValidStyleProperty, value);
    }

    /// <summary>
    /// The <see cref="Style"/> to apply to the element when validation is successful. This is a bindable property.
    /// </summary>
    public Style? UnequalStyle
    {
        get => (Style?)GetValue(UnequalStyleProperty);
        set => SetValue(UnequalStyleProperty, value);
    }

    /// <summary>
    /// Indicates whether or not the current value is equal to the test value. This is a bindable property.
    /// </summary>
    public bool IsEqual
    {
        get => (bool)GetValue(IsEqualProperty);
        set => SetValue(IsEqualProperty, value);
    }

    /// <summary>
    /// The value to compare if you do not want to use Label.Text. This is a bindable property.
    /// </summary>
    public decimal TargetValue
    {
        get => (decimal)GetValue(TargetValueProperty);
        set => SetValue(TargetValueProperty, value);
    }

    /// <summary>
    /// The value to compare against (if any). This is a bindable property.
    /// </summary>
    public decimal EqualValue
    {
        get => (decimal)GetValue(EqualValueProperty);
        set => SetValue(EqualValueProperty, value);
    }

    /// <summary>
    /// Indicates whether or not the current value is considered valid. This is a bindable property.
    /// </summary>
    public bool TestEquality
    {
        get => (bool)GetValue(TestEqualityProperty);
        set => SetValue(TestEqualityProperty, value);
    }

    /// <summary>
    /// Called whenever the value to compare against changes
    /// </summary>
    /// <param name="bindable">The relevant behavior object</param>
    /// <param name="oldValue">Value before change</param>
    /// <param name="newValue">Value to be set</param>
    protected static void OnSomePropertyChanged(BindableObject bindable, object oldValue, object newValue) => ((CurrencyLabelBehavior)bindable).ValidateLabel();
}
