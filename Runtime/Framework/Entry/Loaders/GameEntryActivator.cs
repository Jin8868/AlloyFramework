using System;
using System.Collections.Generic;
using System.Reflection;

namespace AlloyFramework
{
    internal static class GameEntryActivator
    {
        public static IGameEntry CreateFromLoadedAssemblies()
        {
            var candidates = new List<Type>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (var index = 0; index < assemblies.Length; index++)
                CollectCandidates(assemblies[index], candidates);
            return CreateSingle(candidates, "loaded assemblies");
        }

        public static IGameEntry Create(Assembly assembly)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));

            var candidates = new List<Type>();
            CollectCandidates(assembly, candidates);
            return CreateSingle(candidates, $"assembly {assembly.GetName().Name}");
        }

        private static void CollectCandidates(Assembly assembly, ICollection<Type> candidates)
        {
            var types = GetLoadableTypes(assembly);
            for (var index = 0; index < types.Length; index++)
            {
                var type = types[index];
                if (type != null && !type.IsAbstract && !type.IsGenericTypeDefinition &&
                    typeof(IGameEntry).IsAssignableFrom(type))
                    candidates.Add(type);
            }
        }

        private static IGameEntry CreateSingle(IReadOnlyList<Type> candidates, string source)
        {
            if (candidates.Count != 1)
                throw new InvalidOperationException(
                    $"Expected exactly one concrete {nameof(IGameEntry)} implementation in " +
                    $"{source}, but found {candidates.Count}.");

            try
            {
                return (IGameEntry)Activator.CreateInstance(candidates[0], true);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Failed to create game entry {candidates[0].FullName}.", exception);
            }
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types;
            }
        }
    }
}