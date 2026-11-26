using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Scheduling.IntegrationTests.Fixtures;

namespace Scheduling.IntegrationTests.Contract;

/// <summary>FR-030: o documento OpenAPI gerado expõe exatamente as operações de specs/001-online-scheduling-api/contracts/openapi.yaml.</summary>
public sealed partial class OpenApiConformanceTests(ApiFixture fixture) : IntegrationTestBase(fixture)
{
    private const string ContractRelativePath = "specs/001-online-scheduling-api/contracts/openapi.yaml";
    private const string ServerPrefix = "/api/v1";

    [Fact]
    public async Task GeneratedDocument_MatchesContractOperations()
    {
        var expected = ReadContractOperations();
        Assert.Equal(34, expected.Count);

        var response = await Api.Anonymous().GetAsync("/openapi/v1.json");
        await TestApi.EnsureAsync(response, HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var actual = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject().Where(o => HttpMethods.Contains(o.Name)))
            {
                actual.Add($"{operation.Name.ToUpperInvariant()} {path.Name}");
            }
        }

        Assert.Equal(expected, actual);
        Assert.True(document.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("bearerAuth", out _));
    }

    private static readonly HashSet<string> HttpMethods = ["get", "post", "put", "patch", "delete"];

    private static SortedSet<string> ReadContractOperations()
    {
        var file = FindContract();
        var operations = new SortedSet<string>(StringComparer.Ordinal);
        string? currentPath = null;

        foreach (var line in File.ReadLines(file))
        {
            if (PathLine().Match(line) is { Success: true } path)
            {
                currentPath = ServerPrefix + path.Groups[1].Value;
            }
            else if (currentPath is not null && MethodLine().Match(line) is { Success: true } method)
            {
                operations.Add($"{method.Groups[1].Value.ToUpperInvariant()} {currentPath}");
            }
            else if (line.StartsWith("components:", StringComparison.Ordinal))
            {
                break;
            }
        }

        return operations;
    }

    private static string FindContract()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ContractRelativePath);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException($"Contrato não encontrado: {ContractRelativePath}");
    }

    [GeneratedRegex(@"^  (/[^:\s]+):\s*$")]
    private static partial Regex PathLine();

    [GeneratedRegex(@"^    (get|post|put|patch|delete):\s*$")]
    private static partial Regex MethodLine();
}
