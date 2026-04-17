using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Serialization;

namespace MineraScope
{
    // 260416Codex: Keep mineral database file access out of Forms so UI changes do not drag storage logic with them.
    internal sealed class MineralDatabaseRepository
    {
        // 260416Codex: Reuse the serializer because the XML shape is fixed for the whole application.
        private static readonly XmlSerializer Serializer = new(typeof(SolidSolution[]));

        private readonly string _databasePath;
        private readonly string _originalDatabasePath;

        // 260416Codex: Resolve both database paths once so later callers can stay focused on workflow code.
        public MineralDatabaseRepository(string assemblyPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                throw new ArgumentException("Assembly path must not be empty.", nameof(assemblyPath));
            }

            _databasePath = Path.Combine(assemblyPath, "MineralDatabase.xml");
            _originalDatabasePath = Path.Combine(assemblyPath, "MineralDatabaseOriginal.xml");
        }

        // 260416Codex: Create the working XML lazily from the original seed file so callers only need one entry point.
        public void EnsureInitialized()
        {
            if (File.Exists(_databasePath))
            {
                return;
            }

            File.Copy(_originalDatabasePath, _databasePath);
        }

        // 260416Codex: Centralize XML loading so future Forms can share the same persistence behavior.
        public SolidSolution[] Load()
        {
            EnsureInitialized();

            using var stream = File.OpenRead(_databasePath);
            return (SolidSolution[])Serializer.Deserialize(stream)!;
        }

        // 260416Codex: Accept IEnumerable to keep callers flexible while persisting a stable array payload.
        public void Save(IEnumerable<SolidSolution> solutions)
        {
            using var stream = File.Create(_databasePath);
            Serializer.Serialize(stream, solutions.ToArray());
        }

        // 260416Codex: Expose reset explicitly so UI code can restore the seed data without duplicating file logic.
        public void Reset()
        {
            File.Copy(_originalDatabasePath, _databasePath, overwrite: true);
        }
    }
}
