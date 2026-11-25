using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace PurrNet.Analyzers.Tests
{
    public class NetworkModuleAnalyzerTests
    {
        private static readonly MetadataReference[] PlatformReferences =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToArray();

        [Theory]
        [InlineData("Module current => _a;", false)]
        [InlineData("Module current => this._a;", false)]
        [InlineData("Module current => _useB ? _b : _a;", false)]
        [InlineData("Module current => (_useB ? _b : (_a));", false)]
        [InlineData("Module current => _maybe ?? _a;", false)]
        [InlineData("Module current { get { if (_useB) return _b; return _a; } }", false)]
        [InlineData("Module current { get => _a; }", false)]
        [InlineData("Module current => inherited;", false)]
        [InlineData("Module current => new Module();", true)]
        [InlineData("Module current => _useB ? _b : new Module();", true)]
        [InlineData("Module current => _maybe ??= new Module();", true)]
        [InlineData("Module current => Make();", true)]
        [InlineData("Module current => _other._a;", true)]
        [InlineData("Module current => Shared;", true)]
        [InlineData("Module current { get { var m = new Module(); return m; } }", true)]
        [InlineData("Module current { set { } }", true)]
        public async Task OnlyPropertiesWithNoFieldBehindThemWarn(string property, bool expectWarning)
        {
            var source = @"
namespace PurrNet {
    public class NetworkModule {}
    public class NetworkIdentity {}
}
public class Module : PurrNet.NetworkModule {}
public class Base : PurrNet.NetworkIdentity {
    protected Module inherited = new Module();
}
public class Identity : Base {
    private Module _a = new Module();
    private Module _b = new Module();
    private Module _maybe = new Module();
    private Identity _other;
    private bool _useB;
    private static readonly Module Shared = new Module();
    private Module Make() => _a;
    " + property + @"
}";
            var compilation = CSharpCompilation.Create("Game",
                new[] { CSharpSyntaxTree.ParseText(source) }, PlatformReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.Empty(compilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
            var diagnostics = await compilation.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new NetworkModuleAnalyzer()))
                .GetAnalyzerDiagnosticsAsync();
            Assert.Equal(expectWarning ? 1 : 0, diagnostics.Count(d => d.Id == "PN0103"));
        }
    }
}
