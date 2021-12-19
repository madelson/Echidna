using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Medallion.Data.Templating;

namespace Medallion.Data;

public readonly struct SqlTemplate : IEquatable<SqlTemplate>
{
    private readonly ImmutableArray<SqlTemplateFragment> _fragments;

    public SqlTemplate(InterpolatedStringSqlTemplate template)
        : this(template.ToFragmentsAndClear())
    {
    }

    internal SqlTemplate(ImmutableArray<SqlTemplateFragment> fragments)
    {
        this._fragments = fragments;
    }

    internal ImmutableArray<SqlTemplateFragment> Fragments => 
        this._fragments.IsDefault ? ImmutableArray<SqlTemplateFragment>.Empty : this._fragments;

    public static ConditionalSqlTemplate If(bool condition, [InterpolatedStringHandlerArgument("condition")] InterpolatedStringSqlTemplate template) =>
        new(condition ? template.ToFragmentsAndClear() : null);

    public static ConditionalSqlTemplate Else(InterpolatedStringSqlTemplate template) => new(template.ToFragmentsAndClear());

    public static SqlTemplate ForEach<T>(IEnumerable<T> source, InterpolatedStringSqlTemplateSelector<T> selector)
    {
        ArgumentNullException.ThrowIfNull(selector, nameof(selector));

        return new($"{source.Select(t => new SqlTemplate(selector(t)))}");
    }

    public static SqlTemplate ForEach<T>(IEnumerable<T> source, InterpolatedStringSqlTemplateSelectorWithIndex<T> selector)
    {
        ArgumentNullException.ThrowIfNull(selector, nameof(selector));

        return new($"{source.Select((t, index) => new SqlTemplate(selector(t, index)))}");
    }

    public bool Equals(SqlTemplate that) => this.Fragments.SequenceEqual(that.Fragments);

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SqlTemplate that && this.Equals(that);

    public override int GetHashCode()
    {
        var hashCode = default(HashCode);
        foreach (var fragment in this.Fragments) { hashCode.Add(fragment); }
        return hashCode.ToHashCode();
    }
}

public delegate InterpolatedStringSqlTemplate InterpolatedStringSqlTemplateSelector<T>(T value);
public delegate InterpolatedStringSqlTemplate InterpolatedStringSqlTemplateSelectorWithIndex<T>(T value, int index);

