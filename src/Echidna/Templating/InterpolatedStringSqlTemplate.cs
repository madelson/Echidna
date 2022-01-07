using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Medallion.Data.Templating;

namespace Medallion.Data;

[InterpolatedStringHandler]
public ref struct InterpolatedStringSqlTemplate
{
    private ImmutableArray<SqlTemplateFragment>.Builder? _fragments;

    public InterpolatedStringSqlTemplate(int literalLength, int formattedCount)
    {
        InitializeFragments(literalLength, formattedCount, out this._fragments);
    }

    public InterpolatedStringSqlTemplate(int literalLength, int formattedCount, bool condition, out bool shouldAppend)
        : this(literalLength, formattedCount)
    {
        if (condition)
        {
            InitializeFragments(literalLength, formattedCount, out this._fragments);
            shouldAppend = true;
        }
        else { shouldAppend = false; }
    }

    private static void InitializeFragments(int literalLength, int formattedCount, out ImmutableArray<SqlTemplateFragment>.Builder fragments)
    {
        var expectedCapacity = formattedCount + Math.Min(literalLength, formattedCount);
        fragments = ImmutableArray.CreateBuilder<SqlTemplateFragment>(initialCapacity: expectedCapacity);
    }

    private ImmutableArray<SqlTemplateFragment>.Builder Fragments
    {
        get
        {
            var fragments = this._fragments;
            if (fragments is null) { ThrowFragmentsMissing(); }
            return fragments;

            [DoesNotReturn] static void ThrowFragmentsMissing() =>
                throw new InvalidOperationException($"An instance of {nameof(InterpolatedStringSqlTemplate)} cannot be used without being properly initialized or after being consumed");
        }
    }

    public void AppendLiteral(string value) => this.Fragments.Add(new(value, SqlTemplateFragmentType.Raw));

    public void AppendFormatted<T>(T value, string? format = null)
    {
        // TODO convert null to "TypedNull" here for parameters so that we can have the proper dbtype

        if (value is SqlTemplate template)
        {
            if (format != null) { ThrowBadFormat(format); }
            this.AppendFormatted(template);
        }
        else if (value is IEnumerable<SqlTemplate> templates)
        {
            if (format != null) { ThrowBadFormat(format); }
            this.AppendFormatted(templates);
        }
        else
        {
            this.Fragments.Add(new(value, ParseFormat(format)));
        }

        static void ThrowBadFormat(string format) =>
            throw new FormatException($"{nameof(format)} '{format}' is not valid for a value of type {typeof(T)}");
    }

    public void AppendFormatted(ConditionalSqlTemplate value)
    {
        if (value.Fragments != null) { this.Fragments.AddRange(value.Fragments); }
    }

    public void AppendFormatted(SqlTemplate value) => this.Fragments.AddRange(value.Fragments);

    public void AppendFormatted(IEnumerable<SqlTemplate> value)
    {
        foreach (var template in value ?? throw new ArgumentNullException(nameof(value)))
        {
            this.AppendFormatted(template);
        }
    }

    public void AppendFormatted(InterpolatedStringSqlTemplate template) =>
        this.Fragments.AddRange(template.ToFragmentsAndClear());

    public static implicit operator InterpolatedStringSqlTemplate(SqlTemplate template)
    {
        var interpolatedStringTemplate = default(InterpolatedStringSqlTemplate);
        interpolatedStringTemplate._fragments = template.Fragments.ToBuilder();
        return interpolatedStringTemplate;
    }

    internal ImmutableArray<SqlTemplateFragment> ToFragmentsAndClear()
    {
        var fragments = this.Fragments;
        this._fragments = null;
        fragments.Capacity = fragments.Count;
        return fragments.MoveToImmutable();
    }

    private static SqlTemplateFragmentType ParseFormat(string? format) =>
        format switch
        {
            null => SqlTemplateFragmentType.Default,
            "r" => SqlTemplateFragmentType.Raw,
            "v" => SqlTemplateFragmentType.Value,
            "p" => SqlTemplateFragmentType.Parameter,
            _ => throw new FormatException($"Unknown SQL template format specifier '{format}'. Expected 'p' (parameter), 'v' (value), or 'r' (raw)")
        };
}

