using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SourceGen;

internal class ExpressionEqualityVisitorSpec : ExpressionVisitorGenerator.IExpressionVisitorSpec
{
    public string TypeName => "ExpressionEqualityVisitor";

    public void HandleAfterVisitChild(string accessor, CodeWriter code) =>
        code.Line("if (this._failed) { return node; }");

    public void HandleBeforeVisitChild(string accessor, CodeWriter code) =>
        code.Line($"this._other = other.{accessor};");

    public void HandleBeforeVisitChildCollection(string accessor, CodeWriter code) =>
        this.FailIf($"count != other.{accessor}.Count", code);

    public void HandleChildPropertyValue(string accessor, Type type, CodeWriter code)
    {
        var nullableUnderlyingType = Nullable.GetUnderlyingType(type);
        var underlyingType = nullableUnderlyingType ?? type;
        if (!underlyingType.IsPrimitive
            && !underlyingType.IsEnum
            && type != typeof(string)
            && type != typeof(object)
            && type != typeof(CallSiteBinder)
            && !typeof(MemberInfo).IsAssignableFrom(type))
        {
            throw new InvalidOperationException($"Unexpected property type {type} (accessor = '{accessor}')");
        }

        this.FailIf(
            $"!EqualityComparer<{underlyingType}{(nullableUnderlyingType is null ? string.Empty : "?")}>.Default.Equals(node.{accessor}, other.{accessor})",
            code
        );
    }

    public void HandleClosureAccess(CodeWriter code) => this.FailIf("!ExpressionHelper.IsClosureAccess(other)", code);

    public void HandleExpressionType(CodeWriter code) => this.FailIf("node.Type != ((Expression)this._other).Type", code);

    public void HandleNullExpression(CodeWriter code) =>
        code.Line("if (this._other is not null) { this._failed = true; }")
            .Line("return node;");

    public void HandleVisitEnd(CodeWriter code) => code.Line("this._other = other;").Line("return node;");

    public void HandleVisitStart(string nodeTypeName, CodeWriter code) => this.FailIf($"this._other is not {nodeTypeName} other", code);

    private void FailIf(string condition, CodeWriter code)
    {
        code.Line($"if ({condition})")
            .OpenBlock()
            .Line("this._failed = true;")
            .Line("return node;")
            .CloseBlock();
    }
}
