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
public static class Nilogger
{
    private const string _message = "{Message}";

    // Best-effort cleanup: stop the timestamp cache timer when the process or AppDomain
    // is going away so we don't leave a stray timer running during shutdown.
    static Nilogger()
    {
        AppDomain.CurrentDomain.ProcessExit += static (_, __) => SafeShutdown();
        AppDomain.CurrentDomain.DomainUnload += static (_, __) => SafeShutdown();
    }

    #region Cached EventIds (single source of truth)

    // One EventId per level, created once and reused everywhere. Both the pre-compiled
    // delegates below and GetEventId read from these fields, so a given level always
    // emits the same EventId no matter which overload produced the entry.
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

    #region Predefined Delegates for Performance

    // For the no-argument path we lean on LoggerMessage.Define, which hands back a
    // delegate that has already done the template parsing up front. One delegate per
    // level is built once at startup and then reused for the lifetime of the process.
    private static readonly Action<ILogger, string, Exception> _trace =
        LoggerMessage.Define<string>(LogLevel.Trace, _traceId, _message);

    private static readonly Action<ILogger, string, Exception> _debug =
        LoggerMessage.Define<string>(LogLevel.Debug, _debugId, _message);

    private static readonly Action<ILogger, string, Exception> _info =
        LoggerMessage.Define<string>(LogLevel.Information, _infoId, _message);

    private static readonly Action<ILogger, string, Exception> _warn =
        LoggerMessage.Define<string>(LogLevel.Warning, _warnId, _message);

    private static readonly Action<ILogger, string, Exception> _error =
        LoggerMessage.Define<string>(LogLevel.Error, _errorId, _message);

    private static readonly Action<ILogger, string, Exception> _critical =
        LoggerMessage.Define<string>(LogLevel.Critical, _criticalId, _message);

    // Used for any level outside the known range so Resolve can stay branch-light.
    private static readonly Action<ILogger, string, Exception> _noop = static (_, __, ___) => { };

    // Indexed by (int)LogLevel for an O(1) lookup; the trailing _noop covers LogLevel.None.
    private static readonly Action<ILogger, string, Exception>[] _byLevel =
    [
        _trace,
        _debug,
        _info,
        _warn,
        _error,
        _critical,
        _noop
    ];

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static Action<ILogger, string, Exception> Resolve(LogLevel level)
    {
        int idx = (int)level;
        return (uint)idx < (uint)_byLevel.Length ? _byLevel[idx] : _info;
    }

    #endregion Predefined Delegates for Performance

    #region Timestamp Cache (Allocation-Free UTC)

    // Formatting DateTime.UtcNow on every exception would allocate a string each time.
    // Instead a background timer refreshes a single cached ISO-8601 string roughly once
    // a millisecond, and callers just read the volatile field. The thread-local char
    // buffer lets TryFormat write without allocating an intermediate array.
    private static readonly ThreadLocal<char[]> _utcBuffer = new(() => new char[33]);
    private static readonly Timer _utcCacheTimer = new(UpdateUtc, null, 0, 1);
    private static volatile string _cachedUtc = FormatUtc();

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void UpdateUtc(object? _)
    {
        _ = Interlocked.Exchange(ref _cachedUtc, FormatUtc());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static string FormatUtc()
    {
        char[] buf = _utcBuffer?.Value ?? new char[33];
        _ = DateTime.UtcNow.TryFormat(buf, out int len, "O", CultureInfo.InvariantCulture);
        return new string(buf, 0, len);
    }

    /// <summary>
    /// Stops the background timer that refreshes the cached UTC timestamp.
    /// </summary>
    /// <remarks>
    /// This is called automatically on process exit, so you rarely need it. Call it
    /// yourself only when you want deterministic teardown - for example in a unit test
    /// or a short-lived host that creates and destroys app domains. It is safe to call
    /// more than once.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ShutdownUtcTimer()
    {
        try
        {
            _ = Interlocked.Exchange(ref _cachedUtc, FormatUtc());
            _ = _utcCacheTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _utcCacheTimer.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by an earlier shutdown hook - nothing left to do.
        }
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

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static TemplateFormatter GetFormatter(string template)
    {
        return _templateCache.GetOrAdd(template, static t => new TemplateFormatter(t));
    }

    // Parses a message template once and remembers two things: the property names (so
    // structured sinks get {Key, Value} pairs) and a positional format string like
    // "User {0} did {1}" that string.Format can consume directly.
    private sealed class TemplateFormatter
    {
        private readonly string _format;

        public string Template { get; }
        public string[] Names { get; }

        public TemplateFormatter(string template)
        {
            Template = template ?? string.Empty;
            List<string> names = new(4);
            StringBuilder sb = new(Template.Length + 8);
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
                        pos += 2;
                        continue;
                    }

                    // An unterminated '{' is treated as literal text and ends parsing.
                    int close = Template.IndexOf('}', pos + 1);
                    if (close < 0)
                    {
                        _ = sb.Append(Template, pos, len - pos);
                        break;
                    }

                    // Split the placeholder into its name and any ",align"/":format"
                    // suffix, then re-emit it as a positional slot ({0}, {1}, ...).
                    string token = Template.Substring(pos + 1, close - pos - 1);
                    int suffixStart = token.IndexOfAny(_suffixChars);
                    string name = suffixStart < 0 ? token : token[..suffixStart];
                    string suffix = suffixStart < 0 ? string.Empty : token[suffixStart..];

                    _ = sb.Append('{').Append(names.Count).Append(suffix).Append('}');
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
                        pos += 2;
                        continue;
                    }

                    _ = sb.Append("}}");
                    pos++;
                }
                else
                {
                    _ = sb.Append(c);
                    pos++;
                }
            }

            _format = sb.ToString();
            Names = [.. names];
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
        public string Format(object a0)
        {
            try
            { return Names.Length == 0 ? _format : string.Format(CultureInfo.InvariantCulture, _format, a0); }
            catch (FormatException) { return Template; }
        }

        public string Format(object a0, object a1)
        {
            try
            { return Names.Length == 0 ? _format : string.Format(CultureInfo.InvariantCulture, _format, a0, a1); }
            catch (FormatException) { return Template; }
        }

        public string Format(object a0, object a1, object a2)
        {
            try
            { return Names.Length == 0 ? _format : string.Format(CultureInfo.InvariantCulture, _format, a0, a1, a2); }
            catch (FormatException) { return Template; }
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

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            yield return this[0];
            yield return this[1];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return _fmt.Format(_v0!);
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

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            yield return this[0];
            yield return this[1];
            yield return this[2];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return _fmt.Format(_v0!, _v1!);
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

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            yield return this[0];
            yield return this[1];
            yield return this[2];
            yield return this[3];
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public override string ToString()
        {
            return _fmt.Format(_v0!, _v1!, _v2!);
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

        Resolve(level)(logger, message ?? "N/A", null!);
    }

    /// <summary>
    /// Logs a templated message with one argument and no array allocation.
    /// </summary>
    /// <typeparam name="T1">The type of the template argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The severity to log at.</param>
    /// <param name="message">A message template such as <c>"User {Id} signed in"</c>.</param>
    /// <param name="arg1">The value for the first placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void Log<T1>(ILogger logger, LogLevel level, string message, T1 arg1)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        Emit(logger, level, null!, message, arg1);
    }

    /// <summary>
    /// Logs a templated message with two arguments and no array allocation.
    /// </summary>
    /// <typeparam name="T1">The type of the first template argument.</typeparam>
    /// <typeparam name="T2">The type of the second template argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The severity to log at.</param>
    /// <param name="message">A message template such as <c>"Order {Id} totalled {Amount}"</c>.</param>
    /// <param name="arg1">The value for the first placeholder.</param>
    /// <param name="arg2">The value for the second placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void Log<T1, T2>(ILogger logger, LogLevel level, string message, T1 arg1, T2 arg2)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        Emit(logger, level, null!, message, arg1, arg2);
    }

    /// <summary>
    /// Logs a templated message with three arguments and no array allocation.
    /// </summary>
    /// <typeparam name="T1">The type of the first template argument.</typeparam>
    /// <typeparam name="T2">The type of the second template argument.</typeparam>
    /// <typeparam name="T3">The type of the third template argument.</typeparam>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="level">The severity to log at.</param>
    /// <param name="message">A message template with three placeholders.</param>
    /// <param name="arg1">The value for the first placeholder.</param>
    /// <param name="arg2">The value for the second placeholder.</param>
    /// <param name="arg3">The value for the third placeholder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void Log<T1, T2, T3>(ILogger logger, LogLevel level, string message, T1 arg1, T2 arg2, T3 arg3)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (!logger.IsEnabled(level))
        {
            return;
        }

        Emit(logger, level, null!, message, arg1, arg2, arg3);
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
    /// Prefer the strongly-typed overloads for one to three arguments; this overload
    /// allocates a <c>params</c> array and exists for the open-ended case.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
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
            Resolve(level)(logger, message, exception);
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

        Resolve(level)(logger, message ?? "N/A", null!);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    private static void LogNoArgs(ILogger logger, LogLevel level, string message, Exception exception)
    {
        if (!logger.IsEnabled(level))
        {
            return;
        }

        Resolve(level)(logger, message ?? "N/A", exception);
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
    /// Logs an <see cref="LogLevel.Error"/> message without an exception.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">The message template. Must not be <see langword="null"/>.</param>
    /// <param name="args">Optional template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
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
    /// Logs a <see cref="LogLevel.Critical"/> message without an exception.
    /// </summary>
    /// <param name="logger">The logger to write to.</param>
    /// <param name="message">The message template. Must not be <see langword="null"/>.</param>
    /// <param name="args">Optional template arguments.</param>
    /// <exception cref="ArgumentNullException"><paramref name="logger"/> or <paramref name="message"/> is <see langword="null"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
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
        LogNoArgs(logger, LogLevel.Error, msg, null!);
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
        LogNoArgs(logger, LogLevel.Critical, msg, null!);
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

            _ = sb.Append("Timestamp      : ").AppendLine(_cachedUtc)
                .Append("Title          : ").AppendLine(title ?? "N/A")
                .Append("Exception Type : ").AppendLine(ex.GetType().FullName)
                .Append("Message        : ").AppendLine(ex.Message?.Trim() ?? "N/A")
                .Append("HResult        : ").Append(ex.HResult).AppendLine()
                .Append("Source         : ").AppendLine(ex.Source ?? "N/A")
                .Append("Target Site    : ").AppendLine(ex.TargetSite?.Name ?? "N/A");

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

    // Walks the inner-exception chain (and AggregateException branches) up to maxDepth,
    // indenting each level with '>' so the nesting is readable in plain-text sinks.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AppendInnerExceptionDetails(StringBuilder sb, Exception inner, int depth, int maxDepth = 5)
    {
        if (inner is null || depth > maxDepth)
        {
            return;
        }

        string indent = new('>', depth);
        _ = sb.Append(indent).Append(" Exception Type : ").AppendLine(inner.GetType().FullName)
            .Append(indent).Append(" Message        : ").AppendLine(inner.Message?.Trim() ?? "N/A")
            .Append(indent).Append(" HResult        : ").Append(inner.HResult).AppendLine()
            .Append(indent).Append(" Source         : ").AppendLine(inner.Source ?? "N/A")
            .Append(indent).Append(" Target Site    : ").AppendLine(inner.TargetSite?.Name ?? "N/A");

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
    public static IDisposable WriteScope(this ILogger logger, IDictionary<string, object> context)
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

        public IEnumerator<KeyValuePair<string, object>> GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _items.GetEnumerator();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
        public override string ToString()
        {
            if (_items.Count == 0)
            {
                return string.Empty;
            }

            StringBuilder sb = new(_items.Count * 16);
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0)
                {
                    _ = sb.Append(' ');
                }

                KeyValuePair<string, object> kv = _items[i];
                _ = sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            return sb.ToString();
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

            StringBuilder sb = new(_items.Length * 16);
            for (int i = 0; i < _items.Length; i++)
            {
                if (i > 0)
                {
                    _ = sb.Append(' ');
                }

                KeyValuePair<string, object> kv = _items[i];
                _ = sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            return sb.ToString();
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
    public static Func<LogLevel, string, Exception, bool> AsyncSinkFilter { get; private set; } = static (_, _, _) => true;

    /// <summary>
    /// Replaces the <see cref="AsyncSinkFilter"/> predicate.
    /// </summary>
    /// <param name="filter">The new filter. A <see langword="null"/> value leaves the current filter unchanged.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining | MethodImplOptions.AggressiveOptimization)]
    public static void UseAsyncSinkProvider(Func<LogLevel, string, Exception, bool> filter)
    {
        AsyncSinkFilter = filter ?? AsyncSinkFilter;
    }

    /// <summary>
    /// Flushes any buffered asynchronous logging work.
    /// </summary>
    /// <param name="cancellationToken">A token to observe while waiting.</param>
    /// <returns>A task that completes when the (currently no-op) flush finishes.</returns>
    /// <remarks>
    /// A placeholder that lets callers <c>await</c> a flush today and keep that code
    /// correct if a buffering sink is added later. It never throws: cancellation and
    /// disposal are swallowed, and any unexpected non-fatal error is traced and ignored.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
        }
        catch (OperationCanceledException) { /* Cancellation is expected here; nothing to flush. */ }
        catch (ObjectDisposedException) { /* Pipeline already torn down; nothing to flush. */ }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            Debug.WriteLine($"Nilogger.FlushAsync encountered error: {ex}");
        }
    }

    #endregion Extensibility Hooks (Async/Batch Ready)
}
