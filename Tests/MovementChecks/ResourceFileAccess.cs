// Only the resource-file boundary is substituted. Checks execute the real repository and JSON validation.
namespace Godot
{
    public static class FileAccess
    {
        private static string Resolve(string path) => System.IO.Path.Combine(
            System.AppContext.BaseDirectory, path.Replace("res://", ""));
        public static bool FileExists(string path) => System.IO.File.Exists(Resolve(path));
        public static string GetFileAsString(string path) => System.IO.File.ReadAllText(Resolve(path));
    }
}
