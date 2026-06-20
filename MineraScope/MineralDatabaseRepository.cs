using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace MineraScope
{
    // 260416Codex: Keep mineral database file access out of Forms so UI changes do not drag storage logic with them.
    internal sealed class MineralDatabaseRepository
    {
        // 260416Codex: Reuse the serializer because the XML shape is fixed for the whole application.
        private static readonly XmlSerializer Serializer = new(typeof(SolidSolution[]));
        // 260617Codex: Local-only bulk mineral entry mode; keep seed XML diffs close to the hand-maintained file.
        private static readonly XmlWriterSettings WriterSettings = new()
        {
            Indent = true,
            IndentChars = "\t",
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        };

        private readonly string _databasePath;
        private readonly string _originalDatabasePath;
        private readonly string? _sourceOriginalDatabasePath;

        // 260416Codex: Resolve both database paths once so later callers can stay focused on workflow code.
        public MineralDatabaseRepository(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                throw new ArgumentException("Assembly path must not be empty.", nameof(assemblyPath));

            _databasePath = Path.Combine(assemblyPath, "MineralDatabase.xml");
            _originalDatabasePath = Path.Combine(assemblyPath, "MineralDatabaseOriginal.xml");
            _sourceOriginalDatabasePath = ResolveSourceOriginalDatabasePath(assemblyPath);
        }

        // 260416Codex: Create the working XML lazily from the original seed file so callers only need one entry point.
        public void EnsureInitialized()
        {
            if (_sourceOriginalDatabasePath is not null)
            {
                File.Copy(_sourceOriginalDatabasePath, _originalDatabasePath, overwrite: true);
                if (!File.Exists(_databasePath))
                    File.Copy(_sourceOriginalDatabasePath, _databasePath);

                return;
            }

            if (File.Exists(_databasePath))
                return;

            File.Copy(_originalDatabasePath, _databasePath);
        }

        // 260416Codex: Centralize XML loading so future Forms can share the same persistence behavior.
        public SolidSolution[] Load()
        {
            EnsureInitialized();

            using var stream = File.OpenRead(_sourceOriginalDatabasePath ?? _databasePath);
            return (SolidSolution[])Serializer.Deserialize(stream)!;
        }

        // 260416Codex: Accept IEnumerable to keep callers flexible while persisting a stable array payload.
        public void Save(IEnumerable<SolidSolution> solutions)
        {
            var payload = solutions.ToArray();
            SaveTo(_databasePath, payload);

            if (_sourceOriginalDatabasePath is not null)
            {
                SaveTo(_originalDatabasePath, payload);
                SaveTo(_sourceOriginalDatabasePath, payload);
            }
        }

        // 260416Codex: Expose reset explicitly so UI code can restore the seed data without duplicating file logic.
        public void Reset()
        {
            if (_sourceOriginalDatabasePath is not null)
            {
                File.Copy(_sourceOriginalDatabasePath, _originalDatabasePath, overwrite: true);
                File.Copy(_sourceOriginalDatabasePath, _databasePath, overwrite: true);
                return;
            }

            File.Copy(_originalDatabasePath, _databasePath, overwrite: true);
        }

        private static void SaveTo(string path, SolidSolution[] solutions)
        {
            using var writer = XmlWriter.Create(path, WriterSettings);
            Serializer.Serialize(writer, solutions);
        }

        private static string? ResolveSourceOriginalDatabasePath(string assemblyPath)
        {
            for (var directory = new DirectoryInfo(assemblyPath); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "MineralDatabaseOriginal.xml");
                var projectFile = Path.Combine(directory.FullName, "MineraScope.csproj");
                if (File.Exists(candidate) && File.Exists(projectFile))
                    return candidate;
            }

            return null;
        }
    }
}
