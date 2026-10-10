// -----------------------------------------------------------------------------
//  Nilog tests — F-008 regression: the template cache must admit atomically, never
//  exceed its limit, refuse oversized templates, shrink when the limit is lowered,
//  and keep retained memory bounded under adversarial template workloads.
//
//  File        : TemplateCacheBoundsTests.cs
// -----------------------------------------------------------------------------
using Microsoft.Extensions.Logging;

namespace Nilog.Tests;

public class TemplateCacheBoundsTests
{
    private static void WithCache(int limit, int maxLen, Action body)
    {
        int savedLimit = Nilogger.MaxTemplateCacheEntries;
        int savedLen = Nilogger.MaxCachedTemplateLength;
        try
        {
            Nilogger.ClearTemplateCache();
            Nilogger.MaxCachedTemplateLength = maxLen;
            Nilogger.MaxTemplateCacheEntries = limit;
            body();
        }
        finally
        {
            Nilogger.MaxTemplateCacheEntries = savedLimit;
            Nilogger.MaxCachedTemplateLength = savedLen;
            Nilogger.ClearTemplateCache();
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(16)]
    [InlineData(64)]
    public void ConcurrentInsertion_NeverExceedsLimit(int workers)
    {
        WithCache(100, 1024, () =>
        {
            TestLogger l = new();
            int peak = 0;
            Parallel.For(0, workers, new ParallelOptions { MaxDegreeOfParallelism = workers }, w =>
            {
                TestLogger local = new();
                for (int i = 0; i < 2000; i++)
                {
                    local.WriteInformation<int>("tpl-" + w + "-" + i + " {A}", i);
                    int c = Nilogger.TemplateCacheCount;
                    int p;
                    while (c > (p = Volatile.Read(ref peak))) { Interlocked.CompareExchange(ref peak, c, p); }
                }
            });
            Assert.True(peak <= 100, $"peak {peak} exceeded limit 100");
            Assert.True(Nilogger.TemplateCacheCount <= 100);
            l.WriteInformation<int>("after {A}", 1);
        });
    }

    [Fact]
    public void SaturatedCache_StillRendersCorrectly()
    {
        WithCache(2, 1024, () =>
        {
            TestLogger l = new();
            for (int i = 0; i < 50; i++) { l.WriteInformation<int>("t" + i + " {A}", i); }
            Assert.Equal(50, l.Entries.Count);
            Assert.Equal("t49 49", l.Last.Message);
            Assert.Equal(49, l.Last["A"]);
            Assert.Equal(2, Nilogger.TemplateCacheCount);
        });
    }

    [Fact]
    public void HundredOneMegabyteTemplates_AreNotRetained()
    {
        WithCache(10_000, 1024, () =>
        {
            TestLogger l = new() { MinLevel = LogLevel.Information };
            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < 100; i++)
            {
                string big = new string('x', 1_000_000) + "{A}" + i;
                l.WriteInformation<int>(big, i);
            }
            Assert.Equal(0, Nilogger.TemplateCacheCount);
            l.Entries.Clear();
            long after = GC.GetTotalMemory(true);
            // Phase 1 baseline: ~574 MiB retained for this workload. Bound is generous (10 MiB).
            Assert.True(after - before < 10L * 1024 * 1024, $"retained {(after - before) / 1024 / 1024} MiB");
        });
    }

    [Fact]
    public void LoweringLimit_ShrinksCache()
    {
        WithCache(100, 1024, () =>
        {
            TestLogger l = new();
            for (int i = 0; i < 50; i++) { l.WriteInformation<int>("s" + i + " {A}", i); }
            Assert.Equal(50, Nilogger.TemplateCacheCount);
            Nilogger.MaxTemplateCacheEntries = 10;
            Assert.True(Nilogger.TemplateCacheCount <= 10);
        });
    }

    [Fact]
    public void ZeroLimit_DisablesCaching_ButLoggingWorks()
    {
        WithCache(0, 1024, () =>
        {
            TestLogger l = new();
            l.WriteInformation<int>("z {A}", 7);
            Assert.Equal(0, Nilogger.TemplateCacheCount);
            Assert.Equal("z 7", l.Single.Message);
        });
    }

    [Fact]
    public void NegativeLimit_IsIgnored()
    {
        WithCache(5, 1024, () =>
        {
            Nilogger.MaxTemplateCacheEntries = -1;
            Assert.Equal(5, Nilogger.MaxTemplateCacheEntries);
            Nilogger.MaxCachedTemplateLength = 0;
            Assert.Equal(1024, Nilogger.MaxCachedTemplateLength);
        });
    }

    [Fact]
    public void HotTemplates_AreCachedOnce()
    {
        WithCache(100, 1024, () =>
        {
            TestLogger l = new();
            for (int i = 0; i < 1000; i++) { l.WriteInformation<int>("hot {A}", i); }
            Assert.Equal(1, Nilogger.TemplateCacheCount);
        });
    }
}
