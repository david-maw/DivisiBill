#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace System.Runtime.CompilerServices
#pragma warning restore IDE0130 // Namespace does not match folder structure
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false)]
    public sealed class UnionAttribute : Attribute;

    public interface IUnion
    {
        object? Value { get; }
    }
}