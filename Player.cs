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
        public string Name => _engineRep.Name ?? _engineRep.ProcessInfo.Name;

        // Poolの中にある思考エンジンの代表.
        // 名前などの情報を取得するときはこれを経由する．
        NBoardEngine _engineRep;

        Player(PlayerStats stats, EnginePool enginePool, NBoardEngine engineRep)
        {
            Stats = stats;
            EnginePool = enginePool;
            _engineRep = engineRep;
        }

        public void Dispose() => EnginePool.Dispose();

        public static async Task<Player> CreatePlayerAsync(PlayerConfig config, int poolSize)
        {
            var engines = new NBoardEngine[poolSize];
            for (var i = 0; i < engines.Length; i++)
                engines[i] = await NBoardEngine.RunAsync(config.Path, config.Arguments, config.WorkDir, config.InitialCommands);

            var pool = new EnginePool(engines);
            var engine = await pool.RentAsync();
            var engineName = (engine.Name is not null) ? engine.Name : engine.ProcessInfo.Name;
            pool.Return(engine);

            return new Player(new PlayerStats(engineName), new EnginePool(engines), engine);
        }
    }
}
