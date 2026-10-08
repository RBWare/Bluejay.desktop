using System.Reflection;

namespace Grayjay.ClientServer.Constants
{
    public static class App
    {
        public static int Version { get; } = Assembly.GetExecutingAssembly()?.GetName()?.Version?.Minor ?? -1;
        public static string VersionType { get; } = "stable";

        /// <summary>
        /// The official Grayjay Desktop release this Bluejay build is based on, set at build time from git tags.
        /// </summary>
        public static int GrayjayBaseVersion { get; } = int.TryParse(Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(x => x.Key == "GrayjayBaseVersion")?.Value, out var v) ? v : 0;
    }
}
