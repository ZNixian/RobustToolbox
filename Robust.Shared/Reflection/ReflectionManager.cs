﻿using Robust.Shared.Interfaces.Reflection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Robust.Shared.Log;

namespace Robust.Shared.Reflection
{
    public abstract class ReflectionManager : IReflectionManager
    {
        /// <summary>
        /// Enumerable over prefixes that are added to the type provided to <see cref="GetType(string)"/>
        /// if the type can't be found in any assemblies.
        /// </summary>
        /// <remarks>
        /// First prefix should probably be <code>""</code>.
        /// </remarks>
        protected abstract IEnumerable<string> TypePrefixes { get; }
        private readonly List<Assembly> assemblies = new List<Assembly>();

        public event EventHandler<ReflectionUpdateEventArgs> OnAssemblyAdded;

        public IReadOnlyList<Assembly> Assemblies => assemblies;

        public IEnumerable<Type> GetAllChildren<T>(bool inclusive = false)
        {
            try
            {
                // There's very little assemblies, so storing these temporarily is cheap.
                // We need to do it ahead of time so that we can catch ReflectionTypeLoadException HERE,
                // so whoever is using us doesn't have to handle them.
                var TypeLists = new List<Type[]>(Assemblies.Count);
                TypeLists.AddRange(Assemblies.Select(t => t.GetTypes()));

                return TypeLists.SelectMany(t => t)
                                .Where(t => typeof(T).IsAssignableFrom(t)
                                    && !t.IsAbstract
                                    && ((Attribute.GetCustomAttribute(t, typeof(ReflectAttribute)) as ReflectAttribute)
                                        ?.Discoverable ?? ReflectAttribute.DEFAULT_DISCOVERABLE)
                                    && (inclusive || typeof(T) != t));
            }
            catch (ReflectionTypeLoadException e)
            {
                Logger.Error("Caught ReflectionTypeLoadException! Dumping child exceptions:");
                foreach (var inner in e.LoaderExceptions)
                {
                    Logger.Error(inner.ToString());
                }
                throw;
            }
        }

        public void LoadAssemblies(params Assembly[] args) => LoadAssemblies(args.AsEnumerable());
        public void LoadAssemblies(IEnumerable<Assembly> assemblies)
        {
            this.assemblies.AddRange(assemblies);
            OnAssemblyAdded?.Invoke(this, new ReflectionUpdateEventArgs(this));
        }

        /// <seealso cref="TypePrefixes"/>
        public Type GetType(string name)
        {
            // The priority in which types are retrieved is based on the TypePrefixes list.
            // This is an implementation detail. If you need it: make a better API.
            foreach (string prefix in TypePrefixes)
            {
                string appendedName = prefix + name;
                foreach (var assembly in Assemblies)
                {
                    var theType = assembly.GetType(appendedName);
                    if (theType != null)
                    {
                        return theType;
                    }
                }
            }

            return null;
        }

        /// <inheritdoc />
        public Type LooseGetType(string name)
        {
            if (TryLooseGetType(name, out var ret))
            {
                return ret;
            }
            throw new ArgumentException("Unable to find type.");
        }

        public bool TryLooseGetType(string name, out Type type)
        {
            foreach (var assembly in assemblies)
            {
                foreach (var tryType in assembly.DefinedTypes)
                {
                    if (tryType.FullName.EndsWith(name))
                    {
                        type = tryType;
                        return true;
                    }
                }
            }

            type = default;
            return false;
        }

        /// <inheritdoc />
        public IEnumerable<Type> FindTypesWithAttribute<T>()
        {
            var types = new List<Type>();

            foreach (var assembly in Assemblies)
            {
                types.AddRange(assembly.GetTypes().Where(type => Attribute.IsDefined(type, typeof(T))));
            }

            return types;
        }

        /// <inheritdoc />
        public bool TryParseEnumReference(string reference, out Enum @enum)
        {
            if (!reference.StartsWith("enum."))
            {
                @enum = default;
                return false;
            }

            reference = reference.Substring(5);
            var dotIndex = reference.LastIndexOf('.');
            var typeName = reference.Substring(0, dotIndex);
            var value = reference.Substring(dotIndex + 1);

            foreach (var assembly in assemblies)
            {
                foreach (var type in assembly.DefinedTypes)
                {
                    if (!type.IsEnum || !type.FullName.EndsWith(typeName))
                    {
                        continue;
                    }

                    @enum = (Enum)Enum.Parse(type, value);
                    return true;
                }
            }

            throw new ArgumentException("Could not resolve enum reference.");
        }
    }
}
