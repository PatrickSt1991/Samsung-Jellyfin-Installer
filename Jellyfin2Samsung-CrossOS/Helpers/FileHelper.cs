using Avalonia.Platform.Storage;
using Jellyfin2SamsungCrossOS.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Jellyfin2SamsungCrossOS.Helpers
{
    public class FileHelper
    {
        private static readonly string[] wgtItem = ["*.wgt"];
        private static readonly string[] tpkItem = ["*.tpk"];
        private static readonly string[] allItem = ["*.wgt", "*.tpk"];

        public async Task<string?> BrowseWgtFilesAsync(IStorageProvider storageProvider)
        {
            var fileTypes = new List<FilePickerFileType>
            {
                new("WGT Files")
                {
                    Patterns = wgtItem 
                },
                new("TPK Files")
                {
                    Patterns = tpkItem
                },
                new("All Supported Files")
                {
                    Patterns = allItem 
                }
            };

            var options = new FilePickerOpenOptions
            {
                Title = "Select WGT/TPK File",
                FileTypeFilter = fileTypes,
                AllowMultiple = true
            };

            var files = await storageProvider.OpenFilePickerAsync(options);

            if (files?.Any() == true)
            {
                var newPaths = new List<string>();

                foreach (var file in files)
                {
                    var originalPath = file.Path.LocalPath;
                    var directory = Path.GetDirectoryName(originalPath);
                    var baseName = Path.GetFileNameWithoutExtension(originalPath);
                    var extension = Path.GetExtension(originalPath);

                    var random = new Random();
                    const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
                    var randomSuffix = new string(Enumerable.Range(0, 4).Select(_ => chars[random.Next(chars.Length)]).ToArray());

                    var newFileName = $"{baseName}{randomSuffix}{extension}";
                    var newFilePath = Path.Combine(directory ?? Environment.CurrentDirectory, newFileName);

                    File.Copy(originalPath, newFilePath, overwrite: true);

                    newPaths.Add(newFilePath);
                }

                return string.Join(";", newPaths);
            }

            return null;
        }
        public List<ExtensionEntry> ParseExtensions(string output)
        {
            var extensions = new List<ExtensionEntry>();
            var regex = new Regex(
                @"Index\s*:\s*(\d+)\s+Name\s*:\s*(.*?)\s+Repository\s*:\s*.*?\s+Id\s*:\s*.*?\s+Vendor\s*:\s*.*?\s+Description\s*:\s*.*?\s+Default\s*:\s*.*?\s+Activate\s*:\s*(true|false)",
                RegexOptions.Singleline);

            foreach (Match match in regex.Matches(output))
            {
                extensions.Add(new ExtensionEntry
                {
                    Index = int.Parse(match.Groups[1].Value),
                    Name = match.Groups[2].Value.Trim(),
                    Activated = bool.Parse(match.Groups[3].Value)
                });
            }

            return extensions;
        }
    }

}
