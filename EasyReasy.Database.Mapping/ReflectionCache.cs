using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

namespace EasyReasy.Database.Mapping
{
    /// <summary>
    /// Caches reflection metadata (property accessors, setters, column mappings) and compiled
    /// expression delegates to avoid repeated reflection lookups and enable near-native speed.
    /// </summary>
    internal static class ReflectionCache
    {
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();
        private static readonly ConcurrentDictionary<PropertyInfo, Action<object, object?>> SetterCache = new();
        private static readonly ConcurrentDictionary<Type, Func<object>> ParameterlessFactoryCache = new();
        private static readonly ConcurrentDictionary<Type, ConstructionStrategy> StrategyCache = new();

        /// <summary>
        /// Gets the public instance properties of a type, cached for reuse.
        /// </summary>
        internal static PropertyInfo[] GetProperties(Type type)
        {
            return PropertyCache.GetOrAdd(type, t =>
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance));
        }

        /// <summary>
        /// Gets a compiled property setter delegate for the given property.
        /// Compiles: (object instance, object? value) => ((Entity)instance).Property = (Type)value
        /// </summary>
        internal static Action<object, object?> GetPropertySetter(PropertyInfo property)
        {
            return SetterCache.GetOrAdd(property, p =>
            {
                ParameterExpression instanceParam = Expression.Parameter(typeof(object), "instance");
                ParameterExpression valueParam = Expression.Parameter(typeof(object), "value");

                UnaryExpression castInstance = Expression.Convert(instanceParam, p.DeclaringType!);
                UnaryExpression castValue = Expression.Convert(valueParam, p.PropertyType);
                MemberExpression propertyAccess = Expression.Property(castInstance, p);
                BinaryExpression assign = Expression.Assign(propertyAccess, castValue);

                return Expression.Lambda<Action<object, object?>>(assign, instanceParam, valueParam).Compile();
            });
        }

        /// <summary>
        /// Gets a compiled parameterless constructor factory for the given type.
        /// Compiles: () => (object)new Entity()
        /// </summary>
        internal static Func<object> GetParameterlessFactory(Type type)
        {
            return ParameterlessFactoryCache.GetOrAdd(type, t =>
            {
                NewExpression newExpr = Expression.New(t);
                UnaryExpression castToObject = Expression.Convert(newExpr, typeof(object));
                return Expression.Lambda<Func<object>>(castToObject).Compile();
            });
        }

        /// <summary>
        /// Gets the cached construction strategy for the given type, determining whether to use
        /// a parameterless or parameterized constructor and pre-computing the relevant delegates.
        /// </summary>
        internal static ConstructionStrategy GetConstructionStrategy(Type type)
        {
            return StrategyCache.GetOrAdd(type, t =>
            {
                PropertyInfo[] allProperties = GetProperties(t);
                ConstructorInfo? parameterlessCtor = t.GetConstructor(
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

                if (parameterlessCtor != null)
                {
                    return new ConstructionStrategy(
                        parameterlessFactory: GetParameterlessFactory(t),
                        parameterizedFactory: null,
                        constructorParameters: Array.Empty<ParameterInfo>(),
                        settableProperties: allProperties.Where(p => p.CanWrite).ToArray());
                }

                // Pick the public constructor with the most parameters
                ConstructorInfo[] publicCtors = t.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

                if (publicCtors.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"Type '{t.FullName}' has no public constructors.");
                }

                ConstructorInfo bestCtor = publicCtors.OrderByDescending(c => c.GetParameters().Length).First();
                ParameterInfo[] ctorParams = bestCtor.GetParameters();

                // Build compiled parameterized factory:
                // (object[] args) => (object)new Entity((string)args[0], (int?)args[1], ...)
                ParameterExpression argsParam = Expression.Parameter(typeof(object[]), "args");
                Expression[] argExpressions = new Expression[ctorParams.Length];

                for (int i = 0; i < ctorParams.Length; i++)
                {
                    Expression arrayAccess = Expression.ArrayIndex(argsParam, Expression.Constant(i));
                    argExpressions[i] = Expression.Convert(arrayAccess, ctorParams[i].ParameterType);
                }

                NewExpression newExpr = Expression.New(bestCtor, argExpressions);
                UnaryExpression castToObject = Expression.Convert(newExpr, typeof(object));
                Func<object[], object> parameterizedFactory =
                    Expression.Lambda<Func<object[], object>>(castToObject, argsParam).Compile();

                // Remaining settable properties not covered by constructor parameters
                HashSet<string> ctorParamNames = new(
                    ctorParams.Select(p => p.Name!), StringComparer.OrdinalIgnoreCase);

                PropertyInfo[] remainingProps = allProperties
                    .Where(p => p.CanWrite && !ctorParamNames.Contains(p.Name))
                    .ToArray();

                return new ConstructionStrategy(
                    parameterlessFactory: null,
                    parameterizedFactory: parameterizedFactory,
                    constructorParameters: ctorParams,
                    settableProperties: remainingProps);
            });
        }
    }

    /// <summary>
    /// Describes how to construct an entity type: either via a parameterless constructor
    /// (with all properties set afterwards) or via a parameterized constructor
    /// (with remaining settable properties set afterwards).
    /// </summary>
    internal sealed class ConstructionStrategy
    {
        /// <summary>
        /// Compiled factory for parameterless construction: () => (object)new Entity().
        /// Null when the type has no parameterless constructor.
        /// </summary>
        public Func<object>? ParameterlessFactory { get; }

        /// <summary>
        /// Compiled factory for parameterized construction: (object[] args) => (object)new Entity(...).
        /// Null when the type has a parameterless constructor.
        /// </summary>
        public Func<object[], object>? ParameterizedFactory { get; }

        /// <summary>
        /// The constructor parameters for the parameterized path (empty for parameterless).
        /// </summary>
        public ParameterInfo[] ConstructorParameters { get; }

        /// <summary>
        /// Properties that should be set after construction (all settable properties for
        /// parameterless path, or only those not covered by constructor params).
        /// </summary>
        public PropertyInfo[] SettableProperties { get; }

        /// <summary>
        /// True when the type uses a parameterless constructor.
        /// </summary>
        public bool HasParameterlessConstructor => ParameterlessFactory != null;

        public ConstructionStrategy(
            Func<object>? parameterlessFactory,
            Func<object[], object>? parameterizedFactory,
            ParameterInfo[] constructorParameters,
            PropertyInfo[] settableProperties)
        {
            ParameterlessFactory = parameterlessFactory;
            ParameterizedFactory = parameterizedFactory;
            ConstructorParameters = constructorParameters;
            SettableProperties = settableProperties;
        }
    }
}
