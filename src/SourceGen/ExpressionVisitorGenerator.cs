using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace SourceGen;

internal class ExpressionVisitorGenerator
{
    private static readonly IReadOnlyDictionary<Type, MethodInfo> VisitMethodsByNodeType = typeof(ExpressionVisitor)
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
        .Where(m => m.IsVirtual && m.Attributes.HasFlag(MethodAttributes.Family) && m.Name.StartsWith("Visit") && m.Name != "Visit" && m.Name != "VisitDebugInfo")
        .ToDictionary(m => m.GetParameters().Single().ParameterType);

    private static readonly HashSet<string> IgnoredExpressionPropertyNames = new(
        typeof(Expression)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            // Ignored because it is redundant with other properties and implicitly implemented
            .Concat(new[] { nameof(IArgumentProvider.ArgumentCount) })
    );

    public static string GenerateVisitor(IExpressionVisitorSpec spec)
    {
        var code = new CodeWriter();
        foreach (var @namespace in new[] { "System.Collections.Generic", "System.Linq.Expressions" })
        {
            code.Line($"using {@namespace};");
        }

        code.Line("namespace Medallion.Data.Entities;")
            .Line($"internal partial class {spec.TypeName} : ExpressionVisitor")
            .OpenBlock();

        code.Line("public override Expression Visit(Expression node)")
            .OpenBlock()
            .Line("if (node is null)")
            .OpenBlock();
        spec.HandleNullExpression(code);
        code.CloseBlock();
        spec.HandleExpressionType(code);
        code.Line("return base.Visit(node);")
            .CloseBlock()
            .Line();

        foreach (var kvp in VisitMethodsByNodeType)
        {
            var visitMethod = kvp.Value;
            var nodeType = kvp.Key;
            var nodeTypeName = visitMethod.IsGenericMethodDefinition ? nodeType.Name.Replace("`1", "<T>") : nodeType.Name;
            code.Line($"protected override {visitMethod.ReturnType.Name} {visitMethod.Name}{(visitMethod.IsGenericMethodDefinition ? "<T>" : string.Empty)}({nodeTypeName} node)")
                .OpenBlock();

            if (visitMethod.Name == "VisitExtension")
            {
                code.Line(@"throw new NotSupportedException($""Extension nodes are not supported: {node.GetType()}"");");
            }
            else
            {
                spec.HandleVisitStart(nodeTypeName, code);

                if (visitMethod.Name == "VisitMember")
                {
                    code.Line("if (ExpressionHelper.IsClosureAccess(node))")
                        .OpenBlock();
                    spec.HandleClosureAccess(code);
                    code.Line("return node;")
                        .CloseBlock();
                }

                var properties = nodeType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => !IgnoredExpressionPropertyNames.Contains(p.Name));
                foreach (var property in properties)
                {
                    HandleProperty(property.PropertyType, property.Name);
                }

                spec.HandleVisitEnd(code);
            }

            code.CloseBlock()
                .Line();

            void HandleProperty(Type type, string accessor)
            {
                if (typeof(Expression).IsAssignableFrom(type))
                {
                    spec.HandleBeforeVisitChild(accessor, code);
                    code.Line($"this.Visit(node.{accessor});");
                    spec.HandleAfterVisitChild(accessor, code);
                }
                else if (VisitMethodsByNodeType.TryGetValue(type, out var method))
                {
                    spec.HandleBeforeVisitChild(accessor, code);
                    code.Line($"this.{method.Name}(node.{accessor});");
                    spec.HandleAfterVisitChild(accessor, code);
                }
                else if (type.IsConstructedGenericType
                    && type.GetGenericTypeDefinition() == typeof(ReadOnlyCollection<>))
                {
                    code.OpenBlock()
                        .Line($"var count = node.{accessor}.Count;");
                    spec.HandleBeforeVisitChildCollection(accessor, code);
                    code.Line($"for (var i = 0; i < count; ++i)")
                        .OpenBlock();
                    HandleProperty(type.GetGenericArguments().Single(), $"{accessor}[i]");
                    code.CloseBlock()
                        .CloseBlock();
                }
                else
                {
                    spec.HandleChildPropertyValue(accessor, type, code);
                }
            }
        }

        code.CloseBlock();

        return code.ToString();
    }

    public interface IExpressionVisitorSpec
    {
        public string TypeName { get; }

        void HandleNullExpression(CodeWriter code);
        void HandleExpressionType(CodeWriter code);
        void HandleVisitStart(string nodeTypeName, CodeWriter code);
        void HandleVisitEnd(CodeWriter code);
        void HandleClosureAccess(CodeWriter code);
        void HandleBeforeVisitChild(string accessor, CodeWriter code);
        void HandleAfterVisitChild(string accessor, CodeWriter code);
        void HandleBeforeVisitChildCollection(string accessor, CodeWriter code);
        void HandleChildPropertyValue(string accessor, Type type, CodeWriter code);
    }
}
