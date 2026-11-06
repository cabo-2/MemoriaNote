using System.Text;
using MemoriaNote.Domain.Queries;
using MemoriaNote.Persistence.SchemaV2;
using NUnit.Framework;

namespace MemoriaNote.Core.Tests.Persistence.SchemaV2;

/// <summary>Verifies MemoriaNote glob compilation is isolated from SQLite syntax.</summary>
[TestFixture]
public sealed class SqliteGlobPatternCompilerTests
{
    /// <summary>Verifies literal SQLite metacharacters are encoded without backslash semantics.</summary>
    [Test]
    public void Compile_EscapedMetacharacters_ProducesLiteralSqlitePattern()
    {
        var glob = Parse("draft-\\*\\?\\[\\]\\-\\\\");

        Assert.That(
            SqliteGlobPatternCompiler.Compile(glob),
            Is.EqualTo("draft-[*][?][[]]-\\"));
    }

    /// <summary>Verifies Unix and SQLite negation aliases compile to the same SQLite class.</summary>
    [Test]
    public void Compile_NegatedAliases_ProducesSamePattern()
    {
        Assert.That(
            SqliteGlobPatternCompiler.Compile(Parse("[!a-z]")),
            Is.EqualTo("[^a-z]"));
        Assert.That(
            SqliteGlobPatternCompiler.Compile(Parse("[^a-z]")),
            Is.EqualTo("[^a-z]"));
    }

    /// <summary>Verifies ranges containing SQLite class delimiters retain scalar semantics.</summary>
    [Test]
    public void Compile_SpecialRangeEndpoints_SplitsEquivalentRanges()
    {
        Assert.That(
            SqliteGlobPatternCompiler.Compile(Parse("[\\]-a]")),
            Is.EqualTo("[]_-a^]"));
        Assert.That(
            SqliteGlobPatternCompiler.Compile(Parse("[\\--0]")),
            Is.EqualTo("[-.-0]"));
    }

    /// <summary>Verifies a provider pattern cannot exceed its explicit UTF-8 bound.</summary>
    [Test]
    public void Compile_OversizedProviderPattern_Throws()
    {
        var source = new StringBuilder("[");
        for (var index = 0; index < 255; index++)
        {
            var start = 0x10000 + index * 3;
            source.Append(new Rune(start));
            source.Append(new Rune(start + 1));
        }
        source.Append(']');
        var glob = Parse(source.ToString());

        Assert.That(
            () => SqliteGlobPatternCompiler.Compile(glob),
            Throws.TypeOf<ArgumentException>());
    }

    private static MemoriaNoteGlob Parse(string source)
    {
        var parsed = MemoriaNoteGlobParser.TryParse(source, out var glob, out var error);
        Assert.That(parsed, Is.True, error.ToString());
        return glob!;
    }
}
