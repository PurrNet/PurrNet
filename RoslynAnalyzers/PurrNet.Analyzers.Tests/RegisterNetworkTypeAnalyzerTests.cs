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
    public class RegisterNetworkTypeAnalyzerTests
    {
        private static readonly MetadataReference[] PlatformReferences =
            ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            .Split(Path.PathSeparator).Select(path => MetadataReference.CreateFromFile(path)).ToArray();

        [Theory]
        [InlineData("PurrNet.Authentication.AuthenticationRequest<Payload>", false)]
        [InlineData("PurrNet.Authentication.AuthenticationRequest<string>", false)]
        [InlineData("PurrNet.Authentication.AuthenticationRequest<>", false)]
        [InlineData("System.Collections.Generic.List<Payload>", false)]
        [InlineData("Payload", false)]
        [InlineData("ForeignPayload", true)]
        [InlineData("ForeignWrapper<Payload>", true)]
        [InlineData("PurrNet.Authentication.AuthenticationRequest<ForeignPayload>", true)]
        public async Task RegistrationMatchesAssemblyOwnership(string target, bool expectWarning)
        {
            var runtime = CSharpCompilation.Create("PurrNet.Runtime",
                new[] { CSharpSyntaxTree.ParseText(@"
namespace PurrNet {
    public class RegisterNetworkTypeAttribute : System.Attribute {
        public RegisterNetworkTypeAttribute(System.Type type) {}
    }
}
namespace PurrNet.Authentication {
    public struct AuthenticationRequest<T> { public T payload; }
}
public struct ForeignPayload { public int value; }
public struct ForeignWrapper<T> { public T value; }") }, PlatformReferences,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var runtimeImage = new MemoryStream();
            var emitted = runtime.Emit(runtimeImage);
            Assert.True(emitted.Success, string.Join("\n", emitted.Diagnostics));

            var game = CSharpCompilation.Create("Assembly-CSharp",
                new[] { CSharpSyntaxTree.ParseText(@"
public struct Payload { public int value; }
[PurrNet.RegisterNetworkType(typeof(" + target + @"))]
public class CustomAuthenticator {}
") }, PlatformReferences.Append(MetadataReference.CreateFromImage(runtimeImage.ToArray())),
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            Assert.Empty(game.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error));
            var diagnostics = await game.WithAnalyzers(
                ImmutableArray.Create<DiagnosticAnalyzer>(new RegisterNetworkTypeAnalyzer()))
                .GetAnalyzerDiagnosticsAsync();
            Assert.Equal(expectWarning ? 1 : 0, diagnostics.Count(d => d.Id == "PN0014"));
        }
    }
}
