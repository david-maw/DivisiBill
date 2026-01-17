using DivisiBill.InAppBilling;

namespace DivisiBill;

/// <summary>
/// Cross platform InAppBilling implementations
/// </summary>
public class CrossInAppBilling
{
    private static Lazy<IInAppBilling> implementation = new(CreateInAppBilling, System.Threading.LazyThreadSafetyMode.PublicationOnly);


    /// <summary>
    /// Gets if the plugin is supported on the current platform.
    /// </summary>
    public static bool IsSupported => implementation.Value != null;

    /// <summary>
    /// Current plugin implementation to use
    /// </summary>
    public static IInAppBilling Current => implementation.Value ?? throw NotImplementedInReferenceAssembly();

#if ANDROID 
    private static IInAppBilling CreateInAppBilling() => new Platforms.Android.InAppBillingImplementation();
#elif WINDOWS
    private static IInAppBilling CreateInAppBilling() => new Platforms.Windows.InAppBillingImplementation();
#else
    static IInAppBilling CreateInAppBilling() => null;
#endif

    internal static Exception NotImplementedInReferenceAssembly() =>
        new NotImplementedException("Billing functionality is not implemented in this environment.");


    /// <summary>
    /// Dispose of everything 
    /// </summary>
    public static void Dispose()
    {
        if (implementation != null && implementation.IsValueCreated)
        {
            implementation.Value.Dispose();

            implementation = new Lazy<IInAppBilling>(CreateInAppBilling, LazyThreadSafetyMode.PublicationOnly);
        }
    }
}
