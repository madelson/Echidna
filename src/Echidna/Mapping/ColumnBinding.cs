using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Medallion.Data.Mapping;

internal sealed record ColumnBinding(ColumnValueRetrieval Retrieval, ColumnBindingTarget Target);

// using record here to allow for == between targets to work
internal abstract record ColumnBindingTarget()
{
    protected abstract string InternalToString();

    public sealed override string ToString() => this.InternalToString();
}

internal sealed record DictionaryKeyBindingTarget(string Key, Type DictionaryType) : ColumnBindingTarget 
{
    protected override string InternalToString() => $"{this.DictionaryType} value";
}
internal sealed record TupleElementBindingTarget(int ElementIndex, Type TupleType) : ColumnBindingTarget
{
    protected override string InternalToString() => $"Item{this.ElementIndex} of {this.TupleType}";
}

internal sealed record ArrayElementBindingTarget(int Order, Type ElementType) : ColumnBindingTarget
{
    protected override string InternalToString() => $"{this.ElementType} element";
}

internal sealed record PropertyBindingTarget(PropertyInfo Property) : ColumnBindingTarget
{
    protected override string InternalToString() => $"property {this.Property} of {this.Property.DeclaringType}";
}

internal sealed record ConstructorParameterBindingTarget(ParameterInfo Parameter) : ColumnBindingTarget
{
    protected override string InternalToString() => $"parameter {this.Parameter} of {this.Parameter.Member.DeclaringType}.{this.Parameter.Member.Name}";
}

internal sealed record NestedBindingTarget(ColumnBindingTarget OuterTarget, ColumnBindingTarget Target) : ColumnBindingTarget 
{
    protected override string InternalToString() => $"{this.Target} of {this.OuterTarget}";
}


