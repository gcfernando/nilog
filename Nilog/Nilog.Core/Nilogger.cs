// -----------------------------------------------------------------------------
//  Nilog — zero-allocation, high-performance logging extensions for
//  Microsoft.Extensions.Logging. Strongly-typed Write* overloads skip the
//  params object[] allocation (and allocate nothing when a level is disabled),
//  plus scopes, formatted exception reports, and process-wide hooks.
//
//  File        : Nilogger.cs
//  Developer   ::> Gehan Fernando
// -----------------------------------------------------------------------------
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;

namespace Nilog;

/// <summary>
/// High-performance, low-allocation logging helpers built on top of
/// <see cref="ILogger"/> and <c>Microsoft.Extensions.Logging</c>.
/// </summary>
/// <remarks>
/// <para>
/// Nilog exists for one reason: the stock <c>ILogger</c> extension methods take a
/// <c>params object[]</c>, which allocates an array on every call - even when the log
/// level is turned off and the message is thrown away. Nilog replaces that hot path
/// with strongly-typed overloads (up to three arguments) that build a small readonly
/// struct instead of an array, so a disabled log call allocates nothing at all and an
/// enabled one only pays for the unavoidable boxing of value-type arguments.
/// </para>
/// <para>
/// Everything here is static and thread-safe. Parsed message templates, pre-compiled
/// <see cref="LoggerMessage"/> delegates, and a pooled <see cref="StringBuilder"/> are
/// shared across the whole process, so there is nothing to construct, register, or
/// dispose before you start logging.
/// </para>
/// </remarks>
public static partial class Nilogger
{
    // Best-effort cleanup: force one final UTC timestamp refresh when the process or
    // AppDomain is going away, so the cache reflects the true shutdown time.
    static Nilogger()
    {
        AppDomain.CurrentDomain.ProcessExit += static (_, __) => SafeShutdown();
        AppDomain.CurrentDomain.DomainUnload += static (_, __) => SafeShutdown();
    }

    #region Cached EventIds (single source of truth)

    // One EventId per level, created once and reused everywhere. GetEventId reads from
    // these fields so a given level always emits the same EventId regardless of overload.
    private static readonly EventId _traceId = new(0, "TraceEvent");
    private static readonly EventId _debugId = new(1, "DebugEvent");
    private static readonly EventId _infoId = new(2, "InformationEvent");
    private static readonly EventId _warnId = new(3, "WarningEvent");
    private static readonly EventId _errorId = new(4, "ErrorEvent");
    private static readonly EventId _criticalId = new(5, "CriticalEvent");
    private static readonly EventId _unknownId = new(9999, "UnknownEvent");

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static EventId GetEventId(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => _traceId,
            LogLevel.Debug => _debugId,
            LogLevel.Information => _infoId,
            LogLevel.Warning => _warnId,
            LogLevel.Error => _errorId,
            LogLevel.Critical => _criticalId,
            _ => _unknownId
        };
    }

    #endregion Cached EventIds (single source of truth)

    #region Timestamp Cache (Allocation-Free UTC)

    // Formatting DateTime.UtcNow on every exception would allocate a string each time.
    // Rather than a background Timer ticking forever (a thread-pool timer queue entry
    // firing every millisecond for the lifetime of the process, even when nothing is
    // logging), the cached string is refreshed lazily on read: a reader compares the
    // current tick count against the last refresh and only reformats when >= 1 ms has
    // elapsed, which preserves the same effective freshness with zero idle-time cost.
    // The thread-local char buffer lets TryFormat write without allocating an array.
    private static readonly ThreadLocal<char[]> _utcBuffer = new(() => new char[33]);
    private static long _lastUtcRefreshTicks = Environment.TickCount64;
    private static volatile string _cachedUtc = FormatUtc();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static string GetCachedUtc()
    {
        long now = Environment.TickCount64;
        long last = Volatile.Read(ref _lastUtcRefreshTicks);
        if (now - last < 1)
        {
            return _cachedUtc;
        }

        // Only the thread that wins the race refreshes; everyone else reads the value
        // that refresher just (or is about to) produce. A double-refresh under
        // contention is harmless - it just recomputes the same string.
        if (Interlocked.CompareExchange(ref _lastUtcRefreshTicks, now, last) == last)
        {
            UpdateUtc(null);
        }

        return _cachedUtc;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void UpdateUtc(object? _)
    {
        try
        {
            Interlocked.Exchange(ref _cachedUtc, FormatUtc());
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            // A missed timestamp refresh is not fatal; swallowing here prevents the
            // thread-pool unhandled-exception crash path (.NET 6+).
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static string FormatUtc()
    {
        char[] buf = _utcBuffer.Value ?? new char[33];
        _ = DateTime.UtcNow.TryFormat(buf, out int len, "O", CultureInfo.InvariantCulture);
        return new string(buf, 0, len);
    }

    /// <summary>
    /// Forces a final refresh of the cached UTC timestamp used in exception reports.
    /// </summary>
    /// <remarks>
    /// The timestamp cache is refreshed lazily on read (no background timer runs), so
    /// there is nothing to stop or dispose. This method is kept for source/binary
    /// compatibility with earlier versions and for deterministic teardown - it is
    /// always safe to call, including more than once.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ShutdownUtcTimer()
    {
        // No timer to dispose anymore - just force a final refresh so the cached
        // timestamp is current regardless of who calls this or how many times.
        Interlocked.Exchange(ref _cachedUtc, FormatUtc());
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SafeShutdown()
    {
        try
        {
            ShutdownUtcTimer();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            // Shutdown is best-effort; swallow everything except the truly fatal so we
            // never throw out of a ProcessExit/DomainUnload handler.
            Debug.WriteLine($"Nilogger.SafeShutdown encountered non-fatal error: {ex}");
        }
    }

    #endregion Timestamp Cache (Allocation-Free UTC)

    #region Object Pool & Exception Formatter

    // A pooled StringBuilder keeps exception formatting allocation-free under load. The
    // pool is sized to the core count so concurrent formatters rarely contend for one.
    private static readonly ObjectPoolProvider _poolProvider = new DefaultObjectPoolProvider
    {
        MaximumRetained = Environment.ProcessorCount * 8
    };

    private static readonly ObjectPool<StringBuilder> _sbPool = _poolProvider.CreateStringBuilderPool();
    private static volatile Func<Exception, string, bool, string> _exceptionFormatter = FormatExceptionMessageInternal;

    /// <summary>
    /// Gets or sets the function used to turn an exception into a log message.
    /// </summary>
    /// <value>
    /// A delegate that receives the exception, a title, and a flag asking for the
    /// verbose form (stack trace and inner exceptions), and returns the formatted text.
    /// Assigning <see langword="null"/> restores the built-in formatter.
    /// </value>
    /// <remarks>
    /// Replace this when you want exceptions rendered to match your house style - for
    /// example as JSON, or with extra fields pulled from <see cref="Exception.Data"/>.
    /// It is consumed by <see cref="WriteErrorException"/> and
    /// <see cref="WriteCriticalException"/>.
    /// </remarks>
    public static Func<Exception, string, bool, string> ExceptionFormatter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        get => _exceptionFormatter;
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        set => _exceptionFormatter = value ?? FormatExceptionMessageInternal;
    }

    #endregion Object Pool & Exception Formatter

    #region Zero-Allocation Typed State (beats params object[])

    // Each distinct template is tokenized exactly once and the result is cached. This is
    // the same trick LoggerMessage.Define uses internally, except here it works for
    // templates that are only known at runtime rather than fixed at compile time.
    private static readonly ConcurrentDictionary<string, TemplateFormatter> _templateCache = new(StringComparer.Ordinal);

    // Warn once (via Debug.WriteLine) when the cache looks unexpectedly large, which
    // almost always means the caller is interpolating dynamic values into the template
    // string instead of passing them as arguments. The threshold is generous; a healthy
    // app typically has tens of distinct templates, not thousands.
    private static int _templateCacheWarned;
    private static volatile int _maxTemplateCacheEntries = 10_000;

    /// <summary>
    /// Gets or sets the maximum number of parsed templates to keep in the template cache.
    /// </summary>
    /// <value>A positive integer; defaults to 10,000. Values ≤ 0 are ignored.</value>
    /// <remarks>
    /// Once the cache reaches this limit, new templates are still parsed correctly on each
    /// call but the result is not stored, preventing unbounded memory growth from callers
    /// that use interpolated strings as message templates (e.g. <c>WriteInformation($"User {id}")</c>).
    /// A diagnostic trace is emitted once when the threshold is first hit.
    /// </remarks>
    public static int MaxTemplateCacheEntries
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _maxTemplateCacheEntries;
        set
        {
            if (value > 0)
                _maxTemplateCacheEntries = value;
        }
    }

    // Per-thread single-slot fast path. Message templates are almost always string
    // literals, which the runtime interns - so the exact same call site (e.g. inside a
    // hot loop or worker) passes the identical string reference on every call. A
    // reference-equality check against the last template this thread resolved skips the
    // dictionary hash/probe entirely for that overwhelmingly common case, with zero risk:
    // a miss just falls through to the normal lookup below, so behaviour is unchanged.
    [ThreadStatic]
    private static string? _lastTemplate;

    [ThreadStatic]
    private static TemplateFormatter? _lastFormatter;

    // Hot path: a single dictionary probe, inlined at every call site. Falls through to
    // AddFormatter only on a cache miss, keeping the common case branch-light.
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static TemplateFormatter GetFormatter(string template)
    {
        if (ReferenceEquals(template, _lastTemplate) && _lastFormatter is TemplateFormatter cached)
        {
            return cached;
        }

        TemplateFormatter formatter = _templateCache.TryGetValue(template, out TemplateFormatter? hit) ? hit : AddFormatter(template);
        _lastTemplate = template;
        _lastFormatter = formatter;
        return formatter;
    }

    // Slow path: add the template to the cache. When the cache has reached its configured
    // limit, parse on the fly without caching to protect against unbounded memory growth.
    // NoInlining keeps this off the inlined hot path.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static TemplateFormatter AddFormatter(string template)
    {
        int limit = _maxTemplateCacheEntries;
        if (_templateCache.Count >= limit)
        {
            if (Interlocked.CompareExchange(ref _templateCacheWarned, 1, 0) == 0)
            {
                Debug.WriteLine(
                    $"[Nilogger] Template cache has reached {limit} entries. " +
                    "New templates will be parsed on each call but not cached to protect memory. " +
                    "This typically means interpolated strings are being used as message templates " +
                    "instead of placeholder arguments — for example, use " +
                    "WriteInformation(\"User {Id} signed in\", userId) " +
                    "rather than WriteInformation($\"User {userId} signed in\").");
            }
            return new TemplateFormatter(template);
        }

        TemplateFormatter formatter = _templateCache.GetOrAdd(template, static t => new TemplateFormatter(t));
        if (_templateCache.Count > limit &&
            Interlocked.CompareExchange(ref _templateCacheWarned, 1, 0) == 0)
        {
            Debug.WriteLine(
                $"[Nilogger] Template cache has exceeded {limit} entries. " +
                "This typically means interpolated strings are being used as message templates " +
                "instead of placeholder arguments — for example, use " +
                "WriteInformation(\"User {Id} signed in\", userId) " +
                "rather than WriteInformation($\"User {userId} signed in\").");
        }
        return formatter;
    }

    // Parses a message template once and remembers two things: the property names (so
    // structured sinks get {Key, Value} pairs) and a positional format string like
    // "User {0} did {1}" that string.Format can consume directly.
    private sealed class TemplateFormatter
    {
        private readonly string _format;
        private readonly Segment[] _segments;
        private readonly bool _hasFormatSpecifiers;
        private readonly int _maxArgIndex;

        public string Template { get; }
        public string[] Names { get; }

        // A parsed piece of the template for the span-based fast render path: either a
        // literal run of text (already unescaped - single '{'/'}', not "{{"/"}}") or a
        // reference to the Nth call-site argument. -1 in ArgIndex marks a literal.
        private readonly struct Segment
        {
            public readonly string? Literal;
            public readonly int ArgIndex;
            public Segment(string literal) { Literal = literal; ArgIndex = -1; }
            public Segment(int argIndex) { Literal = null; ArgIndex = argIndex; }
        }

        public TemplateFormatter(string template)
        {
            Template = template ?? string.Empty;
            List<string> names = new(4);
            StringBuilder sb = new(Template.Length + 8);
            List<Segment> segs = new(4);
            StringBuilder segLit = new(Template.Length);
            bool hasSpecifiers = false;
            int maxArgIndex = -1;
            int len = Template.Length;
            int pos = 0;

            while (pos < len)
            {
                char c = Template[pos];
                if (c == '{')
                {
                    // "{{" is a literal brace, not the start of a placeholder.
                    if (pos + 1 < len && Template[pos + 1] == '{')
                    {
                        _ = sb.Append("{{");
                        _ = segLit.Append('{');
                        pos += 2;
                        continue;
                    }

                    // An unterminated '{' is treated as literal text and ends parsing.
                    int close = Template.IndexOf('}', pos + 1);
                    if (close < 0)
                    {
                        _ = sb.Append(Template, pos, len - pos);
                        _ = segLit.Append(Template, pos, len - pos);
                        break;
                    }

                    // Split the placeholder into its name and any ",align"/":format"
                    // suffix, then re-emit it as a positional slot ({0}, {1}, ...).
                    string token = Template.Substring(pos + 1, close - pos - 1);
                    int suffixStart = token.IndexOfAny(_suffixChars);
                    string name = suffixStart < 0 ? token : token[..suffixStart];
                    string suffix = suffixStart < 0 ? string.Empty : token[suffixStart..];

                    _ = sb.Append('{').Append(names.Count).Append(suffix).Append('}');

                    int argIndex = names.Count;
                    if (segLit.Length > 0)
                    {
                        segs.Add(new Segment(segLit.ToString()));
                        _ = segLit.Clear();
                    }
                    segs.Add(new Segment(argIndex));
                    if (suffix.Length > 0)
                    {
                        hasSpecifiers = true;
                    }
                    if (argIndex > maxArgIndex)
                    {
                        maxArgIndex = argIndex;
                    }

                    names.Add(name.Trim());
                    pos = close + 1;
                }
                else if (c == '}')
                {
                    // "}}" is a literal brace; a lone '}' is escaped so string.Format
                    // won't choke on it later.
                    if (pos + 1 < len && Template[pos + 1] == '}')
                    {
                        _ = sb.Append("}}");
                        _ = segLit.Append('}');
                        pos += 2;
                        continue;
                    }

                    _ = sb.Append("}}");
                    _ = segLit.Append('}');
                    pos++;
                }
                else
                {
                    _ = sb.Append(c);
                    _ = segLit.Append(c);
                    pos++;
                }
            }

            if (segLit.Length > 0)
            {
                segs.Add(new Segment(segLit.ToString()));
            }

            _format = sb.ToString();
            Names = [.. names];
            _segments = [.. segs];
            _hasFormatSpecifiers = hasSpecifiers;
            _maxArgIndex = maxArgIndex;
        }

        private static readonly char[] _suffixChars = [',', ':'];

        // Falls back to the positional index when a template has fewer names than
        // arguments, so structured sinks still get a usable key.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string GetName(int index)
        {
            return (uint)index < (uint)Names.Length ? Names[index] : index.ToString(CultureInfo.InvariantCulture);
        }

        // Logging must never throw. If the template and the supplied arguments don't
        // line up, we hand back the raw template rather than let a FormatException
        // bubble out of a log call.
        //
        // Note: we always call string.Format even when Names is empty. The reason is that
        // _format preserves "{{" and "}}" verbatim; string.Format is what converts them to
        // literal "{" and "}". Skipping it when Names.Length == 0 would leave those escape
        // sequences unprocessed in the rendered message — e.g. "{{val}}" instead of "{val}".
        // When _format has no positional slots ({0}, {1}, …), string.Format returns it
        // unchanged, so the extra call costs nothing in that case.
        public string Format(object a0)
        {
            try
            { return string.Format(CultureInfo.InvariantCulture, _format, a0); }
            catch (FormatException) { return Template; }
        }

        public string Format(object a0, object a1)
        {
            try
            { return string.Format(CultureInfo.InvariantCulture, _format, a0, a1); }
            catch (FormatException) { return Template; }
        }

        public string Format(object a0, object a1, object a2)
        {
            try
            { return string.Format(CultureInfo.InvariantCulture, _format, a0, a1, a2); }
            catch (FormatException) { return Template; }
        }

        public string Format(object a0, object a1, object a2, object a3)
        {
            try
            { return string.Format(CultureInfo.InvariantCulture, _format, a0, a1, a2, a3); }
            catch (FormatException) { return Template; }
        }

        public string Format(object a0, object a1, object a2, object a3, object a4)
        {
            try
            { return string.Format(CultureInfo.InvariantCulture, _format, a0, a1, a2, a3, a4); }
            catch (FormatException) { return Template; }
        }

        // Array-based fallback used by the source-generated high-arity (6+ argument) typed
        // overloads, where there is no fixed Format(object, …) shape to bind to. An explicit
        // object?[] argument binds here rather than to Format(object a0) because object?[] is
        // a more specific match for the parameter than object. Same never-throw contract as
        // the fixed-arity overloads above.
        public string Format(params object?[] args)
        {
            try
            { return string.Format(CultureInfo.InvariantCulture, _format, args); }
            catch (FormatException) { return Template; }
        }

        // Stack buffer size for the fast render path below. Generous enough for the vast
        // majority of log lines; anything larger simply falls back to Format(), so there
        // is no correctness ceiling - only a performance one.
        private const int StackBufferChars = 256;

        // Span-based render path: no StringBuilder, no pool, no array - just a
        // stack-allocated buffer that is copied into the one string the caller needs.
        // Used only for plain "{Name}" placeholders (no ":format"/",align" suffix) where
        // every supplied argument is referenced - i.e. exactly the templates that don't
        // need string.Format's interpretation at all. Anything else (format specifiers,
        // alignment, an argument-count mismatch, or output that overflows the stack
        // buffer) defers to the battle-tested Format() overloads above, so behaviour for
        // every existing template is unchanged.
        // Supports up to eight arguments so the source-generated 6-8 arg overloads render
        // through the same allocation-free span path as the hand-written 1-5 arg ones,
        // instead of building an object?[] in ToString(). Arguments 6-8 default to null so
        // every existing 1-5 arg caller is unaffected.
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public string Render(int argCount, object? a0, object? a1 = null, object? a2 = null, object? a3 = null, object? a4 = null,
            object? a5 = null, object? a6 = null, object? a7 = null)
        {
            if (_hasFormatSpecifiers || _maxArgIndex >= argCount)
            {
                return FormatFallback(argCount, a0, a1, a2, a3, a4, a5, a6, a7);
            }

            Span<char> buffer = stackalloc char[StackBufferChars];
            int pos = 0;
            Segment[] segments = _segments;
            for (int i = 0; i < segments.Length; i++)
            {
                Segment seg = segments[i];
                bool ok = seg.ArgIndex < 0
                    ? TryWriteLiteral(seg.Literal!, buffer, ref pos)
                    : TryWriteValue(seg.ArgIndex switch { 0 => a0, 1 => a1, 2 => a2, 3 => a3, 4 => a4, 5 => a5, 6 => a6, _ => a7 }, buffer, ref pos);

                if (!ok)
                {
                    // Buffer overflow on an unusually large value - fall back rather than
                    // truncate. Rare in practice; correctness wins over the fast path.
                    return FormatFallback(argCount, a0, a1, a2, a3, a4, a5, a6, a7);
                }
            }

            return new string(buffer[..pos]);
        }

        // Slow path shared by Render: templates with format specifiers/alignment, an
        // argument-count mismatch, or stack-buffer overflow defer to string.Format. Only the
        // 6-8 arg cases build an object?[]; the common 1-5 plain-template path never reaches
        // here. NoInlining keeps it off Render's hot path.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private string FormatFallback(int argCount, object? a0, object? a1, object? a2, object? a3, object? a4, object? a5, object? a6, object? a7)
        {
            return argCount switch
            {
                1 => Format(a0!),
                2 => Format(a0!, a1!),
                3 => Format(a0!, a1!, a2!),
                4 => Format(a0!, a1!, a2!, a3!),
                5 => Format(a0!, a1!, a2!, a3!, a4!),
                6 => Format(new object?[] { a0, a1, a2, a3, a4, a5 }),
                7 => Format(new object?[] { a0, a1, a2, a3, a4, a5, a6 }),
                _ => Format(new object?[] { a0, a1, a2, a3, a4, a5, a6, a7 }),
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryWriteLiteral(string literal, Span<char> buffer, ref int pos)
        {
            if (literal.Length > buffer.Length - pos)
            {
                return false;
            }

            literal.AsSpan().CopyTo(buffer.Slice(pos));
            pos += literal.Length;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryWriteValue(object? value, Span<char> buffer, ref int pos)
        {
            // string.Format renders a null argument as an empty string - match that
            // exactly so the fast path is indistinguishable from the fallback.
            if (value is null)
            {
                return true;
            }

            if (value is ISpanFormattable formattable)
            {
                if (formattable.TryFormat(buffer[pos..], out int written, default, CultureInfo.InvariantCulture))
                {
                    pos += written;
                    return true;
                }
                return false;
            }

            return TryWriteLiteral(value.ToString() ?? string.Empty, buffer, ref pos);
        }
    }

    // The typed log state. It is a readonly struct so it lives on the stack, and it
    // implements IReadOnlyList so structured sinks can enumerate the named values. The
    // final entry is always "{OriginalFormat}" - the convention Microsoft sinks look for
    // to recover the un-rendered template.
    private readonly struct LogState<T0> : IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly TemplateFormatter _fmt;
        private readonly T0 _v0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LogState(TemplateFormatter fmt, T0 v0)
        {
            (_fmt, _v0) = (fmt, v0);
        }

        public int Count => 2;

        public KeyValuePair<string, object> this[int index] => index switch
        {
            0 => new(_fmt.GetName(0), _v0!),
            1 => new("{OriginalFormat}", _fmt.Template),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        // Struct enumerator: foreach on LogState<T0> is allocation-free (duck-typed
        // pattern). The explicit interface implementations box for IEnumerable<T>
        // callers, but the common structured-sink path avoids that allocation.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new(this);

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
            => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => _fmt.Render(1, _v0!);

        public struct Enumerator : IEnumerator<KeyValuePair<string, object>>
        {
            private readonly LogState<T0> _state;
            private int _index;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator(LogState<T0> state)
            {
                _state = state;
                _index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                _index++;
                return _index < _state.Count;
            }

            public readonly KeyValuePair<string, object> Current => _state[_index];
            readonly object IEnumerator.Current => _state[_index];

            public void Reset() { _index = -1; }
            public readonly void Dispose() { }
        }
    }

    private readonly struct LogState<T0, T1> : IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly TemplateFormatter _fmt;
        private readonly T0 _v0;
        private readonly T1 _v1;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LogState(TemplateFormatter fmt, T0 v0, T1 v1)
        {
            (_fmt, _v0, _v1) = (fmt, v0, v1);
        }

        public int Count => 3;

        public KeyValuePair<string, object> this[int index] => index switch
        {
            0 => new(_fmt.GetName(0), _v0!),
            1 => new(_fmt.GetName(1), _v1!),
            2 => new("{OriginalFormat}", _fmt.Template),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new(this);

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
            => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => _fmt.Render(2, _v0!, _v1!);

        public struct Enumerator : IEnumerator<KeyValuePair<string, object>>
        {
            private readonly LogState<T0, T1> _state;
            private int _index;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator(LogState<T0, T1> state)
            {
                _state = state;
                _index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                _index++;
                return _index < _state.Count;
            }

            public readonly KeyValuePair<string, object> Current => _state[_index];
            readonly object IEnumerator.Current => _state[_index];

            public void Reset() { _index = -1; }
            public readonly void Dispose() { }
        }
    }

    private readonly struct LogState<T0, T1, T2> : IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly TemplateFormatter _fmt;
        private readonly T0 _v0;
        private readonly T1 _v1;
        private readonly T2 _v2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LogState(TemplateFormatter fmt, T0 v0, T1 v1, T2 v2)
        {
            (_fmt, _v0, _v1, _v2) = (fmt, v0, v1, v2);
        }

        public int Count => 4;

        public KeyValuePair<string, object> this[int index] => index switch
        {
            0 => new(_fmt.GetName(0), _v0!),
            1 => new(_fmt.GetName(1), _v1!),
            2 => new(_fmt.GetName(2), _v2!),
            3 => new("{OriginalFormat}", _fmt.Template),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new(this);

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
            => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => _fmt.Render(3, _v0!, _v1!, _v2!);

        public struct Enumerator : IEnumerator<KeyValuePair<string, object>>
        {
            private readonly LogState<T0, T1, T2> _state;
            private int _index;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator(LogState<T0, T1, T2> state)
            {
                _state = state;
                _index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                _index++;
                return _index < _state.Count;
            }

            public readonly KeyValuePair<string, object> Current => _state[_index];
            readonly object IEnumerator.Current => _state[_index];

            public void Reset() { _index = -1; }
            public readonly void Dispose() { }
        }
    }

    private readonly struct LogState<T0, T1, T2, T3> : IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly TemplateFormatter _fmt;
        private readonly T0 _v0;
        private readonly T1 _v1;
        private readonly T2 _v2;
        private readonly T3 _v3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LogState(TemplateFormatter fmt, T0 v0, T1 v1, T2 v2, T3 v3)
        {
            (_fmt, _v0, _v1, _v2, _v3) = (fmt, v0, v1, v2, v3);
        }

        public int Count => 5;

        public KeyValuePair<string, object> this[int index] => index switch
        {
            0 => new(_fmt.GetName(0), _v0!),
            1 => new(_fmt.GetName(1), _v1!),
            2 => new(_fmt.GetName(2), _v2!),
            3 => new(_fmt.GetName(3), _v3!),
            4 => new("{OriginalFormat}", _fmt.Template),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new(this);

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
            => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => _fmt.Render(4, _v0!, _v1!, _v2!, _v3!);

        public struct Enumerator : IEnumerator<KeyValuePair<string, object>>
        {
            private readonly LogState<T0, T1, T2, T3> _state;
            private int _index;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator(LogState<T0, T1, T2, T3> state)
            {
                _state = state;
                _index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                _index++;
                return _index < _state.Count;
            }

            public readonly KeyValuePair<string, object> Current => _state[_index];
            readonly object IEnumerator.Current => _state[_index];

            public void Reset() { _index = -1; }
            public readonly void Dispose() { }
        }
    }

    private readonly struct LogState<T0, T1, T2, T3, T4> : IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly TemplateFormatter _fmt;
        private readonly T0 _v0;
        private readonly T1 _v1;
        private readonly T2 _v2;
        private readonly T3 _v3;
        private readonly T4 _v4;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LogState(TemplateFormatter fmt, T0 v0, T1 v1, T2 v2, T3 v3, T4 v4)
        {
            (_fmt, _v0, _v1, _v2, _v3, _v4) = (fmt, v0, v1, v2, v3, v4);
        }

        public int Count => 6;

        public KeyValuePair<string, object> this[int index] => index switch
        {
            0 => new(_fmt.GetName(0), _v0!),
            1 => new(_fmt.GetName(1), _v1!),
            2 => new(_fmt.GetName(2), _v2!),
            3 => new(_fmt.GetName(3), _v3!),
            4 => new(_fmt.GetName(4), _v4!),
            5 => new("{OriginalFormat}", _fmt.Template),
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new(this);

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
            => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => _fmt.Render(5, _v0!, _v1!, _v2!, _v3!, _v4!);

        public struct Enumerator : IEnumerator<KeyValuePair<string, object>>
        {
            private readonly LogState<T0, T1, T2, T3, T4> _state;
            private int _index;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator(LogState<T0, T1, T2, T3, T4> state)
            {
                _state = state;
                _index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                _index++;
                return _index < _state.Count;
            }

            public readonly KeyValuePair<string, object> Current => _state[_index];
            readonly object IEnumerator.Current => _state[_index];

            public void Reset() { _index = -1; }
            public readonly void Dispose() { }
        }
    }

    #endregion Zero-Allocation Typed State (beats params object[])

    #region Generic Log Methods

    /// <summary>
    /// Logs a plain message at the given level with no template arguments.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The severity to log at.</param>
    /// <param name="message">The message text. A <see langword="null"/> value is logged as <c>"N/A"</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void Log(ILogger logger, LogLevel level, string message)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        logger.Log(level, GetEventId(level), message ?? "N/A", null, static (s, _) => s);
    }

    /// <summary>
    /// Logs a templated message with one argument and no array allocation.
    /// </summary>
    /// <typeparam name="T0">The type of the template argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The severity to log at.</param>
    /// <param name="message">A message template such as <c>"User {Id} signed in"</c>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void Log<T0>(ILogger logger, LogLevel level, string message, T0 arg0)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        Emit(logger, level, null!, message, arg0);
    }

    /// <summary>
    /// Logs a templated message with two arguments and no array allocation.
    /// </summary>
    /// <typeparam name="T0">The type of the first template argument.</typeparam>
    /// <typeparam name="T1">The type of the second template argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The severity to log at.</param>
    /// <param name="message">A message template such as <c>"Order {Id} totalled {Amount}"</c>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void Log<T0, T1>(ILogger logger, LogLevel level, string message, T0 arg0, T1 arg1)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        Emit(logger, level, null!, message, arg0, arg1);
    }

    /// <summary>
    /// Logs a templated message with three arguments and no array allocation.
    /// </summary>
    /// <typeparam name="T0">The type of the first template argument.</typeparam>
    /// <typeparam name="T1">The type of the second template argument.</typeparam>
    /// <typeparam name="T2">The type of the third template argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The severity to log at.</param>
    /// <param name="message">A message template with three placeholders.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void Log<T0, T1, T2>(ILogger logger, LogLevel level, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        Emit(logger, level, null!, message, arg0, arg1, arg2);
    }

    /// <summary>
    /// Logs a templated message with four arguments and no array allocation.
    /// </summary>
    /// <typeparam name="T0">The type of the first template argument.</typeparam>
    /// <typeparam name="T1">The type of the second template argument.</typeparam>
    /// <typeparam name="T2">The type of the third template argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth template argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The severity to log at.</param>
    /// <param name="message">A message template with four placeholders.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void Log<T0, T1, T2, T3>(ILogger logger, LogLevel level, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        Emit(logger, level, null!, message, arg0, arg1, arg2, arg3);
    }

    /// <summary>
    /// Logs a templated message with five arguments and no array allocation.
    /// </summary>
    /// <typeparam name="T0">The type of the first template argument.</typeparam>
    /// <typeparam name="T1">The type of the second template argument.</typeparam>
    /// <typeparam name="T2">The type of the third template argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth template argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth template argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The severity to log at.</param>
    /// <param name="message">A message template with five placeholders.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <param name="arg4">The value for the fifth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This typed overload stays at the default priority so a plain 5-argument call binds here
    /// (zero array). Correct binding when a trailing <see cref="Exception"/> is passed -
    /// <c>Log(logger, level, "{A}", someException, b, c, d, e)</c> - is handled by giving the
    /// <see cref="Log(ILogger, LogLevel, string, Exception, object[])"/> overload a higher
    /// <see cref="OverloadResolutionPriorityAttribute"/> instead, so it wins only when an
    /// Exception is actually supplied.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void Log<T0, T1, T2, T3, T4>(ILogger logger, LogLevel level, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        Emit(logger, level, null!, message, arg0, arg1, arg2, arg3, arg4);
    }

    // The typed emit helpers wrap the arguments in a stack-allocated LogState struct and
    // pass a cached static formatter lambda, so nothing is allocated beyond the boxing of
    // value types - and nothing at all once the IsEnabled guard above has returned.
    // CA1873 is suppressed for these helpers in GlobalSuppressions.cs: callers already check
    // IsEnabled, and building the state does no formatting work (ToString runs only if a
    // sink actually reads the entry).
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void Emit<T0>(ILogger logger, LogLevel level, Exception exception, string message, T0 v0)
    {
        TemplateFormatter fmt = GetFormatter(message ?? "N/A");
        logger.Log(level, GetEventId(level), new LogState<T0>(fmt, v0), exception, static (s, _) => s.ToString());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void Emit<T0, T1>(ILogger logger, LogLevel level, Exception exception, string message, T0 v0, T1 v1)
    {
        TemplateFormatter fmt = GetFormatter(message ?? "N/A");
        logger.Log(level, GetEventId(level), new LogState<T0, T1>(fmt, v0, v1), exception, static (s, _) => s.ToString());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void Emit<T0, T1, T2>(ILogger logger, LogLevel level, Exception exception, string message, T0 v0, T1 v1, T2 v2)
    {
        TemplateFormatter fmt = GetFormatter(message ?? "N/A");
        logger.Log(level, GetEventId(level), new LogState<T0, T1, T2>(fmt, v0, v1, v2), exception, static (s, _) => s.ToString());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void Emit<T0, T1, T2, T3>(ILogger logger, LogLevel level, Exception exception, string message, T0 v0, T1 v1, T2 v2, T3 v3)
    {
        TemplateFormatter fmt = GetFormatter(message ?? "N/A");
        logger.Log(level, GetEventId(level), new LogState<T0, T1, T2, T3>(fmt, v0, v1, v2, v3), exception, static (s, _) => s.ToString());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void Emit<T0, T1, T2, T3, T4>(ILogger logger, LogLevel level, Exception exception, string message, T0 v0, T1 v1, T2 v2, T3 v3, T4 v4)
    {
        TemplateFormatter fmt = GetFormatter(message ?? "N/A");
        logger.Log(level, GetEventId(level), new LogState<T0, T1, T2, T3, T4>(fmt, v0, v1, v2, v3, v4), exception, static (s, _) => s.ToString());
    }

    /// <summary>
    /// Logs a message with an associated exception and an arbitrary number of arguments.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The severity to log at.</param>
    /// <param name="message">The message template. A <see langword="null"/> value is logged as <c>"N/A"</c>.</param>
    /// <param name="exception">The exception to attach, or <see langword="null"/> for none.</param>
    /// <param name="args">The template arguments. When empty, the fast no-argument path is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Prefer the strongly-typed overloads for the no-exception case; this overload allocates a
    /// <c>params</c> array and exists for attaching an exception plus an open-ended argument list.
    /// <see cref="OverloadResolutionPriorityAttribute"/>(1) ensures that when a trailing
    /// <see cref="Exception"/> is supplied this overload wins over the typed
    /// <c>Log&lt;T0..Tn&gt;</c> overloads (so the exception is attached, not logged as a value),
    /// while a plain typed call with no exception still binds to the zero-array typed overload.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(1)]
    public static void Log(ILogger logger, LogLevel level, string message, Exception exception, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        message ??= "N/A";
        if (args is null || args.Length == 0)
        {
            logger.Log(level, GetEventId(level), message, exception, static (s, _) => s);
            return;
        }

        // Open-ended path: the template and argument count vary by call, so the compiled
        // LoggerMessage approach (CA1848) and constant-template rule (CA2254) don't apply.
        // This is the deliberate escape hatch documented on this overload; the two rules are
        // suppressed for this member in GlobalSuppressions.cs.
        logger.Log(level, GetEventId(level), exception, message, args);
    }

    /// <summary>
    /// Logs a message with an arbitrary number of arguments and no exception.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The severity to log at.</param>
    /// <param name="message">The message template.</param>
    /// <param name="args">The template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void Log(ILogger logger, LogLevel level, string message, params object[] args)
    {
        Log(logger, level, message, null!, args);
    }

    /// <summary>
    /// Logs an exception together with a message template, with the exception passed first.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The severity to log at.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="messageTemplate">The message template. A <see langword="null"/> value is logged as <c>"N/A"</c>.</param>
    /// <param name="args">The template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void Log(ILogger logger, LogLevel level, Exception exception, string messageTemplate, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(exception);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        messageTemplate ??= "N/A";
        // Same deliberate escape hatch as above: a runtime-varying template that forgoes the
        // compiled LoggerMessage delegates by design (CA1848/CA2254 suppressed in
        // GlobalSuppressions.cs).
        logger.Log(level, GetEventId(level), exception, messageTemplate, args ?? Array.Empty<object>());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void LogNoArgs(ILogger logger, LogLevel level, string message)
    {
        if (!logger.IsEnabled(level))
        {
            return;
        }

        logger.Log(level, GetEventId(level), message ?? "N/A", null, static (s, _) => s);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void LogNoArgs(ILogger logger, LogLevel level, string message, Exception exception)
    {
        if (!logger.IsEnabled(level))
        {
            return;
        }

        logger.Log(level, GetEventId(level), message ?? "N/A", exception, static (s, _) => s);
    }

    #endregion Generic Log Methods

    #region Convenience Methods

    /// <summary>
    /// Logs a <see cref="LogLevel.Trace"/> message.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">The message template. Must not be <see langword="null"/>.</param>
    /// <param name="args">Optional template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteTrace(this ILogger logger, string message, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);

        if (args is null || args.Length == 0)
        { LogNoArgs(logger, LogLevel.Trace, message); return; }
        Log(logger, LogLevel.Trace, message, args);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Debug"/> message.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">The message template. Must not be <see langword="null"/>.</param>
    /// <param name="args">Optional template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteDebug(this ILogger logger, string message, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);

        if (args is null || args.Length == 0)
        { LogNoArgs(logger, LogLevel.Debug, message); return; }
        Log(logger, LogLevel.Debug, message, args);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Information"/> message.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">The message template. Must not be <see langword="null"/>.</param>
    /// <param name="args">Optional template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteInformation(this ILogger logger, string message, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);

        if (args is null || args.Length == 0)
        { LogNoArgs(logger, LogLevel.Information, message); return; }
        Log(logger, LogLevel.Information, message, args);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Warning"/> message.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">The message template. Must not be <see langword="null"/>.</param>
    /// <param name="args">Optional template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteWarning(this ILogger logger, string message, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);

        if (args is null || args.Length == 0)
        { LogNoArgs(logger, LogLevel.Warning, message); return; }
        Log(logger, LogLevel.Warning, message, args);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message with an associated exception.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">The message template. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="args">Optional template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteError(this ILogger logger, string message, Exception exception, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);

        if (args is null || args.Length == 0)
        { LogNoArgs(logger, LogLevel.Error, message, exception); return; }
        Log(logger, LogLevel.Error, message, exception, args);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message with an associated exception and no template arguments.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">The message text. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Prefer this overload over the <c>params</c> variant when there are no template arguments:
    /// it avoids the empty array the compiler otherwise synthesises and wins overload resolution
    /// cleanly over the <c>params</c> form, so the call site is unambiguous.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteError(this ILogger logger, string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        LogNoArgs(logger, LogLevel.Error, message, exception);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message without an exception.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">The message template. Must not be <see langword="null"/>.</param>
    /// <param name="args">Optional template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(-2)]
    public static void WriteError(this ILogger logger, string message, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);

        if (args is null || args.Length == 0)
        { LogNoArgs(logger, LogLevel.Error, message); return; }
        Log(logger, LogLevel.Error, message, args);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message with an associated exception.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">The message template. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="args">Optional template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteCritical(this ILogger logger, string message, Exception exception, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);

        if (args is null || args.Length == 0)
        { LogNoArgs(logger, LogLevel.Critical, message, exception); return; }
        Log(logger, LogLevel.Critical, message, exception, args);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message with an associated exception and no template arguments.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">The message text. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Prefer this overload over the <c>params</c> variant when there are no template arguments:
    /// it avoids the empty array the compiler otherwise synthesises and wins overload resolution
    /// cleanly over the <c>params</c> form, so the call site is unambiguous.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteCritical(this ILogger logger, string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        LogNoArgs(logger, LogLevel.Critical, message, exception);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message without an exception.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">The message template. Must not be <see langword="null"/>.</param>
    /// <param name="args">Optional template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(-2)]
    public static void WriteCritical(this ILogger logger, string message, params object[] args)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);

        if (args is null || args.Length == 0)
        { LogNoArgs(logger, LogLevel.Critical, message); return; }
        Log(logger, LogLevel.Critical, message, args);
    }

    #endregion Convenience Methods

    #region Typed Convenience Overloads (zero array allocation)

    // These strongly-typed overloads win overload resolution over the params versions
    // above (a normal-form match beats an expanded params match), so a call like
    // logger.WriteInformation("User {Id}", 42) binds here automatically: no object[] is
    // allocated, and nothing at all is allocated when the level is disabled. This is the
    // core advantage over the framework's params-based extension methods.

    /// <summary>
    /// Logs a <see cref="LogLevel.Trace"/> message with one strongly-typed argument.
    /// </summary>
    /// <typeparam name="T0">The type of the argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A single-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteTrace<T0>(this ILogger logger, string message, T0 arg0)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Trace))
        { return; }
        Emit(logger, LogLevel.Trace, null!, message, arg0);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Trace"/> message with two strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A two-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteTrace<T0, T1>(this ILogger logger, string message, T0 arg0, T1 arg1)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Trace))
        { return; }
        Emit(logger, LogLevel.Trace, null!, message, arg0, arg1);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Trace"/> message with three strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A three-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteTrace<T0, T1, T2>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Trace))
        { return; }
        Emit(logger, LogLevel.Trace, null!, message, arg0, arg1, arg2);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Trace"/> message with four strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A four-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteTrace<T0, T1, T2, T3>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Trace))
        { return; }
        Emit(logger, LogLevel.Trace, null!, message, arg0, arg1, arg2, arg3);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Trace"/> message with five strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A five-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <param name="arg4">The value for the fifth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteTrace<T0, T1, T2, T3, T4>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Trace))
        { return; }
        Emit(logger, LogLevel.Trace, null!, message, arg0, arg1, arg2, arg3, arg4);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Debug"/> message with one strongly-typed argument.
    /// </summary>
    /// <typeparam name="T0">The type of the argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A single-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteDebug<T0>(this ILogger logger, string message, T0 arg0)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Debug))
        { return; }
        Emit(logger, LogLevel.Debug, null!, message, arg0);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Debug"/> message with two strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A two-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteDebug<T0, T1>(this ILogger logger, string message, T0 arg0, T1 arg1)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Debug))
        { return; }
        Emit(logger, LogLevel.Debug, null!, message, arg0, arg1);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Debug"/> message with three strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A three-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteDebug<T0, T1, T2>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Debug))
        { return; }
        Emit(logger, LogLevel.Debug, null!, message, arg0, arg1, arg2);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Debug"/> message with four strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A four-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteDebug<T0, T1, T2, T3>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Debug))
        { return; }
        Emit(logger, LogLevel.Debug, null!, message, arg0, arg1, arg2, arg3);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Debug"/> message with five strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A five-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <param name="arg4">The value for the fifth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteDebug<T0, T1, T2, T3, T4>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Debug))
        { return; }
        Emit(logger, LogLevel.Debug, null!, message, arg0, arg1, arg2, arg3, arg4);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Information"/> message with one strongly-typed argument.
    /// </summary>
    /// <typeparam name="T0">The type of the argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A single-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteInformation<T0>(this ILogger logger, string message, T0 arg0)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Information))
        { return; }
        Emit(logger, LogLevel.Information, null!, message, arg0);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Information"/> message with two strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A two-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteInformation<T0, T1>(this ILogger logger, string message, T0 arg0, T1 arg1)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Information))
        { return; }
        Emit(logger, LogLevel.Information, null!, message, arg0, arg1);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Information"/> message with three strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A three-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteInformation<T0, T1, T2>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Information))
        { return; }
        Emit(logger, LogLevel.Information, null!, message, arg0, arg1, arg2);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Information"/> message with four strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A four-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteInformation<T0, T1, T2, T3>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Information))
        { return; }
        Emit(logger, LogLevel.Information, null!, message, arg0, arg1, arg2, arg3);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Information"/> message with five strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A five-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <param name="arg4">The value for the fifth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteInformation<T0, T1, T2, T3, T4>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Information))
        { return; }
        Emit(logger, LogLevel.Information, null!, message, arg0, arg1, arg2, arg3, arg4);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Warning"/> message with one strongly-typed argument.
    /// </summary>
    /// <typeparam name="T0">The type of the argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A single-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteWarning<T0>(this ILogger logger, string message, T0 arg0)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Warning))
        { return; }
        Emit(logger, LogLevel.Warning, null!, message, arg0);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Warning"/> message with two strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A two-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteWarning<T0, T1>(this ILogger logger, string message, T0 arg0, T1 arg1)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Warning))
        { return; }
        Emit(logger, LogLevel.Warning, null!, message, arg0, arg1);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Warning"/> message with three strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A three-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteWarning<T0, T1, T2>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Warning))
        { return; }
        Emit(logger, LogLevel.Warning, null!, message, arg0, arg1, arg2);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Warning"/> message with four strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A four-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteWarning<T0, T1, T2, T3>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Warning))
        { return; }
        Emit(logger, LogLevel.Warning, null!, message, arg0, arg1, arg2, arg3);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Warning"/> message with five strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A five-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <param name="arg4">The value for the fifth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteWarning<T0, T1, T2, T3, T4>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Warning))
        { return; }
        Emit(logger, LogLevel.Warning, null!, message, arg0, arg1, arg2, arg3, arg4);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message with an exception and one strongly-typed argument.
    /// </summary>
    /// <typeparam name="T0">The type of the argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A single-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteError<T0>(this ILogger logger, string message, Exception exception, T0 arg0)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        if (!logger.IsEnabled(LogLevel.Error))
        { return; }
        Emit(logger, LogLevel.Error, exception, message, arg0);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message with an exception and two strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A two-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteError<T0, T1>(this ILogger logger, string message, Exception exception, T0 arg0, T1 arg1)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        if (!logger.IsEnabled(LogLevel.Error))
        { return; }
        Emit(logger, LogLevel.Error, exception, message, arg0, arg1);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message with an exception and three strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A three-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteError<T0, T1, T2>(this ILogger logger, string message, Exception exception, T0 arg0, T1 arg1, T2 arg2)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        if (!logger.IsEnabled(LogLevel.Error))
        { return; }
        Emit(logger, LogLevel.Error, exception, message, arg0, arg1, arg2);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message with an exception and four strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A four-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteError<T0, T1, T2, T3>(this ILogger logger, string message, Exception exception, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        if (!logger.IsEnabled(LogLevel.Error))
        { return; }
        Emit(logger, LogLevel.Error, exception, message, arg0, arg1, arg2, arg3);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message with an exception and five strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A five-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <param name="arg4">The value for the fifth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteError<T0, T1, T2, T3, T4>(this ILogger logger, string message, Exception exception, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        if (!logger.IsEnabled(LogLevel.Error))
        { return; }
        Emit(logger, LogLevel.Error, exception, message, arg0, arg1, arg2, arg3, arg4);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message with an exception and one strongly-typed argument.
    /// </summary>
    /// <typeparam name="T0">The type of the argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A single-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteCritical<T0>(this ILogger logger, string message, Exception exception, T0 arg0)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        if (!logger.IsEnabled(LogLevel.Critical))
        { return; }
        Emit(logger, LogLevel.Critical, exception, message, arg0);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message with an exception and two strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A two-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteCritical<T0, T1>(this ILogger logger, string message, Exception exception, T0 arg0, T1 arg1)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        if (!logger.IsEnabled(LogLevel.Critical))
        { return; }
        Emit(logger, LogLevel.Critical, exception, message, arg0, arg1);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message with an exception and three strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A three-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteCritical<T0, T1, T2>(this ILogger logger, string message, Exception exception, T0 arg0, T1 arg1, T2 arg2)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        if (!logger.IsEnabled(LogLevel.Critical))
        { return; }
        Emit(logger, LogLevel.Critical, exception, message, arg0, arg1, arg2);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message with an exception and four strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A four-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteCritical<T0, T1, T2, T3>(this ILogger logger, string message, Exception exception, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        if (!logger.IsEnabled(LogLevel.Critical))
        { return; }
        Emit(logger, LogLevel.Critical, exception, message, arg0, arg1, arg2, arg3);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message with an exception and five strongly-typed arguments.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A five-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="exception">The exception to attach. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <param name="arg4">The value for the fifth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/>, <paramref name="message"/>, or <paramref name="exception"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void WriteCritical<T0, T1, T2, T3, T4>(this ILogger logger, string message, Exception exception, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(exception);
        if (!logger.IsEnabled(LogLevel.Critical))
        { return; }
        Emit(logger, LogLevel.Critical, exception, message, arg0, arg1, arg2, arg3, arg4);
    }

    // -------------------------------------------------------------------------
    // WriteError / WriteCritical — typed, no exception
    //
    // [OverloadResolutionPriority(-1)] keeps these below the existing
    // WriteError(message, Exception) / WriteError(message, Exception, T0, ...)
    // overloads (priority 0), so passing an Exception as the second argument
    // still routes to the with-exception path.  The params fallbacks above are
    // at priority -2, so these typed variants beat them for 1–3 value args.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message with one strongly-typed argument and no exception.
    /// </summary>
    /// <typeparam name="T0">The type of the argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A single-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(-1)]
    public static void WriteError<T0>(this ILogger logger, string message, T0 arg0)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Error))
        { return; }
        Emit(logger, LogLevel.Error, null!, message, arg0);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message with two strongly-typed arguments and no exception.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A two-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(-1)]
    public static void WriteError<T0, T1>(this ILogger logger, string message, T0 arg0, T1 arg1)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Error))
        { return; }
        Emit(logger, LogLevel.Error, null!, message, arg0, arg1);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message with three strongly-typed arguments and no exception.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A three-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(-1)]
    public static void WriteError<T0, T1, T2>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Error))
        { return; }
        Emit(logger, LogLevel.Error, null!, message, arg0, arg1, arg2);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message with four strongly-typed arguments and no exception.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A four-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(-1)]
    public static void WriteError<T0, T1, T2, T3>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Error))
        { return; }
        Emit(logger, LogLevel.Error, null!, message, arg0, arg1, arg2, arg3);
    }

    /// <summary>
    /// Logs an <see cref="LogLevel.Error"/> message with five strongly-typed arguments and no exception.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A five-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <param name="arg4">The value for the fifth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(-1)]
    public static void WriteError<T0, T1, T2, T3, T4>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Error))
        { return; }
        Emit(logger, LogLevel.Error, null!, message, arg0, arg1, arg2, arg3, arg4);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message with one strongly-typed argument and no exception.
    /// </summary>
    /// <typeparam name="T0">The type of the argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A single-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(-1)]
    public static void WriteCritical<T0>(this ILogger logger, string message, T0 arg0)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Critical))
        { return; }
        Emit(logger, LogLevel.Critical, null!, message, arg0);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message with two strongly-typed arguments and no exception.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A two-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(-1)]
    public static void WriteCritical<T0, T1>(this ILogger logger, string message, T0 arg0, T1 arg1)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Critical))
        { return; }
        Emit(logger, LogLevel.Critical, null!, message, arg0, arg1);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message with three strongly-typed arguments and no exception.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A three-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(-1)]
    public static void WriteCritical<T0, T1, T2>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Critical))
        { return; }
        Emit(logger, LogLevel.Critical, null!, message, arg0, arg1, arg2);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message with four strongly-typed arguments and no exception.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A four-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(-1)]
    public static void WriteCritical<T0, T1, T2, T3>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Critical))
        { return; }
        Emit(logger, LogLevel.Critical, null!, message, arg0, arg1, arg2, arg3);
    }

    /// <summary>
    /// Logs a <see cref="LogLevel.Critical"/> message with five strongly-typed arguments and no exception.
    /// </summary>
    /// <typeparam name="T0">The type of the first argument.</typeparam>
    /// <typeparam name="T1">The type of the second argument.</typeparam>
    /// <typeparam name="T2">The type of the third argument.</typeparam>
    /// <typeparam name="T3">The type of the fourth argument.</typeparam>
    /// <typeparam name="T4">The type of the fifth argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">A five-placeholder message template. Must not be <see langword="null"/>.</param>
    /// <param name="arg0">The value for the first placeholder.</param>
    /// <param name="arg1">The value for the second placeholder.</param>
    /// <param name="arg2">The value for the third placeholder.</param>
    /// <param name="arg3">The value for the fourth placeholder.</param>
    /// <param name="arg4">The value for the fifth placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    [OverloadResolutionPriority(-1)]
    public static void WriteCritical<T0, T1, T2, T3, T4>(this ILogger logger, string message, T0 arg0, T1 arg1, T2 arg2, T3 arg3, T4 arg4)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(message);
        if (!logger.IsEnabled(LogLevel.Critical))
        { return; }
        Emit(logger, LogLevel.Critical, null!, message, arg0, arg1, arg2, arg3, arg4);
    }

    #endregion Typed Convenience Overloads (zero array allocation)

    #region Exception Logging

    /// <summary>
    /// Logs an exception at <see cref="LogLevel.Error"/>, rendered through <see cref="ExceptionFormatter"/>.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception to log. Must not be <see langword="null"/>.</param>
    /// <param name="title">A short heading placed at the top of the formatted message.</param>
    /// <param name="moreDetailsEnabled">When <see langword="true"/>, includes the stack trace and any inner exceptions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="ex"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WriteErrorException(this ILogger logger, Exception ex, string title = "System Error", bool moreDetailsEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(ex);

        if (!logger.IsEnabled(LogLevel.Error))
        {
            return;
        }

        string msg = _exceptionFormatter(ex, title, moreDetailsEnabled);
        LogNoArgs(logger, LogLevel.Error, msg, ex);
    }

    /// <summary>
    /// Logs an exception at <see cref="LogLevel.Critical"/>, rendered through <see cref="ExceptionFormatter"/>.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="ex">The exception to log. Must not be <see langword="null"/>.</param>
    /// <param name="title">A short heading placed at the top of the formatted message.</param>
    /// <param name="moreDetailsEnabled">When <see langword="true"/>, includes the stack trace and any inner exceptions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="ex"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void WriteCriticalException(this ILogger logger, Exception ex, string title = "Critical System Error", bool moreDetailsEnabled = false)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(ex);

        if (!logger.IsEnabled(LogLevel.Critical))
        {
            return;
        }

        string msg = _exceptionFormatter(ex, title, moreDetailsEnabled);
        LogNoArgs(logger, LogLevel.Critical, msg, ex);
    }

    // The default exception renderer. It produces a fixed, aligned block of fields and,
    // when asked, appends the stack trace and a bounded walk of inner exceptions. The
    // StringBuilder comes from the pool so repeated calls don't churn the heap.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string FormatExceptionMessageInternal(Exception ex, string title, bool moreDetailsEnabled)
    {
        StringBuilder sb = _sbPool.Get();
        try
        {
            _ = sb.Clear();
            _ = sb.EnsureCapacity(1024);

            Type exType = ex.GetType();
            _ = sb.Append("Timestamp      : ").AppendLine(GetCachedUtc())
                .Append("Title          : ").AppendLine(title ?? "N/A")
                .Append("Exception Type : ").AppendLine(exType.FullName ?? exType.Name)
                .Append("Message        : ").AppendLine(ex.Message?.Trim() ?? "N/A")
                .Append("HResult        : ").Append(ex.HResult).AppendLine()
                .Append("Source         : ").AppendLine(ex.Source ?? "N/A");
            // Note: Exception.TargetSite is deliberately not reported - it is annotated
            // [RequiresUnreferencedCode] (trim/AOT-unsafe) and is redundant with the stack
            // trace below. Omitting it keeps Nilog fully Native-AOT and trimming compatible.

            if (moreDetailsEnabled)
            {
                string? st = ex.StackTrace;
                if (!string.IsNullOrWhiteSpace(st))
                {
                    _ = sb.AppendLine().AppendLine("Stack Trace    :").AppendLine(st.Trim());
                }

                if (ex.InnerException is not null)
                {
                    _ = sb.AppendLine().AppendLine("---- Inner Exceptions ----");
                    AppendInnerExceptionDetails(sb, ex.InnerException, 1, maxDepth: 3);
                }
            }

            return sb.ToString();
        }
        finally
        {
            _sbPool.Return(sb);
        }
    }

    // Pre-built indent strings so AppendInnerExceptionDetails never allocates a new
    // string for each recursion level. Depths beyond the array length fall back to
    // new string('>', depth), which is the slow path and not reached with maxDepth ≤ 5.
    private static readonly string[] _indents = [">", ">>", ">>>", ">>>>", ">>>>>"];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetIndent(int depth) =>
        (uint)(depth - 1) < (uint)_indents.Length ? _indents[depth - 1] : new string('>', depth);

    // Walks the inner-exception chain (and AggregateException branches) up to maxDepth,
    // indenting each level with '>' so the nesting is readable in plain-text sinks.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AppendInnerExceptionDetails(StringBuilder sb, Exception inner, int depth, int maxDepth = 5)
    {
        if (inner is null || depth > maxDepth)
        {
            return;
        }

        string indent = GetIndent(depth);
        Type innerType = inner.GetType();
        _ = sb.Append(indent).Append(" Exception Type : ").AppendLine(innerType.FullName ?? innerType.Name)
            .Append(indent).Append(" Message        : ").AppendLine(inner.Message?.Trim() ?? "N/A")
            .Append(indent).Append(" HResult        : ").Append(inner.HResult).AppendLine()
            .Append(indent).Append(" Source         : ").AppendLine(inner.Source ?? "N/A");
        // TargetSite omitted for trim/AOT safety (see FormatExceptionMessageInternal).

        string? st = inner.StackTrace;
        if (!string.IsNullOrWhiteSpace(st))
        {
            _ = sb.Append(indent).AppendLine(" Stack Trace    :").AppendLine(st.Trim());
        }

        // AggregateException can fan out into several inner exceptions; follow each one.
        if (inner is AggregateException agg && agg.InnerExceptions.Count > 0)
        {
            for (int i = 0; i < agg.InnerExceptions.Count; i++)
            {
                _ = sb.AppendLine();
                AppendInnerExceptionDetails(sb, agg.InnerExceptions[i], depth + 1, maxDepth);
            }
        }
        else if (inner.InnerException is not null)
        {
            _ = sb.AppendLine();
            AppendInnerExceptionDetails(sb, inner.InnerException, depth + 1, maxDepth);
        }
    }

    #endregion Exception Logging

    #region Log Scope Helper

    /// <summary>
    /// Begins a logging scope carrying a single key/value pair.
    /// </summary>
    /// <param name="logger">The logger to open the scope on.</param>
    /// <param name="key">The property name. Must not be <see langword="null"/> or whitespace.</param>
    /// <param name="value">The property value. A <see langword="null"/> value is recorded as <c>"N/A"</c>.</param>
    /// <returns>An <see cref="IDisposable"/> that ends the scope when disposed; use it with <c>using</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="key"/> is <see langword="null"/> or whitespace.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static IDisposable WriteScope(this ILogger logger, string key, object value)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Key cannot be null or whitespace.", nameof(key));
        }

        SingleScope scope = new(key, value);
        return logger.BeginScope(scope) ?? new DisposableScope(scope);
    }

    /// <summary>
    /// Begins a logging scope carrying several key/value pairs.
    /// </summary>
    /// <param name="logger">The logger to open the scope on.</param>
    /// <param name="context">The properties to attach. <see langword="null"/> or empty returns a no-op scope.</param>
    /// <returns>An <see cref="IDisposable"/> that ends the scope when disposed; use it with <c>using</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Small contexts (four entries or fewer) are stored in a pre-sized array to avoid
    /// the overhead of a list; larger ones fall back to a list. Either way the values are
    /// copied, so later mutation of the supplied dictionary does not affect the scope.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static IDisposable WriteScope(this ILogger logger, IDictionary<string, object>? context)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (context is null || context.Count == 0)
        {
            return NullScope.Instance;
        }

        if (context.Count <= 4)
        {
            KeyValuePair<string, object>[] items = new KeyValuePair<string, object>[context.Count];
            int i = 0;
            foreach (KeyValuePair<string, object> kv in context)
            {
                items[i++] = new KeyValuePair<string, object>(kv.Key, kv.Value ?? "N/A");
            }

            SmallScopeWrapper wrapper = new(items);
            return logger.BeginScope(wrapper) ?? new DisposableScope(wrapper);
        }
        else
        {
            List<KeyValuePair<string, object>> safe = new(context.Count);
            foreach (KeyValuePair<string, object> kv in context)
            {
                safe.Add(new KeyValuePair<string, object>(kv.Key, kv.Value ?? "N/A"));
            }

            ScopeWrapper wrapper = new(safe);
            return logger.BeginScope(wrapper) ?? new DisposableScope(wrapper);
        }
    }

    /// <summary>
    /// Begins a logging scope from any key/value sequence, including
    /// <see cref="IReadOnlyDictionary{TKey, TValue}"/> and immutable dictionary types that
    /// do not implement <see cref="IDictionary{TKey, TValue}"/>.
    /// </summary>
    /// <param name="logger">The logger to open the scope on.</param>
    /// <param name="context">The key/value pairs to attach. <see langword="null"/> or empty returns a no-op scope.</param>
    /// <returns>An <see cref="IDisposable"/> that ends the scope when disposed; use it with <c>using</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// <para>
    /// C# overload resolution prefers <see cref="WriteScope(ILogger, IDictionary{string, object})"/> for
    /// <see cref="Dictionary{TKey, TValue}"/> arguments (a more-derived interface wins). This overload
    /// is chosen automatically when the variable is typed as <see cref="IReadOnlyDictionary{TKey, TValue}"/>
    /// or any other <see cref="IEnumerable{T}"/> of key/value pairs — no cast required.
    /// </para>
    /// <para>
    /// When the sequence implements <see cref="IReadOnlyCollection{T}"/> or <see cref="ICollection{T}"/>,
    /// the count is read up front so that small sequences (four entries or fewer) use the same
    /// allocation-light pre-sized array path as the dictionary overload. Values are always copied.
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static IDisposable WriteScope(this ILogger logger, IEnumerable<KeyValuePair<string, object>>? context)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (context is null)
        {
            return NullScope.Instance;
        }

        int knownCount = context switch
        {
            IReadOnlyCollection<KeyValuePair<string, object>> rc => rc.Count,
            ICollection<KeyValuePair<string, object>> c => c.Count,
            _ => -1
        };

        if (knownCount == 0)
        {
            return NullScope.Instance;
        }

        // When count is known and small, allocate exactly the right array up front —
        // same allocation profile as the IDictionary overload.
        if (knownCount is > 0 and <= 4)
        {
            KeyValuePair<string, object>[] items = new KeyValuePair<string, object>[knownCount];
            int i = 0;
            foreach (KeyValuePair<string, object> kv in context)
            {
                items[i++] = new KeyValuePair<string, object>(kv.Key, kv.Value ?? "N/A");
            }

            SmallScopeWrapper wrapper = new(items);
            return logger.BeginScope(wrapper) ?? new DisposableScope(wrapper);
        }
        else
        {
            // Count unknown or > 4: enumerate once into a list, then pick the wrapper.
            List<KeyValuePair<string, object>> safe = knownCount > 0 ? new(knownCount) : [];
            foreach (KeyValuePair<string, object> kv in context)
            {
                safe.Add(new KeyValuePair<string, object>(kv.Key, kv.Value ?? "N/A"));
            }

            if (safe.Count == 0)
            {
                return NullScope.Instance;
            }

            if (safe.Count <= 4)
            {
                SmallScopeWrapper wrapper = new([.. safe]);
                return logger.BeginScope(wrapper) ?? new DisposableScope(wrapper);
            }

            ScopeWrapper wrapper2 = new(safe);
            return logger.BeginScope(wrapper2) ?? new DisposableScope(wrapper2);
        }
    }

    // Single-pair scope state. A readonly struct with a hand-written enumerator so that
    // BeginScope can read the pair without allocating an iterator on the heap.
    private readonly struct SingleScope : IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly KeyValuePair<string, object> _pair;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public SingleScope(string key, object value)
        {
            _pair = new KeyValuePair<string, object>(key, value ?? "N/A");
        }

        public int Count => 1;

        public KeyValuePair<string, object> this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            get => index == 0
                ? _pair
                : throw new ArgumentOutOfRangeException(nameof(index));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public Enumerator GetEnumerator()
        {
            return new(_pair);
        }

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public override string ToString()
        {
            return $"{_pair.Key}={_pair.Value}";
        }

        public struct Enumerator : IEnumerator<KeyValuePair<string, object>>
        {
            private bool _moved;

            public Enumerator(KeyValuePair<string, object> value)
            {
                (Current, _moved) = (value, false);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public bool MoveNext()
            {
                if (_moved)
                {
                    return false;
                }

                _moved = true;
                return true;
            }

            public KeyValuePair<string, object> Current { get; }
            readonly object IEnumerator.Current => Current;

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public void Reset()
            {
                _moved = false;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public readonly void Dispose()
            { }
        }
    }

    // Backing state for larger scopes (more than four entries). The values were already
    // copied into the list by the caller, so this just exposes them and renders a
    // "key=value key=value" string for plain-text sinks.
    private sealed class ScopeWrapper : IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly List<KeyValuePair<string, object>> _items;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public ScopeWrapper(List<KeyValuePair<string, object>> items)
        {
            _items = items;
        }

        public int Count => _items.Count;
        public KeyValuePair<string, object> this[int index] => _items[index];

        // Return the concrete List<T>.Enumerator struct so that duck-typed foreach
        // on a ScopeWrapper is allocation-free. The explicit interface implementations
        // still box, but direct foreach avoids that cost.
        public List<KeyValuePair<string, object>>.Enumerator GetEnumerator()
            => _items.GetEnumerator();

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
            => _items.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => _items.GetEnumerator();

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public override string ToString()
        {
            if (_items.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = _sbPool.Get();
            try
            {
                _ = sb.Clear();
                for (int i = 0; i < _items.Count; i++)
                {
                    if (i > 0) _ = sb.Append(' ');
                    KeyValuePair<string, object> kv = _items[i];
                    _ = sb.Append(kv.Key).Append('=').Append(kv.Value);
                }
                return sb.ToString();
            }
            finally
            {
                _sbPool.Return(sb);
            }
        }
    }

    // Backing state for small scopes (up to four entries), kept in a plain array with a
    // struct enumerator so iteration stays allocation-free.
    private sealed class SmallScopeWrapper : IReadOnlyList<KeyValuePair<string, object>>
    {
        private readonly KeyValuePair<string, object>[] _items;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public SmallScopeWrapper(KeyValuePair<string, object>[] items)
        {
            _items = items;
        }

        public int Count => _items.Length;
        public KeyValuePair<string, object> this[int index] => _items[index];

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public Enumerator GetEnumerator()
        {
            return new(_items);
        }

        IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public struct Enumerator : IEnumerator<KeyValuePair<string, object>>
        {
            private readonly KeyValuePair<string, object>[] _arr;
            private int _index;

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public Enumerator(KeyValuePair<string, object>[] arr)
            {
                (_arr, _index) = (arr, -1);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public bool MoveNext()
            {
                return ++_index < _arr.Length;
            }

            public readonly KeyValuePair<string, object> Current => _arr[_index];
            readonly object IEnumerator.Current => _arr[_index];

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public void Reset()
            {
                _index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
            public readonly void Dispose()
            { }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public override string ToString()
        {
            if (_items.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = _sbPool.Get();
            try
            {
                _ = sb.Clear();
                for (int i = 0; i < _items.Length; i++)
                {
                    if (i > 0) _ = sb.Append(' ');
                    KeyValuePair<string, object> kv = _items[i];
                    _ = sb.Append(kv.Key).Append('=').Append(kv.Value);
                }
                return sb.ToString();
            }
            finally
            {
                _sbPool.Return(sb);
            }
        }
    }

    // Fallback returned when the underlying logger's BeginScope hands back null. It keeps
    // the scope state alive for its ToString while being a safe no-op to dispose.
    private sealed class DisposableScope : IDisposable
    {
        private readonly object _state;

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public DisposableScope(object state)
        {
            _state = state;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public void Dispose()
        { }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public override string ToString()
        {
            return _state.ToString() ?? string.Empty;
        }
    }

    #endregion Log Scope Helper

    #region Extensibility Hooks (Async/Batch Ready)

    private static volatile Func<LogLevel, string, Exception, bool> _asyncSinkFilter = static (_, _, _) => true;

    /// <summary>
    /// Gets a predicate that decides whether an entry should reach an asynchronous sink.
    /// </summary>
    /// <value>
    /// A delegate taking the level, the rendered message, and the exception, returning
    /// <see langword="true"/> to keep the entry. The default keeps everything. Replace it
    /// through <see cref="UseAsyncSinkProvider"/>.
    /// </value>
    /// <remarks>
    /// This is a forward-looking hook for callers that wire Nilog into a custom async or
    /// batching pipeline; the core logging methods do not consult it.
    /// </remarks>
    public static Func<LogLevel, string, Exception, bool> AsyncSinkFilter
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        get => _asyncSinkFilter;
    }

    /// <summary>
    /// Replaces the <see cref="AsyncSinkFilter"/> predicate.
    /// </summary>
    /// <param name="filter">The new filter. A <see langword="null"/> value leaves the current filter unchanged.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void UseAsyncSinkProvider(Func<LogLevel, string, Exception, bool> filter)
    {
        _asyncSinkFilter = filter ?? _asyncSinkFilter;
    }

    // Flush callbacks registered by buffering/batching sinks (e.g. file, Seq, OpenTelemetry,
    // Application Insights) so FlushAsync can actually drain them. Stored in a copy-on-write
    // array: registration is rare, FlushAsync reads a single volatile reference, and when
    // nothing is registered FlushAsync stays a zero-allocation no-op (backward compatible).
    private static volatile Func<CancellationToken, Task>[] _flushCallbacks = Array.Empty<Func<CancellationToken, Task>>();
    private static readonly object _flushLock = new();

    /// <summary>
    /// Registers an asynchronous flush callback to be awaited by <see cref="FlushAsync"/>.
    /// </summary>
    /// <param name="flush">
    /// A delegate that drains a buffering/batching sink. It is invoked once per
    /// <see cref="FlushAsync"/> call, in registration order, and is passed the caller's
    /// cancellation token.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="flush"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// This is how Nilog supports a real flush without owning a sink: a sink (or the host's
    /// shutdown code) registers how to drain its own buffers, and <see cref="FlushAsync"/>
    /// awaits them all. If nothing is registered, <see cref="FlushAsync"/> remains a no-op.
    /// </remarks>
    public static void RegisterFlush(Func<CancellationToken, Task> flush)
    {
        ArgumentNullException.ThrowIfNull(flush);
        lock (_flushLock)
        {
            Func<CancellationToken, Task>[] current = _flushCallbacks;
            var updated = new Func<CancellationToken, Task>[current.Length + 1];
            Array.Copy(current, updated, current.Length);
            updated[current.Length] = flush;
            _flushCallbacks = updated;
        }
    }

    /// <summary>
    /// Removes a flush callback previously registered with <see cref="RegisterFlush"/>.
    /// </summary>
    /// <param name="flush">The delegate to remove.</param>
    /// <returns><see langword="true"/> if a matching callback was found and removed; otherwise <see langword="false"/>.</returns>
    public static bool UnregisterFlush(Func<CancellationToken, Task> flush)
    {
        if (flush is null)
        {
            return false;
        }

        lock (_flushLock)
        {
            Func<CancellationToken, Task>[] current = _flushCallbacks;
            int index = Array.IndexOf(current, flush);
            if (index < 0)
            {
                return false;
            }

            if (current.Length == 1)
            {
                _flushCallbacks = Array.Empty<Func<CancellationToken, Task>>();
                return true;
            }

            var updated = new Func<CancellationToken, Task>[current.Length - 1];
            Array.Copy(current, 0, updated, 0, index);
            Array.Copy(current, index + 1, updated, index, current.Length - index - 1);
            _flushCallbacks = updated;
            return true;
        }
    }

    /// <summary>
    /// Flushes all registered sink flush callbacks (see <see cref="RegisterFlush"/>).
    /// </summary>
    /// <param name="cancellationToken">Observed between callbacks and passed to each one.</param>
    /// <returns>
    /// A task that completes when every registered callback has completed. When no callback is
    /// registered this returns <see cref="Task.CompletedTask"/> synchronously with zero allocation,
    /// preserving the original no-op behaviour.
    /// </returns>
    /// <remarks>
    /// Every callback is attempted even if an earlier one faults; any exceptions are surfaced
    /// together as an <see cref="AggregateException"/> so a single bad sink never silently
    /// swallows the rest of the flush.
    /// </remarks>
    public static Task FlushAsync(CancellationToken cancellationToken = default)
    {
        Func<CancellationToken, Task>[] callbacks = _flushCallbacks;
        return callbacks.Length == 0 ? Task.CompletedTask : FlushAllAsync(callbacks, cancellationToken);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task FlushAllAsync(Func<CancellationToken, Task>[] callbacks, CancellationToken cancellationToken)
    {
        List<Exception>? errors = null;
        for (int i = 0; i < callbacks.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Task task = callbacks[i](cancellationToken);
                if (task is not null)
                {
                    await task.ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                (errors ??= []).Add(ex);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("One or more Nilog flush callbacks failed.", errors);
        }
    }

    #endregion Extensibility Hooks (Async/Batch Ready)
}
