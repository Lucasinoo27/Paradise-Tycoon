using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace ParadiseTycoon.GameState
{
    /// <summary>Version 1 stores complete checkpoints between waves; active waves need a future schema.</summary>
    public sealed class JsonGameStateStore : IGameStateStore
    {
        public const int FormatVersion = 1;
        readonly string path;
        readonly GameConfiguration configuration;
        public string LastLoadWarning { get; private set; }

        [Serializable]
        sealed class Envelope { public int formatVersion; public GameStateData state; }

        public JsonGameStateStore(string path, GameConfiguration configuration)
        {
            this.path = Path.GetFullPath(path ?? throw new ArgumentNullException(nameof(path)));
            this.configuration = configuration.Copy();
        }

        public GameStateData Load()
        {
            LastLoadWarning = null;
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return null;
            try { return Read(path); }
            catch (Exception primary) when (primary is IOException || primary is InvalidDataException)
            {
                if (!File.Exists(path + ".bak")) throw;
                var recovered = Read(path + ".bak");
                LastLoadWarning = "Recovered the previous checkpoint: " + primary.Message;
                return recovered;
            }
        }

        GameStateData Read(string source)
        {
            Envelope envelope;
            try { envelope = JsonUtility.FromJson<Envelope>(File.ReadAllText(source, Encoding.UTF8)); }
            catch (ArgumentException exception) { throw new InvalidDataException("Malformed game save.", exception); }
            if (envelope == null || envelope.formatVersion < 1) throw new InvalidDataException("Missing save format version.");
            // Do not replace a save written by a newer game with an older backup.
            if (envelope.formatVersion != FormatVersion) throw new ArgumentException("Unsupported game save version: " + envelope.formatVersion);
            if (envelope.state == null) throw new InvalidDataException("Missing game state.");
            // Reuse the domain validator, including building ids/levels and reward deduplication state.
            var validator = new GameStateService(configuration, new SingleSnapshotStore(envelope.state));
            var result = validator.TryLoad();
            if (!result.Succeeded) throw new InvalidDataException(result.Message);
            return validator.Snapshot();
        }

        public void Save(GameStateData state)
        {
            if (state == null || state.wavePhase != WavePhase.BetweenWaves) throw new InvalidDataException("Only between-wave checkpoints may be saved.");
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(new Envelope { formatVersion = FormatVersion, state = state }, true));
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                // Same-directory replacement is atomic: readers see either the previous or the new whole transaction.
                if (File.Exists(path)) File.Replace(temporary, path, path + ".bak");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        sealed class SingleSnapshotStore : IGameStateStore
        {
            readonly GameStateData state;
            public SingleSnapshotStore(GameStateData state) { this.state = state; }
            public GameStateData Load() => state;
            public void Save(GameStateData snapshot) { }
        }
    }
}
