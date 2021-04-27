using System;
using System.CodeDom.Compiler;
using System.IO;

namespace SourceGeneration.Mvvm.Helpers
{
    public class SourceWriter : IndentedTextWriter
    {
        public SourceWriter(TextWriter writer) : base(writer) { }

        public IDisposable StartBlock(string text)
        {
            WriteLine(text);
            return new BaseBlock(this);
        }

        public IDisposable StartFunction(string returnType, string functionName, string arguments ="", string prefix = "")
        {
            if (string.IsNullOrEmpty(prefix) == false)
            {
                Write(prefix);
            }

            WriteLine($"{returnType} {functionName}({arguments})");
            return new BaseBlock(this);
        }

        public IDisposable StartProperty(string propertyType, string propertyName)
        {
            WriteLine($"public {propertyType} {propertyName}");
            return new BaseBlock(this);
        }

        class BaseBlock : IDisposable
        {
            protected readonly IndentedTextWriter _writer;

            public BaseBlock(IndentedTextWriter writer)
            {
                _writer = writer;

                _writer.WriteLine("{");
                _writer.Indent++;
            }

            public virtual void Dispose()
            {
                _writer.Indent--;
                _writer.WriteLine("}");
            }
        }
    }
}
