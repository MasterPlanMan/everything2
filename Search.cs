using System.Text.RegularExpressions;
using System.Globalization;

namespace Everything2;

public sealed record Entry(string Path, bool IsDirectory, long? Size = null, DateTime? ModifiedUtc = null)
{
    public string Name { get; } = System.IO.Path.GetFileName(Path.TrimEnd('\\'));
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? Path;
}

public sealed record SearchResult(Entry[] Entries, int Total, double Milliseconds);

public sealed class Query
{
    private readonly List<Func<Entry, bool>> predicates = [];
    public Query(string text, int kind = 0, string? drive = null, DateTime? now = null)
    {
        if (kind != 0) predicates.Add(e => e.IsDirectory == (kind == 2));
        if (!string.IsNullOrEmpty(drive)) predicates.Add(e => e.Path.StartsWith(drive, StringComparison.OrdinalIgnoreCase));
        foreach (Match token in Regex.Matches(text, "(?:[^\\s\"]|\"[^\"]*\")+"))
        {
            var value = token.Value.Replace("\"", "");
            var negative = value.StartsWith('!');
            if (negative) value = value[1..];
            Func<Entry, bool> test;
            if (value.Equals("folder:", StringComparison.OrdinalIgnoreCase)) test = e => e.IsDirectory;
            else if (value.Equals("file:", StringComparison.OrdinalIgnoreCase)) test = e => !e.IsDirectory;
            else if (value.StartsWith("size:", StringComparison.OrdinalIgnoreCase)) test = SizeFilter(value[5..]);
            else if (value.StartsWith("modified:", StringComparison.OrdinalIgnoreCase)) test = DateFilter(value[9..], now ?? DateTime.Now);
            else if (value.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            {
                var extensions = value[4..].Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => "." + x.TrimStart('.')).ToArray();
                test = e => !e.IsDirectory && extensions.Contains(System.IO.Path.GetExtension(e.Name), StringComparer.OrdinalIgnoreCase);
            }
            else
            {
                var path = value.StartsWith("path:", StringComparison.OrdinalIgnoreCase);
                if (path) value = value[5..];
                path |= value.Contains('\\') || value.Contains('/');
                value = value.Replace('/', '\\');
                var term = value;
                if (term.Contains('*') || term.Contains('?'))
                {
                    var pattern = new Regex(Regex.Escape(term).Replace("\\*", ".*").Replace("\\?", "."), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
                    test = e => pattern.IsMatch(path ? e.Path : e.Name);
                }
                else test = e => (path ? e.Path : e.Name).Contains(term, StringComparison.OrdinalIgnoreCase);
            }
            predicates.Add(negative ? e => !test(e) : test);
        }
    }
    private static (string Operator, string Value) Comparison(string text)
    {
        foreach (var op in new[] { ">=", "<=", ">", "<", "=" })
            if (text.StartsWith(op, StringComparison.Ordinal)) return (op, text[op.Length..]);
        return ("=", text);
    }
    private static long Bytes(string text)
    {
        var match = Regex.Match(text, @"^(\d+(?:\.\d+)?)\s*(B|KB|MB|GB|TB)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
            throw new FormatException("Use size:>10MB or size:1MB..100MB (B, KB, MB, GB, TB).");
        var power = match.Groups[2].Value.ToUpperInvariant() switch { "KB" => 1, "MB" => 2, "GB" => 3, "TB" => 4, _ => 0 };
        for (var i = 0; i < power; i++)
        {
            if (number > long.MaxValue / 1024m) throw new FormatException("Size is too large.");
            number *= 1024;
        }
        if (number > long.MaxValue) throw new FormatException("Size is too large.");
        return (long)number;
    }
    private static Func<Entry, bool> SizeFilter(string text)
    {
        if (text.Contains(".."))
        {
            var range = text.Split("..");
            if (range.Length != 2) throw new FormatException("Use size:1MB..100MB.");
            var min = Bytes(range[0]); var max = Bytes(range[1]);
            if (min > max) throw new FormatException("The minimum size must be below the maximum.");
            return e => !e.IsDirectory && e.Size is { } size && size >= min && size <= max;
        }
        var (op, value) = Comparison(text); var bytes = Bytes(value);
        return e => !e.IsDirectory && e.Size is { } size && (op switch { ">" => size > bytes, ">=" => size >= bytes, "<" => size < bytes, "<=" => size <= bytes, _ => size == bytes });
    }
    private static DateTime Day(string text, DateTime now)
    {
        if (text.Equals("today", StringComparison.OrdinalIgnoreCase)) return now.Date;
        if (text.Equals("yesterday", StringComparison.OrdinalIgnoreCase)) return now.Date.AddDays(-1);
        if (DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day < DateTime.MaxValue.Date) return day;
        throw new FormatException("Use modified:>=2026-01-01, modified:today, or modified:7d.");
    }
    private static Func<Entry, bool> DateFilter(string text, DateTime now)
    {
        if (Regex.IsMatch(text, @"^\d+d$", RegexOptions.IgnoreCase))
        {
            if (!int.TryParse(text[..^1], out var days) || days < 1 || days > 365000) throw new FormatException("Use a day count between 1d and 365000d.");
            var since = now.AddDays(-days).ToUniversalTime(); var until = now.ToUniversalTime();
            return e => e.ModifiedUtc is { } date && date >= since && date <= until;
        }
        if (text.Contains(".."))
        {
            var range = text.Split("..");
            if (range.Length != 2) throw new FormatException("Use modified:2026-01-01..2026-01-31.");
            var startDay = Day(range[0], now); var endDay = Day(range[1], now);
            if (startDay > endDay) throw new FormatException("The start date must be before the end date.");
            var start = startDay.ToUniversalTime(); var end = endDay.AddDays(1).ToUniversalTime();
            return e => e.ModifiedUtc is { } date && date >= start && date < end;
        }
        var (op, value) = Comparison(text); var day = Day(value, now);
        var first = day.ToUniversalTime(); var next = day.AddDays(1).ToUniversalTime();
        return e => e.ModifiedUtc is { } date && (op switch { ">" => date >= next, ">=" => date >= first, "<" => date < first, "<=" => date < next, _ => date >= first && date < next });
    }
    public bool Matches(Entry entry) => predicates.All(p => p(entry));
    public SearchResult Search(Entry[] entries, CancellationToken token, int limit = 2000, ISet<string>? preferredPaths = null)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var found = new List<Entry>();
        var preferred = new List<Entry>();
        var total = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            if (!Matches(entries[i])) continue;
            total++;
            if (preferredPaths?.Contains(entries[i].Path) == true)
            {
                if (preferred.Count < limit) preferred.Add(entries[i]);
            }
            else if (found.Count < limit) found.Add(entries[i]);
        }
        var selected = preferred.Concat(found.Take(Math.Max(0, limit - preferred.Count))).ToArray();
        ResultOrder.Sort(selected, preferredPaths: preferredPaths);
        return new(selected, total, clock.Elapsed.TotalMilliseconds);
    }
}

public static class ResultOrder
{
    public static void Sort(Entry[] entries, string column = "Name", bool descending = false, ISet<string>? preferredPaths = null)
    {
        Array.Sort(entries, (a, b) =>
        {
            // Pin rank stays first even when the column order is descending.
            var rank = (preferredPaths?.Contains(b.Path) == true).CompareTo(preferredPaths?.Contains(a.Path) == true);
            if (rank != 0) return rank;
            var comparison = column switch
            {
                "Path" => StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path),
                "Type" => StringComparer.OrdinalIgnoreCase.Compare(a.IsDirectory ? "Folder" : Path.GetExtension(a.Name), b.IsDirectory ? "Folder" : Path.GetExtension(b.Name)),
                "Size" => Nullable.Compare(a.Size, b.Size),
                "Modified" => Nullable.Compare(a.ModifiedUtc, b.ModifiedUtc),
                _ => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name)
            };
            if (comparison == 0) comparison = StringComparer.OrdinalIgnoreCase.Compare(a.Path, b.Path);
            return descending ? -comparison : comparison;
        });
    }
}
