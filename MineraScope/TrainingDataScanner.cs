using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MineraScope
{
    // 260416Codex: Move training-data folder discovery out of Forms so the same scan logic can back any generator UI.
    internal sealed class TrainingDataScanner
    {
        private readonly Func<string, MineralFolder?> _analyzeMineralFolder;

        // 260416Codex: Inject the mineral-folder analyzer so this scanner stays focused on directory traversal only.
        public TrainingDataScanner(Func<string, MineralFolder?> analyzeMineralFolder)
        {
            _analyzeMineralFolder = analyzeMineralFolder ?? throw new ArgumentNullException(nameof(analyzeMineralFolder));
        }

        // 260416Codex: Return plain mineral names so UI code can bind the result without caring about the scan details.
        public string[] Scan(string trainingPath)
        {
            if (!Directory.Exists(trainingPath))
            {
                return [];
            }

            if (Directory.EnumerateFiles(trainingPath, "*.*").Any(IsSpectrumFile))
            {
                return [GetFolderName(trainingPath)];
            }

            var mineralNames = new List<string>();
            foreach (var mineralFolder in Directory.GetDirectories(trainingPath))
            {
                var mineralInfo = _analyzeMineralFolder(mineralFolder);
                if (mineralInfo is null)
                {
                    continue;
                }

                mineralNames.Add(GetFolderName(mineralFolder));
            }

            return mineralNames.ToArray();
        }

        // 260416Codex: Normalize trailing separators so a manually entered path still produces a stable folder name.
        private static string GetFolderName(string path)
        {
            var trimmedPath = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFileName(trimmedPath);
        }

        // 260416Codex: Keep spectrum-file detection in one place because both root and subfolder scans use the same rule.
        private static bool IsSpectrumFile(string path) =>
            path.EndsWith(".msa", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".emsa", StringComparison.OrdinalIgnoreCase);
    }
}
