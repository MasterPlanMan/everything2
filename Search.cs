using System.Text.RegularExpressions;

namespace Everything2;

public sealed record Entry(string Path, bool IsDirectory)
{
    public string Name { get; } = System.IO.Path.GetFileName(Path.TrimEnd('\\'));
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? Path;
}

public sealed record SearchResult(Entry[] Entries, int Total, double Milliseconds);

public sealed class Query
{
    private readonly List<Func<Entry, bool>> predicates = [];
    public Query(string text, int kind = 0, string? drive = null)
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
    public bool Matches(Entry entry) => predicates.All(p => p(entry));
    public SearchResult Search(Entry[] entries, CancellationToken token, int limit = 2000)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var found = new List<Entry>();
        var total = 0;
        for (var i = 0; i < entries.Length; i++)
        {
            if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
            if (!Matches(entries[i])) continue;
            total++;
            if (found.Count < limit) found.Add(entries[i]);
        }
        found.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return new(found.ToArray(), total, clock.Elapsed.TotalMilliseconds);
    }
}
