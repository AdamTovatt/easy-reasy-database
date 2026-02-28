using EasyReasy.EnvironmentVariables;

namespace EasyReasy.Database.Mapping.Benchmarks
{
    [EnvironmentVariableNameContainer]
    public static class EnvironmentVariables
    {
        [EnvironmentVariableName(minLength: 10)]
        public static readonly VariableName DatabaseConnectionString = new VariableName("DATABASE_CONNECTION_STRING");
    }
}
