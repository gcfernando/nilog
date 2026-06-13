namespace Nilog;

// A shared, do-nothing scope handed back when there is nothing to push (for example an
// empty context). Being a singleton, it lets the scope helpers stay allocation-free on
// the "no work" path while still returning a valid IDisposable for the caller's using.
internal sealed class NullScope : IDisposable
{
    public static readonly NullScope Instance = new();

    private NullScope()
    { }

    public void Dispose()
    { }
}
