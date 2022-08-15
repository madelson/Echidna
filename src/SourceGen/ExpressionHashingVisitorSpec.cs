using System;

namespace SourceGen;

internal class ExpressionHashingVisitorSpec : ExpressionVisitorGenerator.IExpressionVisitorSpec
{
    public string TypeName => "ExpressionHashingVisitor";

    public void HandleAfterVisitChild(string accessor, CodeWriter code) { }

    public void HandleBeforeVisitChild(string accessor, CodeWriter code) { }

    public void HandleBeforeVisitChildCollection(string accessor, CodeWriter code) { }

    public void HandleChildPropertyValue(string accessor, Type type, CodeWriter code) => this.Add($"node.{accessor}", code);

    public void HandleClosureAccess(CodeWriter code) { }

    public void HandleExpressionType(CodeWriter code) { }

    public void HandleNullExpression(CodeWriter code) { }

    public void HandleVisitEnd(CodeWriter code) => code.Line("return node;");

    public void HandleVisitStart(string nodeTypeName, CodeWriter code) { }

    private void Add(string text, CodeWriter code) => code.Line($"this._hash.Add({text});");
}
