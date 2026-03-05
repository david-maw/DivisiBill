# AmountEntry Control

## Overview
`AmountEntry` is a custom Entry control with built-in currency validation and user-stopped-typing functionality. It eliminates the need to attach `CurrencyValidationBehavior` and `UserStoppedTypingBehavior` separately in XAML by including both capabilities directly.

## Usage

### In XAML
Add the namespace:
```xaml
xmlns:views="clr-namespace:DivisiBill.Controls"
```

#### Before (with separate behaviors)
```xaml
<Entry
    x:Name="entryTipRate"
    Grid.Row="1"
    Grid.Column="1"
    Completed="OnCompleted"
    Focused="OnFocused"
    Unfocused="OnInputViewUnfocused"
    MaxLength="2"
    ReturnCommand="{Binding UnloadTipRateStringCommand}"
    Text="{Binding TipRateString}">
    <Entry.Behaviors>
        <services:CurrencyValidationBehavior
            InvalidStyle="{StaticResource InvalidEntryStyle}"
            IsValid="{Binding TipRateStringIsValid}"
            MaximumValue="99"
            MinimumValue="0"
            ValidStyle="{StaticResource ValidEntryStyle}" />
        <services:UserStoppedTypingBehavior 
            Command="{Binding UnloadTipRateStringCommand}" 
            StoppedTypingTimeThreshold="{Static vm:PropertiesViewModel.StoppedTypingTimeThreshold}" />
    </Entry.Behaviors>
</Entry>
```

#### After (with AmountEntry)
```xaml
<views:AmountEntry
    Grid.Row="1"
    Grid.Column="1"
    Completed="OnCompleted"
    Focused="OnFocused"
    Unfocused="OnInputViewUnfocused"
    MaxLength="2"
    ReturnCommand="{Binding UnloadTipRateStringCommand}"
    Text="{Binding TipRateString}"
    MinimumValue="0"
    MaximumValue="99"
    ValidStyle="{StaticResource ValidEntryStyle}"
    InvalidStyle="{StaticResource InvalidEntryStyle}"
    IsValid="{Binding TipRateStringIsValid}"
    StoppedTypingCommand="{Binding UnloadTipRateStringCommand}"
    StoppedTypingTimeThreshold="{Static vm:PropertiesViewModel.StoppedTypingTimeThreshold}" />
```

## Bindable Properties

### Currency Validation Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `MinimumValue` | `decimal` | `MinValue` | The smallest value that may be set |
| `MaximumValue` | `decimal` | `MaxValue` | The largest value that may be set |
| `ValidStyle` | `Style?` | `null` | Style to apply when validation succeeds |
| `InvalidStyle` | `Style?` | `null` | Style to apply when validation fails |
| `UnequalStyle` | `Style?` | `null` | Style to apply when value doesn't match expected |
| `IsValid` | `bool` | `true` | **Read-only**. Indicates if current input is a valid number|
| `IsEqual` | `bool` | `true` | **Read-only**. Indicates if value equals expected |
| `EqualValue` | `decimal` | `0` | The value to compare against (if any) |
| `TestEquality` | `bool` | `true` | Whether to test if value equals expected value |
| `AllowBlank` | `bool` | `false` | Whether blank input is allowed |

### User-Stopped-Typing Properties

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `StoppedTypingCommand` | `ICommand?` | `null` | Command to execute when user stops typing |
| `StoppedTypingTimeThreshold` | `int` | `1000` | Time threshold in milliseconds after which user is considered to have stopped typing |

## Pre-configured Defaults

The `AmountEntry` automatically configures:
- `Keyboard` = `Numeric`
- `HorizontalTextAlignment` = `End`
- `HorizontalOptions` = `End`
- `VerticalOptions` = `Center`

## Validation Logic

`AmountEntry` validates text input against currency format rules:
- Accepts optional leading minus sign for negative numbers
- Validates numeric format: integers or decimals with up to 2 decimal places
- Enforces minimum and maximum value bounds
- Can compare against an expected value for equality testing
- Applies appropriate styles based on validation state
- If AllowsBlank is true, empty input is not highlighted but is treated as zero for comparisons

## User-Stopped-Typing Behavior

`AmountEntry` automatically detects when the user stops typing and can execute a command:
- A cancellation timer starts when text changes
- If the user types again before the timer expires, it resets
- When the timer expires without new input, the `StoppedTypingCommand` is executed
- Default threshold is 1000ms (1 second)

## Examples

### Simple Currency Input
```xaml
<views:AmountEntry
    Text="{Binding Amount}"
    MinimumValue="0"
    MaximumValue="999.99"
    ValidStyle="{StaticResource ValidEntryStyle}"
    InvalidStyle="{StaticResource InvalidEntryStyle}"
    IsValid="{Binding AmountIsValid}" />
```

### With Equality Checking
```xaml
<views:AmountEntry
    Text="{Binding ScannedAmount}"
    MinimumValue="0"
    MaximumValue="999999.99"
    ValidStyle="{StaticResource ValidEntryStyle}"
    InvalidStyle="{StaticResource InvalidEntryStyle}"
    UnequalStyle="{StaticResource UnequalEntryStyle}"
    EqualValue="{Binding ExpectedAmount}"
    TestEquality="{Binding HasScannedAmount}"
    IsValid="{Binding ScannedAmountIsValid}"
    IsEqual="{Binding ScannedAmountEquals}" />
```

### With Blank Support
```xaml
<views:AmountEntry
    Text="{Binding OptionalAmount}"
    AllowBlank="True"
    MinimumValue="0"
    MaximumValue="999.99"
    ValidStyle="{StaticResource ValidEntryStyle}"
    InvalidStyle="{StaticResource InvalidEntryStyle}"
    IsValid="{Binding OptionalAmountIsValid}"
    StoppedTypingCommand="{Binding ProcessAmountCommand}"
    StoppedTypingTimeThreshold="1000" />
```

## Notes

- `IsValid` and `IsEqual` are read-only and reflect the validation state
- The control automatically applies styles based on validation results
- Validation occurs whenever text changes or validation-related properties change
- Blank values are treated as zero for comparison purposes when `AllowBlank` is `True`
- The regex pattern respects the system's locale settings for decimal separators
- The `StoppedTypingCommand` is executed on the main thread after the specified `StoppedTypingTimeThreshold`
- Typing more text before the threshold expires cancels and restarts the timer
