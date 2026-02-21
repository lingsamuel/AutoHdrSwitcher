# Process Rule Matching

Each row in the watch list maps to one `ProcessWatchRule`.
The app keeps running even when no rules are configured.

Priority is fixed:

1. `RegexMode = true`
   - Use regular expression matching.
   - `ExactMatch` and `CaseSensitive` are ignored by design.
2. `RegexMode = false` and `ExactMatch = true`
   - Use full-string comparison.
3. `RegexMode = false` and `ExactMatch = false`
   - Default behavior is `includes`.
   - If the pattern contains `*`, use simple wildcard matching (`*` only).

Default behavior:

- `ExactMatch = false`
- `CaseSensitive = false`
- `RegexMode = false`
- `PollIntervalSeconds = 2`

API entry points:

- `ProcessWatchMatcher.IsMatch(candidate, rule)`
- `ProcessWatchMatcher.IsMatchAny(candidate, rules)`
- `ProcessWatchMatcher.TryFindFirstMatch(candidate, rules, out matchedRule)`
