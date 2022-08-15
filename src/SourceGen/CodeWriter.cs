using System;
using System.Collections.Generic;
using System.Text;

namespace SourceGen;

internal class CodeWriter
{
    private readonly StringBuilder _builder = new();
    private readonly Stack<char> _blockChars = new();

    public CodeWriter OpenBlock(char blockChar = '{')
    {
        this.AppendIndent().Append(blockChar).AppendLine();
        this._blockChars.Push(blockChar);
        return this;
    }

    public CodeWriter CloseBlock()
    {
        var blockChar = this._blockChars.Pop();
        this.AppendIndent()
            .Append(blockChar switch
            {
                '{' => '}',
                '[' => ']',
                '(' => ')',
                _ => throw new InvalidOperationException($"Bad block char '{blockChar}'"),
            })
            .AppendLine();
        return this;
    }

    public CodeWriter Line(string text = "")
    {
        if (text.Length == 0)
        {
            this._builder.AppendLine();
        }
        else
        {
            this.AppendIndent().AppendLine(text);
        }
        return this;
    }

    public override string ToString() => this._builder.ToString();

    private StringBuilder AppendIndent() => this._builder.Append('\t', repeatCount: this._blockChars.Count);
}
