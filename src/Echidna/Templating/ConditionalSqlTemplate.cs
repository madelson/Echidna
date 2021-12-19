using Medallion.Data.Templating;
using System.Collections.Immutable;

// TODO review all namespaces
namespace Medallion.Data;

public readonly ref struct ConditionalSqlTemplate
{
    internal ConditionalSqlTemplate(ImmutableArray<SqlTemplateFragment>? fragments)
    {
        this.Fragments = fragments;
    }

    internal readonly ImmutableArray<SqlTemplateFragment>? Fragments { get; }

    public static ConditionalSqlTemplate operator |(ConditionalSqlTemplate @if, ConditionalSqlTemplate @else) =>
        @if.Fragments.HasValue
            ? throw new InvalidOperationException("Conditional templates should be combined as follows: If(...) || If(...) || Else(...)")
            : @else;

    public static bool operator true(ConditionalSqlTemplate template) => template.Fragments.HasValue;

    public static bool operator false(ConditionalSqlTemplate template) => !template.Fragments.HasValue;

    public static implicit operator SqlTemplate(ConditionalSqlTemplate template) => new(template.Fragments ?? ImmutableArray<SqlTemplateFragment>.Empty);
}
