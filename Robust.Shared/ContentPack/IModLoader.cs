using Robust.Shared.Interfaces.Resources;

namespace Robust.Shared.ContentPack
{
    public interface IModLoader
    {
        /// <summary>
        ///     Loads an assembly into the current AppDomain.
        /// </summary>
        /// <typeparam name="T">The type of the entry point to search for.</typeparam>
        /// <param name="assembly">Byte array of the assembly.</param>
        /// <param name="symbols">Optional byte array of the debug symbols.</param>
        void LoadGameAssembly<T>(byte[] assembly, byte[] symbols = null)
            where T : GameShared;

        /// <summary>
        ///     Loads an assembly into the current AppDomain.
        /// </summary>
        /// <typeparam name="T">The type of the entry point to search for.</typeparam>
        void LoadGameAssembly<T>(string diskPath)
            where T : GameShared;

        /// <summary>
        ///     Broadcasts a run level change to all loaded entry point.
        /// </summary>
        /// <param name="level">New level</param>
        void BroadcastRunLevel(ModRunLevel level);

        void BroadcastUpdate(ModUpdateLevel level, float frameTime);

        /// <summary>
        ///     Tries to load an assembly from a resource manager into the current appdomain.
        /// </summary>
        /// <typeparam name="T">The type of the entry point to search for.</typeparam>
        /// <param name="resMan">The assembly will be located inside of this resource manager.</param>
        /// <param name="assemblyName">File name of the assembly inside of the ./Assemblies folder in the resource manager.</param>
        /// <returns>If the assembly was successfully located and loaded.</returns>
        bool TryLoadAssembly<T>(IResourceManager resMan, string assemblyName)
            where T : GameShared;
    }
}
