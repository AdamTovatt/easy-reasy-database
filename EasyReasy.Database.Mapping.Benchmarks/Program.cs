using BenchmarkDotNet.Running;
using EasyReasy.Database.Mapping.Benchmarks;
using EasyReasy.EnvironmentVariables;

string variablesFilePath = Path.Combine(AppContext.BaseDirectory, "..", "..", "EnvironmentVariables.txt");

if (!File.Exists(variablesFilePath))
{
    string exampleContent = EnvironmentVariableHelper.GetExampleContent(
        "DATABASE_CONNECTION_STRING", "Host=localhost;Port=5432;Database=easy-reasy-db-mapping;Username=postgres;Password=postgres");

    File.WriteAllText(variablesFilePath, exampleContent);
}

EnvironmentVariableHelper.LoadVariablesFromFile(variablesFilePath);
EnvironmentVariableHelper.ValidateVariableNamesIn(typeof(EnvironmentVariables));

// Multiple benchmark sets live in this assembly — use BenchmarkSwitcher so users can pick.
// No args: interactive console picker.
// Run all sets: dotnet run -c Release -- --filter '*'
// Run one set:  dotnet run -c Release -- --filter '*PolymorphicJson*'
BenchmarkSwitcher.FromTypes(new[]
{
    typeof(QueryBenchmarks),
    typeof(PolymorphicJsonBenchmarks),
}).Run(args);
