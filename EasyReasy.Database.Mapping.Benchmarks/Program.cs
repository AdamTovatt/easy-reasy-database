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

BenchmarkRunner.Run<QueryBenchmarks>();
