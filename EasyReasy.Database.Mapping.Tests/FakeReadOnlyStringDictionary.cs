using System.Collections;

namespace EasyReasy.Database.Mapping.Tests
{
    /// <summary>
    /// Implements <see cref="IReadOnlyDictionary{TKey, TValue}"/> over string keys but deliberately
    /// does not implement the non-generic <see cref="IDictionary"/>. Used to verify that the
    /// parameter binder rejects such inputs with a clear exception rather than silently binding
    /// the container's instance properties via reflection.
    /// </summary>
    internal sealed class FakeReadOnlyStringDictionary : IReadOnlyDictionary<string, object?>
    {
        private readonly Dictionary<string, object?> _inner = new()
        {
            ["name"] = "fake",
        };

        public object? this[string key] => _inner[key];

        public IEnumerable<string> Keys => _inner.Keys;

        public IEnumerable<object?> Values => _inner.Values;

        public int Count => _inner.Count;

        public bool ContainsKey(string key) => _inner.ContainsKey(key);

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _inner.GetEnumerator();

        public bool TryGetValue(string key, out object? value) => _inner.TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator() => _inner.GetEnumerator();
    }
}
