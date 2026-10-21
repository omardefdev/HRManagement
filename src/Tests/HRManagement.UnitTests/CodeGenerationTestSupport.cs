using HRManagement.ServiceDefaults.CodeGeneration;
using HRManagement.SharedKernel.CodeGeneration;
using HRManagement.TestSupport.CodeGeneration;

namespace HRManagement.UnitTests;

internal static class CodeGenerationTestSupport
{
    public static ICodeGenerationClient CreateClient() => new FakeCodeGenerationClient();
}
