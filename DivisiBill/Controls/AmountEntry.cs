#nullable enable

using DivisiBill.Services;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace DivisiBill.Controls;

/// <summary>
/// A custom Entry control that provides built-in currency validation and user-stopped-typing functionality.
/// Eliminates the need to attach CurrencyValidationBehavior and UserStoppedTypingBehavior separately in XAML.
/// </summary>
public partial class AmountEntry : Entry
{
    #region Global Variables and Constructor
    private static readonly NumberFormatInfo nfi = new();
    // Optional leading minus then either an integer or floating point number with two digits of precision
    private static readonly Regex NumberRegex = new(@"^-?\d{1,15}(" + ((nfi.CurrencyDecimalSeparator[0] == '.') ? @"\." : ",") + @"\d{" + nfi.CurrencyDecimalDigits + "})?$");
    private CancellationTokenSource? stoppedTypingCts;
    private const string defaultText = " "; // Not null so as to avoid PlaceHolder text on Android Material 3 Entry objects

    public AmountEntry()
    {
        // Set a couple of attributes to sensible defaults for a numeric field
        Keyboard = Keyboard.Numeric;
        HorizontalTextAlignment = TextAlignment.End;
        Text = defaultText; // Initial Amount is 0, so show a blank rather than nothing

        // Events causing Amount updates
        TextChanged += OnTextChanged;
        Completed += OnCompleted;
        // Events to deal with the default blank text and ensure the Placeholder is shown in Android when the field is blank and not focused
        Focused += OnFocused;
        Unfocused += OnUnfocused;
    }
    // When the control is focused, if the text is the default blank text then clear it to make it easier for the user to start typing.
    // When the control is unfocused, if the text is blank then set it back to the default blank text to force the display of
    // the Placeholder text in Android.
    private void OnFocused(object? sender, FocusEventArgs e)
    {
        if (Text == defaultText)
            Text = string.Empty;
    }
    private void OnUnfocused(object? sender, FocusEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Text))
            Text = defaultText;
    }
    #endregion
    #region Data Entry Management
    private async void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        ValidateEntry();

        // Reset any stopped typing timer set when processing the previous call
        stoppedTypingCts?.Cancel();
        stoppedTypingCts = new CancellationTokenSource();
        try
        {
            await Task.Delay(StoppedTypingTimeThreshold, stoppedTypingCts.Token);
            // If we get here the Delay timed out because nothing was typed for the threshold duration
            if (IsValid) // only update the Amount if the current text is valid
                DoneWithThisEntry();
        }
        catch (OperationCanceledException)
        {
            // Task was cancelled, probably because the user typed something else before the threshold was reached. Do nothing.
        }
    }

    private void OnCompleted(object? sender, EventArgs e)
    {
        // User pressed the Enter/Return key
        if (IsValid) // only update the Amount if the current text is valid
            DoneWithThisEntry();
    }

    private void DoneWithThisEntry()
    {
        UpdateAmountFromText(); // update the Amount property based on the current text
        UpdateTextFromAmount(); // update the format to signify completion 
        StoppedTypingCommand?.Execute(null);
    }

    private void UpdateAmountFromText()
    {
        if (string.IsNullOrWhiteSpace(Text))
            Amount = 0;
        else if (decimal.TryParse(Text, out decimal value))
            Amount = value;
    }

    private void UpdateTextFromAmount() => Text = Amount == 0 && AllowBlank ? string.Empty : Amount.ToString("0." + new string('0', nfi.CurrencyDecimalDigits)).First(MaxLength)?.TrimEnd('.', ',');

    private void ValidateEntry()
    {
        if ((ValidStyle ?? InvalidStyle ?? UnequalStyle) is null)
            return;

        if (string.IsNullOrWhiteSpace(Text))
        {
            // If the input is blank, show it as valid only if AllowBlank is true but set IsEqual regardless. 
            // If AllowBlank is false then blank input is invalid and Amount will remain unchanged until the user enters something valid.
            IsValid = false;
            if (AllowBlank)
                Amount = 0; // Set Amount to 0 when blank input is allowed to ensure Amount has a consistent value that can be used for binding even when the user hasn't entered anything yet
            IsEqual = !TestEquality || UnequalStyle is null || 0 == EqualValue;
            Style = AllowBlank ? ValidStyle : InvalidStyle;
            return;
        }

        bool formatValid = NumberRegex.IsMatch(Text);
        if (formatValid && decimal.TryParse(Text, out decimal amount) && amount <= MaximumValue && amount >= MinimumValue)
        {
            IsValid = true;
            IsEqual = !TestEquality || UnequalStyle is null || amount == EqualValue;
            Style = IsEqual ? ValidStyle : UnequalStyle;
        }
        else
        {
            IsValid = false;
            IsEqual = false;
            Style = InvalidStyle;
        }
    }
    #endregion
    #region Bindable Properties

    /// <summary>
    /// Hides the inherited Text property. Use the Amount property instead.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public new string? Text
    {
        get => base.Text;
        set => base.Text = value;
    }

    /// <summary>
    /// Gets or sets the minimum allowed value. This is a bindable property.
    /// </summary>
    public static readonly BindableProperty MinimumValueProperty =
        BindableProperty.Create(nameof(MinimumValue),
            typeof(decimal),
            typeof(AmountEntry),
            decimal.MinValue,
            propertyChanged: (bindable, oldValue, newValue) =>
            {
                if (bindable is AmountEntry entry)
                    entry.ValidateEntry();
            });

    public decimal MinimumValue
    {
        get => (decimal)GetValue(MinimumValueProperty);
        set => SetValue(MinimumValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum allowed value. This is a bindable property.
    /// </summary>
    public static readonly BindableProperty MaximumValueProperty =
        BindableProperty.Create(nameof(MaximumValue),
            typeof(decimal),
            typeof(AmountEntry),
            decimal.MaxValue,
            propertyChanged: (bindable, oldValue, newValue) =>
            {
                if (bindable is AmountEntry entry)
                    entry.ValidateEntry();
            });

    public decimal MaximumValue
    {
        get => (decimal)GetValue(MaximumValueProperty);
        set => SetValue(MaximumValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the style to apply when validation succeeds. This is a bindable property.
    /// </summary>
    public static readonly BindableProperty ValidStyleProperty =
        BindableProperty.Create(nameof(ValidStyle),
            typeof(Style),
            typeof(AmountEntry),
            propertyChanged: (bindable, oldValue, newValue) =>
            {
                if (bindable is AmountEntry entry)
                    entry.ValidateEntry();
            });

    public Style? ValidStyle
    {
        get => (Style?)GetValue(ValidStyleProperty);
        set => SetValue(ValidStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the style to apply when validation fails. This is a bindable property.
    /// </summary>
    public static readonly BindableProperty InvalidStyleProperty =
        BindableProperty.Create(nameof(InvalidStyle),
            typeof(Style),
            typeof(AmountEntry),
            propertyChanged: (bindable, oldValue, newValue) =>
            {
                if (bindable is AmountEntry entry)
                    entry.ValidateEntry();
            });

    public Style? InvalidStyle
    {
        get => (Style?)GetValue(InvalidStyleProperty);
        set => SetValue(InvalidStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the style to apply when value doesn't match the expected value. This is a bindable property.
    /// </summary>
    public static readonly BindableProperty UnequalStyleProperty =
        BindableProperty.Create(nameof(UnequalStyle),
            typeof(Style),
            typeof(AmountEntry),
            propertyChanged: (bindable, oldValue, newValue) =>
            {
                if (bindable is AmountEntry entry)
                    entry.ValidateEntry();
            });

    public Style? UnequalStyle
    {
        get => (Style?)GetValue(UnequalStyleProperty);
        set => SetValue(UnequalStyleProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the current input is a valid number. This is a bindable property.
    /// Note that this is not necessarily the same as whether the value equals the expected value (if any), which is indicated by the IsEqual property.
    /// Also note that if AllowBlank is true then IsValid will not be true for blank input because it doesn't represent a valid number.
    /// This is to allow blank input without putting the control into an invalid state visual, which can be desirable in some cases (e.g. when the user hasn't entered
    /// anything yet and you don't want to show an error style). In this case the Amount property will be 0 until the user enters a valid number but IsValid will be false.
    /// </summary>
    public static readonly BindableProperty IsValidProperty =
        BindableProperty.Create(nameof(IsValid),
            typeof(bool),
            typeof(AmountEntry),
            true,
            BindingMode.OneWayToSource);

    public bool IsValid
    {
        get => (bool)GetValue(IsValidProperty);
        private set => SetValue(IsValidProperty, value);
    }

    /// <summary>
    /// Gets a value indicating whether the current value equals the expected value. This is a bindable property.
    /// </summary>
    public static readonly BindableProperty IsEqualProperty =
        BindableProperty.Create(nameof(IsEqual),
            typeof(bool),
            typeof(AmountEntry),
            true,
            BindingMode.OneWayToSource);

    public bool IsEqual
    {
        get => (bool)GetValue(IsEqualProperty);
        private set => SetValue(IsEqualProperty, value);
    }

    /// <summary>
    /// Gets or sets the value to compare against (if any). This is a bindable property.
    /// </summary>
    public static readonly BindableProperty EqualValueProperty =
        BindableProperty.Create(nameof(EqualValue),
            typeof(decimal),
            typeof(AmountEntry),
            propertyChanged: (bindable, oldValue, newValue) =>
            {
                if (bindable is AmountEntry entry)
                {
                    entry.ValidateEntry();
                    if (!entry.testEqualitySet)
                        entry.TestEquality = newValue is not null; // Automatically enable equality testing when an expected value is set or reset
                }
            });

    public decimal EqualValue
    {
        get => (decimal)GetValue(EqualValueProperty);
        set => SetValue(EqualValueProperty, value);
    }

    /// <summary>
    /// Gets or sets whether to test if the current value equals the expected value. This is a bindable property.
    /// </summary>
    public static readonly BindableProperty TestEqualityProperty =
        BindableProperty.Create(nameof(TestEquality),
            typeof(bool),
            typeof(AmountEntry),
            propertyChanged: (bindable, oldValue, newValue) =>
            {
                if (bindable is AmountEntry entry)
                {
                    entry.ValidateEntry();
                    entry.testEqualitySet = true;
                }
            });

    private bool testEqualitySet = false;
    public bool TestEquality
    {
        get => (bool)GetValue(TestEqualityProperty);
        set => SetValue(TestEqualityProperty, value);
    }

    /// <summary>
    /// Gets or sets whether blank input is allowed. This is a bindable property.
    /// </summary>
    public static readonly BindableProperty AllowBlankProperty =
        BindableProperty.Create(nameof(AllowBlank),
            typeof(bool),
            typeof(AmountEntry),
            propertyChanged: (bindable, oldValue, newValue) =>
            {
                if (bindable is AmountEntry entry)
                    entry.ValidateEntry();
            });

    public bool AllowBlank
    {
        get => (bool)GetValue(AllowBlankProperty);
        set => SetValue(AllowBlankProperty, value);
    }

    /// <summary>
    /// Gets or sets the decimal amount value. This is a bindable property.
    /// </summary>
    public static readonly BindableProperty AmountProperty =
        BindableProperty.Create(nameof(Amount),
            typeof(decimal),
            typeof(AmountEntry),
            0m,
            BindingMode.TwoWay,
            propertyChanged: (bindable, oldValue, newValue) =>
            {
                if (bindable is AmountEntry entry && (decimal)newValue != (decimal)oldValue)
                    entry.UpdateTextFromAmount();
            });

    public decimal Amount
    {
        get => (decimal)GetValue(AmountProperty);
        set => SetValue(AmountProperty, value);
    }

    /// <summary>
    /// Gets or sets the command to execute when the user stops typing. This is a bindable property.
    /// </summary>
    public static readonly BindableProperty StoppedTypingCommandProperty =
        BindableProperty.Create(nameof(StoppedTypingCommand),
            typeof(ICommand),
            typeof(AmountEntry));

    public ICommand? StoppedTypingCommand
    {
        get => (ICommand?)GetValue(StoppedTypingCommandProperty);
        set => SetValue(StoppedTypingCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets the time threshold (in milliseconds) after which the user is considered to have stopped typing. This is a bindable property.
    /// </summary>
    public static readonly BindableProperty StoppedTypingTimeThresholdProperty =
        BindableProperty.Create(nameof(StoppedTypingTimeThreshold),
            typeof(int),
            typeof(AmountEntry),
            1000);

    public int StoppedTypingTimeThreshold
    {
        get => (int)GetValue(StoppedTypingTimeThresholdProperty);
        set => SetValue(StoppedTypingTimeThresholdProperty, value);
    }
    #endregion
}
