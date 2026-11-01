using System;
using System.IO;
using System.Threading.Tasks;
using YARG.Core.Logging;

namespace YARG.Helpers
{
    /// <summary>
    /// Content that is updated at runtime by extracting a downloaded repository archive. Updates are written to a
    /// writable download folder; a read-only seed folder (such as one baked into StreamingAssets, which is not
    /// writable in packaged installs) is used until the download folder holds a complete update.
    /// </summary>
    public class SeededDownloadFolder
    {
        private const string VERSION_FILE = "version.txt";

        private readonly string _repoFolderName;

        public string SeedFolder { get; }
        public string DownloadFolder { get; }

        public string DownloadRepoDirectory => Path.Combine(DownloadFolder, _repoFolderName);

        /// <summary>
        /// Written last when an update completes. Delete it before touching <see cref="DownloadRepoDirectory"/>
        /// so that an interrupted update is not mistaken for a complete one.
        /// </summary>
        public string DownloadVersionPath => Path.Combine(DownloadFolder, VERSION_FILE);

        /// <summary>
        /// The download folder if it holds a complete update, otherwise the seed folder.
        /// </summary>
        public string ActiveFolder => IsComplete(DownloadFolder) ? DownloadFolder : SeedFolder;

        public string ActiveRepoDirectory => Path.Combine(ActiveFolder, _repoFolderName);

        public SeededDownloadFolder(string seedFolder, string downloadFolder, string repoFolderName)
        {
            SeedFolder = seedFolder;
            DownloadFolder = downloadFolder;
            _repoFolderName = repoFolderName;
        }

        /// <summary>
        /// The version of the active folder's content, or null if it is unknown or the content is missing.
        /// </summary>
        public async Task<string> ReadActiveVersionAsync()
        {
            var folder = ActiveFolder;
            if (!IsComplete(folder))
            {
                return null;
            }

            var versionPath = Path.Combine(folder, VERSION_FILE);
            try
            {
                return await File.ReadAllTextAsync(versionPath);
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, $"Failed to read version file `{versionPath}`.");
                return null;
            }
        }

        /// <summary>
        /// Creates the download folder. Returns false, after logging, if it cannot be created.
        /// </summary>
        public bool TryCreateDownloadFolder()
        {
            try
            {
                Directory.CreateDirectory(DownloadFolder);
                return true;
            }
            catch (Exception e)
            {
                YargLogger.LogException(e, $"Failed to create download folder `{DownloadFolder}`.");
                return false;
            }
        }

        private bool IsComplete(string folder)
        {
            return File.Exists(Path.Combine(folder, VERSION_FILE))
                && Directory.Exists(Path.Combine(folder, _repoFolderName));
        }
    }
}
