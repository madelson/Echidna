using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace SourceGen;

[Generator]
internal class ExpressionComparerGenerator : ISourceGenerator
{
    public void Execute(GeneratorExecutionContext context)
    {
        var specs = new ExpressionVisitorGenerator.IExpressionVisitorSpec[]
        {
            new ExpressionEqualityVisitorSpec(),
            new ExpressionHashingVisitorSpec(),
        };

        foreach (var spec in specs)
        {
            var code = ExpressionVisitorGenerator.GenerateVisitor(spec);
            context.AddSource($"{spec.TypeName}.g.cs", code);

            System.IO.Directory.CreateDirectory(@"C:\dev\sourcegenout");
            System.IO.File.WriteAllText($@"C:\dev\sourcegenout\{spec.TypeName}.cs", $"// Generated {DateTime.Now}{Environment.NewLine}" + code);
        }
    }

    public void Initialize(GeneratorInitializationContext context)
    {
    }
}
