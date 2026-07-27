using NBoardLocalGameServer.Engine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace NBoardLocalGameServer
{
    internal record PlayerConfig(string Path, string Arguments, string WorkDir, IReadOnlyList<string> InitialCommands)
    {
        public static PlayerConfig? Load(string path) => JsonSerializer.Deserialize<PlayerConfig>(File.ReadAllText(path));
        public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal class Player : IDisposable
    {
        public PlayerStats Stats { get; }
        public EnginePool EnginePool { get; }

        /// <summary>The engine's own self-reported NBoard name (or process name as fallback). Used for
        /// GGF game records (PB[]/PW[]), where the engine's own identification is the natural thing to
        /// record.</summary>
        public string Name => _engineRep.Name ?? _engineRep.ProcessInfo.Name;

        /// <summary>
        /// Name used for PlayerStats/MatchStats labels (stats.json, results UI). Defaults to Name, but
        /// callers that have a more specific identity for this player (e.g. the web layer's registered
        /// engine name) can override it — self-reported NBoard names can collide across different
        /// registrations of the same underlying engine binary, which makes results hard to read.
        /// </summary>
        public string DisplayName { get; }

        // Poolの中にある思考エンジンの代表.
        // 名前などの情報を取得するときはこれを経由する．
        NBoardEngine _engineRep;

        Player(PlayerStats stats, EnginePool enginePool, NBoardEngine engineRep, string displayName)
        {
            Stats = stats;
            EnginePool = enginePool;
            _engineRep = engineRep;
            DisplayName = displayName;
        }

        public void Dispose() => EnginePool.Dispose();

        public static async Task<Player> CreatePlayerAsync(PlayerConfig config, int poolSize, string? displayNameOverride = null)
        {
            var engines = new NBoardEngine[poolSize];
            for (var i = 0; i < engines.Length; i++)
                engines[i] = await NBoardEngine.RunAsync(config.Path, config.Arguments, config.WorkDir, config.InitialCommands);

            var pool = new EnginePool(engines);
            var engine = await pool.RentAsync();
            var engineName = (engine.Name is not null) ? engine.Name : engine.ProcessInfo.Name;
            pool.Return(engine);

            var displayName = displayNameOverride ?? engineName;
            return new Player(new PlayerStats(displayName), new EnginePool(engines), engine, displayName);
        }
    }
}
