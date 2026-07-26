using EasyReasy.Database.Testing;

namespace EasyReasy.Database.Tests.Testing
{
    public class RepositoryRootTests
    {
        [Fact]
        public void Find_FromTheTestBinaries_FindsTheDirectoryContainingTheRootFile()
        {
            string root = RepositoryRoot.Find(AppContext.BaseDirectory, "EasyReasy.Database.sln");

            Assert.True(File.Exists(Path.Combine(root, "EasyReasy.Database.sln")));
        }

        [Fact]
        public void Find_WhenNoAncestorContainsTheRootFile_ThrowsNamingBothInputs()
        {
            DirectoryNotFoundException exception = Assert.Throws<DirectoryNotFoundException>(
                () => RepositoryRoot.Find(Path.GetTempPath(), "DoesNotExistAnywhere.sln"));

            Assert.Contains("DoesNotExistAnywhere.sln", exception.Message);
            Assert.Contains(Path.GetTempPath(), exception.Message);
        }
    }
}
