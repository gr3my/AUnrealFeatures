using AUnrealFeatures.Hosting.Http.Actions;
using AUnrealFeatures.Hosting.Http.Interfaces;
using MimeTypes;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace AUnrealFeatures.Hosting.Http.Preprocessors.Default
{
    public sealed class HttpStaticFilesProcessor : IHttpRequestPreprocessor
    {
        private readonly string _basePath;
        private readonly bool _allowExtensionlessLookups;

        public HttpStaticFilesProcessor(string baseFolder, bool allowExtensionlessLookups = false)
        {
            _basePath = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, baseFolder));
            _allowExtensionlessLookups = allowExtensionlessLookups;
        }

        public async Task<HttpPreprocessorContainer> TryPreprocessRequest(IHttpRequest request, IHttpResponse response)
        {
            if (!TryGetFilePath(request.Uri, out var filePath))
            {
                return new HttpPreprocessorContainer
                {
                    actionResult = Results.NotFound(),
                    result = HttpPreprocessorResult.FAIL
                };
            }

            if (await FileExistsAsync(filePath))
                return await CreateSuccessResponseAsync(filePath);

            if (await DirectoryExistsAsync(filePath))
            {
                string indexFile = await FindIndexFileAsync(filePath);
                if (indexFile != null)
                    return await CreateSuccessResponseAsync(indexFile);
            }

            if (_allowExtensionlessLookups)
            {
                string fileWithExtension = await FindFileWithExtensionAsync(filePath);
                if (fileWithExtension != null)
                    return await CreateSuccessResponseAsync(fileWithExtension);
            }

            return new HttpPreprocessorContainer
            {
                actionResult = Results.NotFound(),
                result = HttpPreprocessorResult.FAIL
            };
        }

        private bool TryGetFilePath(string uri, out string filePath)
        {
            filePath = "";
            try
            {
                var pathOnly = uri.Split('?', '#')[0];
                var relative = Uri.UnescapeDataString(pathOnly).TrimStart('/', '\\');
                var candidate = Path.GetFullPath(Path.Combine(_basePath, relative));
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                var baseWithSeparator = _basePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!candidate.Equals(_basePath, comparison) && !candidate.StartsWith(baseWithSeparator, comparison))
                    return false;
                filePath = candidate;
                return true;
            }
            catch
            {
                return false;
            }
        }

#pragma warning disable CS8603 // Possible null reference return.
        private async Task<string> FindIndexFileAsync(string directoryPath)
        {
            var files = await Task.Run(() => Directory.GetFiles(directoryPath));
            return files.FirstOrDefault(file => Path.GetFileNameWithoutExtension(file).Equals("index", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<string> FindFileWithExtensionAsync(string filePath)
        {
            string directory = Path.GetDirectoryName(filePath);
            if (directory == null) return null;

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);

            var files = await Task.Run(() => Directory.GetFiles(directory));
            return files.FirstOrDefault(file => Path.GetFileNameWithoutExtension(file).Equals(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase));
#pragma warning restore CS8603 // Possible null reference return.
        }

        private async Task<HttpPreprocessorContainer> CreateSuccessResponseAsync(string filePath)
        {
            string mimeType = MimeTypeMap.GetMimeType(Path.GetExtension(filePath).TrimStart('.'));
            byte[] fileContent = await File.ReadAllBytesAsync(filePath);

            return new HttpPreprocessorContainer
            {
                actionResult = Results.Configurable(HttpStatusCode.OK, mimeType, fileContent),
                result = HttpPreprocessorResult.OK | HttpPreprocessorResult.STOP_AFTER
            };
        }

        private Task<bool> FileExistsAsync(string path) => Task.Run(() => File.Exists(path));
        private Task<bool> DirectoryExistsAsync(string path) => Task.Run(() => Directory.Exists(path));
    }
}