namespace EasyReasy.Database.Testing
{
    /// <summary>
    /// Locates the repository root a directory belongs to, by walking up until the directory containing a
    /// named sentinel file (typically the solution file) is found. The root itself is what identifies a
    /// checkout, which is how <see cref="CheckoutDatabaseIdentity"/> derives the database a checkout owns;
    /// it is also useful on its own for reading committed non-compiled artifacts (SQL scripts, infra
    /// files) from tests without depending on the exact bin depth.
    /// </summary>
    public static class RepositoryRoot
    {
        /// <summary>
        /// Returns the repository root above <paramref name="startDirectory"/> — the closest ancestor
        /// directory containing <paramref name="rootFileName"/>.
        /// </summary>
        /// <param name="startDirectory">The directory to start walking up from, typically <see cref="AppContext.BaseDirectory"/>.</param>
        /// <param name="rootFileName">The file whose presence marks the root, e.g. <c>MyProject.sln</c>.</param>
        /// <exception cref="DirectoryNotFoundException">If no directory containing <paramref name="rootFileName"/> is found walking up.</exception>
        public static string Find(string startDirectory, string rootFileName)
        {
            DirectoryInfo? directory = new DirectoryInfo(startDirectory);

            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, rootFileName)))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                $"Could not locate the repository root (no {rootFileName} found walking up from '{startDirectory}').");
        }
    }
}
