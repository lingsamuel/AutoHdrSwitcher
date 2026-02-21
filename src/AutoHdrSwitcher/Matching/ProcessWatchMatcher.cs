using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace AutoHdrSwitcher.Matching;

public static class ProcessWatchMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);
    private static readonly ConcurrentDictionary<RegexCacheKey, Regex> RegexCache = new();

    public static bool IsMatch(string candidate, ProcessWatchRule rule)
    {
        if (rule is null)
        {
            throw new ArgumentNullException(nameof(rule));
        }

        if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.Pattern) || string.IsNullOrEmpty(candidate))
        {
            return false;
        }

        // Regex mode has highest priority and ignores ExactMatch + CaseSensitive by design.
        if (rule.RegexMode)
        {
            try
            {
                var regex = GetOrCreateRegex(
                    pattern: rule.Pattern,
                    options: RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                return regex.IsMatch(candidate);
            }
            catch (ArgumentException)
            {
                // Invalid user regex should not crash the watcher loop.
                return false;
            }
        }

        var comparison = rule.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        if (rule.ExactMatch)
        {
            return string.Equals(candidate, rule.Pattern, comparison);
        }

        // Default mode: includes. If '*' exists, treat as simple wildcard mode.
        if (!rule.Pattern.Contains('*'))
        {
            return candidate.IndexOf(rule.Pattern, comparison) >= 0;
        }

        var wildcardPattern = WildcardToAnchoredRegex(rule.Pattern);
        var wildcardOptions =
            RegexOptions.Compiled |
            RegexOptions.CultureInvariant |
            (rule.CaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase);
        var wildcardRegex = GetOrCreateRegex(wildcardPattern, wildcardOptions);
        return wildcardRegex.IsMatch(candidate);
    }

    public static bool IsMatchAny(string candidate, IReadOnlyCollection<ProcessWatchRule> rules)
    {
        if (rules is null)
        {
            throw new ArgumentNullException(nameof(rules));
        }

        foreach (var rule in rules)
        {
            if (IsMatch(candidate, rule))
            {
                return true;
            }
        }

        return false;
    }

    public static bool TryFindFirstMatch(
        string candidate,
        IReadOnlyCollection<ProcessWatchRule> rules,
        out ProcessWatchRule? matchedRule)
    {
        if (rules is null)
        {
            throw new ArgumentNullException(nameof(rules));
        }

        foreach (var rule in rules)
        {
            if (IsMatch(candidate, rule))
            {
                matchedRule = rule;
                return true;
            }
        }

        matchedRule = null;
        return false;
    }

    private static Regex GetOrCreateRegex(string pattern, RegexOptions options)
    {
        var key = new RegexCacheKey(pattern, options);
        return RegexCache.GetOrAdd(
            key,
            static k => new Regex(k.Pattern, k.Options, RegexTimeout));
    }

    private static string WildcardToAnchoredRegex(string pattern)
    {
        // Only '*' is supported as wildcard. Everything else is escaped.
        return "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
    }

    private readonly record struct RegexCacheKey(string Pattern, RegexOptions Options);
}
