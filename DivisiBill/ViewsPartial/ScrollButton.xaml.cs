using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

namespace DivisiBill.ViewsPartial;

/// <summary>
/// A custom control that provides list navigation functionality with visual feedback.
/// Supports both quick press for incremental scrolling and long press for jumping to list extremities.
/// The button appearance (an up or down arrow) automatically adjusts based on its position.
/// </summary>
public partial class ScrollButton : ContentView
{
    /// <summary>
    /// Timer used to reset visual state back to normal after user interactions
    /// </summary>
    private readonly Timer StateTimer;

    public ScrollButton()
    {
        InitializeComponent();
        StateTimer = new Timer(_ => MainThread.InvokeOnMainThreadAsync(() => VisualStateManager.GoToState(this, "Normal")));
    }

    /// <summary>
    /// Handles quick press interactions, triggering incremental scroll in the specified direction.
    /// Shows a scale-down animation and executes the bound command with "Up" or "Down" parameter.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPress))]
    public async Task Press(string parameter)
    {
        await this.ScaleToAsync(0.5, 100);
        await this.ScaleToAsync(1.0, 100);
        Command.Execute(parameter);
        StateTimer.Change(200, Timeout.Infinite);
    }
    private bool CanPress(string parameter) => Command is not null && Command.CanExecute(parameter);

    /// <summary>
    /// Handles long press interactions, triggering scroll to list extremities.
    /// Shows a scale-up animation and executes the bound command with "Start" or "End" parameter.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanLongPress))]
    public async Task LongPress(string parameter)
    {
        await this.ScaleToAsync(1.5, 100);
        await this.ScaleToAsync(1.0, 100);
        Command.Execute(parameter);
        StateTimer.Change(200, Timeout.Infinite);
    }
    private bool CanLongPress(string parameter) => Command is not null && Command.CanExecute(parameter);

    /// <summary>
    /// The command to execute when the button is pressed (long or quick).
    /// Receives "Up"/"Down" for quick press or "Start"/"End" for long press.
    /// </summary>
    public ICommand Command
    {
        get => (ICommand)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(ScrollButton), null, propertyChanged: OnCommandChanged);
    public static void OnCommandChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (ScrollButton)bindable;
        control.Command = (ICommand)newValue;
    }

    /// <summary>
    /// Controls the vertical positioning of the scroll button (Start or End).
    /// Automatically updates the Down property based on position.
    /// </summary>
    public static new readonly BindableProperty VerticalOptionsProperty =
        BindableProperty.Create(nameof(VerticalOptions), typeof(LayoutOptions), typeof(ScrollButton), LayoutOptions.End, propertyChanged: OnVerticalOptionsChanged);
    private static void OnVerticalOptionsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var control = (ScrollButton)bindable;
        LayoutOptions newLayoutOptions = (LayoutOptions)newValue;
        ((ContentView)bindable).VerticalOptions = newLayoutOptions;
        control.Down = newLayoutOptions.Equals(LayoutOptions.End);
    }

    /// <summary>
    /// Indicates the direction of the arrow icon and scroll behavior. Used to allow the user to read the button
    /// direction determined from VerticalOptions.
    /// </summary>
    public bool Down
    {
        get => (bool)GetValue(DownProperty);
        set => SetValue(DownProperty, value);
    }

    public static readonly BindableProperty DownProperty =
        BindableProperty.Create(nameof(Down), typeof(bool), typeof(ScrollButton), false);
}