#nullable enable

using System.Globalization;
using System.Text.RegularExpressions;

namespace DivisiBill.Services;

public class CurrencyValidationBehavior : Behavior<Entry>
{
    public static readonly BindableProperty IsValidProperty =
        BindableProperty.Create(nameof(IsValid), typeof(bool), typeof(CurrencyValidationBehavior), true, BindingMode.OneWayToSource);

    public static readonly BindableProperty MinimumValueProperty =
        BindableProperty.Create(nameof(MinimumValue), typeof(double), typeof(CurrencyValidationBehavior), double.MinValue, propertyChanged: OnSomePropertyChanged);

    public static readonly BindableProperty MaximumValueProperty =
        BindableProperty.Create(nameof(MaximumValue), typeof(double), typeof(CurrencyValidationBehavior), double.MaxValue, propertyChanged: OnSomePropertyChanged);

    public static readonly BindableProperty ValidStyleProperty =
        BindableProperty.Create(nameof(ValidStyle), typeof(Style), typeof(CurrencyValidationBehavior));

    public static readonly BindableProperty InvalidStyleProperty =
        BindableProperty.Create(nameof(InvalidStyle), typeof(Style), typeof(CurrencyValidationBehavior));

    public static readonly BindableProperty UnequalStyleProperty =
        BindableProperty.Create(nameof(UnequalStyle), typeof(Style), typeof(CurrencyValidationBehavior));

    public static readonly BindableProperty IsEqualProperty =
        BindableProperty.Create(nameof(IsEqual), typeof(bool), typeof(CurrencyValidationBehavior), true, BindingMode.OneWayToSource);

    public static readonly BindableProperty EqualValueProperty =
        BindableProperty.Create(nameof(EqualValue), typeof(decimal), typeof(CurrencyValidationBehavior), propertyChanged: OnSomePropertyChanged);

    public static readonly BindableProperty TestEqualityProperty =
        BindableProperty.Create(nameof(TestEquality), typeof(bool), typeof(CurrencyValidationBehavior), true, propertyChanged: OnSomePropertyChanged);

    public static readonly BindableProperty AllowBlankProperty =
        BindableProperty.Create(nameof(AllowBlank), typeof(bool), typeof(CurrencyValidationBehavior), false, propertyChanged: OnSomePropertyChanged);
    private bool bindingWasSet = false;
    private Entry? savedEntry;
    protected override void OnAttachedTo(Entry entry)
    {
        savedEntry = entry;
        if (bindingWasSet = (BindingContext is null))
            SetBinding(BindingContextProperty,
            new Binding
            {
                Path = BindingContextProperty.PropertyName,
                Source = entry,
            });
        entry.TextChanged += OnEntryTextChanged;
        base.OnAttachedTo(entry);
    }

    protected override void OnDetachingFrom(Entry entry)
    {
        if (bindingWasSet)
            BindingContext = null;
        entry.TextChanged -= OnEntryTextChanged;
        savedEntry = null;
        base.OnDetachingFrom(entry);
    }
    private static readonly NumberFormatInfo nfi = new();
    // Optional leading minus then either an integer or floating point number with two digits of precision
    private static readonly Regex NumberRegex = new(@"^-?\d{1,15}(" + ((nfi.CurrencyDecimalSeparator[0] == '.') ? @"\." : ",") + @"\d{" + nfi.CurrencyDecimalDigits + "})?$");

    private void OnEntryTextChanged(object? sender, TextChangedEventArgs args)
    {
        if (sender is Entry entry && (ValidStyle ?? InvalidStyle ?? UnequalStyle) is not null)
            ValidateEntry();
    }

    private void ValidateEntry()
    {
        if (savedEntry is null)
        {
            IsValid = false;
            IsEqual = false;
            return;
        }
        else if (string.IsNullOrWhiteSpace(savedEntry.Text))
        {
            IsEqual = (IsValid = AllowBlank) && (!TestEquality || (UnequalStyle is null || (IsSet(EqualValueProperty) && 0 == EqualValue)));
#pragma warning disable CS8601 // Possible null reference assignment.
            // Warning here from .NET 9 is a bug because Style should be nullable, see https://github.com/dotnet/maui/issues/25227
            savedEntry.Style = IsValid ? IsEqual ? ValidStyle : UnequalStyle : InvalidStyle;
#pragma warning restore CS8601 // Possible null reference assignment.
            return;
        }
        bool formatValid = NumberRegex.IsMatch(savedEntry.Text);
        if (formatValid && double.TryParse(savedEntry.Text, out double f) && f <= MaximumValue && f >= MinimumValue)
        {
            IsValid = true;
            IsEqual = !TestEquality || (UnequalStyle is null || (IsSet(EqualValueProperty) && decimal.Parse(savedEntry.Text) == EqualValue));
#pragma warning disable CS8601 // Possible null reference assignment.
            // Warning here from .NET 9 is a bug because Style should be nullable, see https://github.com/dotnet/maui/issues/25227
            savedEntry.Style = IsEqual ? ValidStyle : UnequalStyle;
#pragma warning restore CS8601 // Possible null reference assignment.
        }
        else
        {
            IsValid = false;
            IsEqual = false;
#pragma warning disable CS8601 // Possible null reference assignment.
            // Warning here from .NET 9 is a bug because Style should be nullable, see https://github.com/dotnet/maui/issues/25227
            savedEntry.Style = InvalidStyle;
#pragma warning restore CS8601 // Possible null reference assignment.
        }
    }

    /// <summary>
    /// Indicates whether or not the current value is considered valid. This is a bindable property.
    /// </summary>
    public bool IsValid
    {
        get => (bool)GetValue(IsValidProperty);
        set => SetValue(IsValidProperty, value);
    }

    /// <summary>
    /// The smallest value that may be set, defined as a double. This is a bindable property.
    /// </summary>
    public double MinimumValue
    {
        get => (double)GetValue(MinimumValueProperty);
        set => SetValue(MinimumValueProperty, value);
    }

    /// <summary>
    /// The largest value that may be set, defined as a double. This is a bindable property.
    /// </summary>
    public double MaximumValue
    {
        get => (double)GetValue(MaximumValueProperty);
        set => SetValue(MaximumValueProperty, value);
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
    public Style? InvalidStyle
    {
        get => (Style?)GetValue(InvalidStyleProperty);
        set => SetValue(InvalidStyleProperty, value);
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
    /// Indicates whether or not the current value is permitted to be blank
    /// (which is treated as zero for comparison purposes). This is a bindable property.
    /// </summary>
    public bool AllowBlank
    {
        get => (bool)GetValue(AllowBlankProperty);
        set => SetValue(AllowBlankProperty, value);
    }

    /// <summary>
    /// Called whenever the value to compare against changes
    /// </summary>
    /// <param name="bindable">The relevant behavior object</param>
    /// <param name="oldValue">Value before change</param>
    /// <param name="newValue">Value to be set</param>
    protected static void OnSomePropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var currencyValidationBehavior = (CurrencyValidationBehavior)bindable;
        currencyValidationBehavior.ValidateEntry();
    }
}

