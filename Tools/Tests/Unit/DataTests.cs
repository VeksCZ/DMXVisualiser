using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

static class Repo
{
    public static string Root
    {
        get
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d != null && !Directory.Exists(Path.Combine(d.FullName, "Assets", "Visualizer"))) d = d.Parent;
            return d?.FullName ?? throw new DirectoryNotFoundException("kořen repozitáře nenalezen");
        }
    }
}

// Lokalizace: každý klíč má češtinu i angličtinu, stejné {0} v obou, a všechny klíče použité v kódu existují.
public class LocTests
{
    static Dictionary<string, string[]> Table() =>
        (Dictionary<string, string[]>)typeof(Loc).GetField("D", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

    static IEnumerable<string> Placeholders(string s) =>
        Regex.Matches(s, @"\{(\d+)\}").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x);

    [Fact]
    public void EveryEntryHasBothLanguages()
    {
        foreach (var kv in Table())
        {
            Assert.True(kv.Value.Length == 2, kv.Key + ": musí mít CZ a EN");
            Assert.False(string.IsNullOrWhiteSpace(kv.Value[0]), kv.Key + ": prázdná čeština");
            Assert.False(string.IsNullOrWhiteSpace(kv.Value[1]), kv.Key + ": prázdná angličtina");
        }
    }

    [Fact]
    public void PlaceholdersMatchBetweenLanguages()
    {
        foreach (var kv in Table())
            Assert.True(Placeholders(kv.Value[0]).SequenceEqual(Placeholders(kv.Value[1])), kv.Key + ": různé {n} v CZ a EN");
    }

    [Fact]
    public void FormattingWorksInBothLanguages()
    {
        foreach (var en in new[] { false, true })
        {
            Loc.En = en;
            foreach (var kv in Table())
            {
                int n = Placeholders(kv.Value[0]).Select(int.Parse).DefaultIfEmpty(-1).Max() + 1;
                var args = Enumerable.Range(0, n).Select(i => (object)"x").ToArray();
                Loc.F(kv.Key, args);   // nesmí vyhodit FormatException (třeba nezdvojená složená závorka)
            }
        }
        Loc.En = false;
    }

    [Fact]
    public void KeysUsedInCodeExist()
    {
        var keys = Table().Keys.ToHashSet();
        var re = new Regex(@"Loc\.[TF]\(""([^""]+)""");
        var missing = new List<string>();
        foreach (var f in Directory.GetFiles(Path.Combine(Repo.Root, "Assets", "Visualizer", "Scripts"), "*.cs", SearchOption.AllDirectories))
            if (Path.GetFileName(f) != "Loc.cs")      // tam je klíč jen v komentáři
            foreach (Match m in re.Matches(File.ReadAllText(f)))
                if (!keys.Contains(m.Groups[1].Value)) missing.Add(Path.GetFileName(f) + ": " + m.Groups[1].Value);
        Assert.True(missing.Count == 0, "Chybějící klíče Loc: " + string.Join(", ", missing));
    }
}

// Vestavěné profily: validní JSON, kanály v rozsahu režimu, odkazy na existující části, rozsahy hodnot 0–255.
public class BuiltinProfileTests
{
    static IEnumerable<string> Files() =>
        Directory.GetFiles(Path.Combine(Repo.Root, "Assets", "Visualizer", "Resources", "Profiles"), "*.json");

    public static IEnumerable<object[]> Names() => Files().Select(f => new object[] { Path.GetFileName(f) });

    static JsonElement Load(string name) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Repo.Root, "Assets", "Visualizer", "Resources", "Profiles", name))).RootElement;

    [Fact]
    public void ProfilesExist() => Assert.True(Files().Count() >= 10);

    [Theory, MemberData(nameof(Names))]
    public void IdMatchesFileName(string file)
    {
        var p = Load(file);
        Assert.Equal(Path.GetFileNameWithoutExtension(file), p.GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("name").GetString()));
    }

    [Theory, MemberData(nameof(Names))]
    public void ModesAndControlsAreConsistent(string file)
    {
        var p = Load(file);
        string kind = p.TryGetProperty("kind", out var k) ? k.GetString() : "light";
        if (kind == "prop") { Assert.True(p.TryGetProperty("prop", out _)); return; }

        int parts = p.TryGetProperty("parts", out var pa) ? pa.GetArrayLength() : 0;
        var modes = p.GetProperty("modes");
        Assert.True(modes.GetArrayLength() > 0);
        foreach (var m in modes.EnumerateArray())
        {
            int channels = m.GetProperty("channels").GetInt32();
            Assert.InRange(channels, 1, 512);
            foreach (var c in m.GetProperty("controls").EnumerateArray())
            {
                string fn = c.GetProperty("fn").GetString();
                int ch = c.GetProperty("ch").GetInt32();
                Assert.InRange(ch, 1, channels);
                if (c.TryGetProperty("fine", out var fine)) Assert.InRange(fine.GetInt32(), 0, channels);
                if (kind != "hazer")
                {
                    int part = c.TryGetProperty("part", out var pt) ? pt.GetInt32() : 0;
                    Assert.True(part >= 0 && part < parts, file + ": " + fn + " míří na neexistující část " + part);
                }
                if (c.TryGetProperty("ranges", out var ranges))
                    foreach (var r in ranges.EnumerateArray())
                    {
                        int from = r.GetProperty("from").GetInt32(), to = r.GetProperty("to").GetInt32();
                        Assert.True(from >= 0 && to <= 255 && from <= to, file + ": " + fn + " rozsah " + from + "–" + to);
                    }
            }
        }
    }

    [Theory, MemberData(nameof(Names))]
    public void ColorsAreHex(string file)
    {
        var re = new Regex("\"(color2?|top|lycra|offColor)\"\\s*:\\s*\"([^\"]*)\"");
        foreach (Match m in re.Matches(File.ReadAllText(Path.Combine(Repo.Root, "Assets", "Visualizer", "Resources", "Profiles", file))))
            if (m.Groups[2].Value.Length > 0)
                Assert.Matches("^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", m.Groups[2].Value);
    }
}
