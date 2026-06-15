// -----------------------------------------------------------------------------
//  Polyfill: OverloadResolutionPriorityAttribute
//
//  System.Runtime.CompilerServices.OverloadResolutionPriorityAttribute was
//  added in .NET 9. The C# 13 compiler recognises it by full name regardless
//  of the assembly it lives in, so this internal copy gives the same behaviour
//  on net8.0 targets without any extra dependency.
//
//  File        : OverloadResolutionPriorityPolyfill.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
#if !NET9_0_OR_GREATER
namespace System.Runtime.CompilerServices;

[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Property,
    AllowMultiple = false,
    Inherited = false)]
internal sealed class OverloadResolutionPriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}
#endif
