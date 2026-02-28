namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// A collection of named parameters that can be built dynamically at runtime.
    /// Use this instead of anonymous objects when the parameter set is not known at compile time,
    /// such as bulk insert operations with variable batch sizes.
    /// </summary>
    public class DynamicParameters
    {
        private readonly List<(string Name, object? Value)> _parameters = new();

        /// <summary>
        /// Adds a named parameter with the given value.
        /// </summary>
        public void Add(string name, object? value)
        {
            _parameters.Add((name, value));
        }

        /// <summary>
        /// Gets the parameters as a read-only list for binding.
        /// </summary>
        internal IReadOnlyList<(string Name, object? Value)> GetParameters() => _parameters;
    }
}
