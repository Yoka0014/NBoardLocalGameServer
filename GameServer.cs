using NBoardLocalGameServer.Engine;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace NBoardLocalGameServer
{
    internal class GameServerConfig
    {
        /// <summary>
        /// 1ゲームごとに手番を入れ替えるか
        /// </summary>
        public bool SwapPlayer { get; init; } = true;

        /// <summary>
        /// 手番を入れ替えたとき, 手番入れ替える前と同じ局面で再対局するか, もしくは別の局面を用意するか.
        /// SwapPlayerがtrueのときのみ有効.
        /// </summary>
        public bool UseSamePositionWhenSwapPlayer { get; init; } = true;

        /// <summary>
        /// 開始局面集のパス
        /// </summary>
        public string OpeningBookPath { get; set; } = string.Empty;

        public static GameServerConfig? Load(string path) => JsonSerializer.Deserialize<GameServerConfig>(File.ReadAllText(path));
    }

    internal class EnginePool
    {
        readonly Channel<NBoardEngine> _pool;

        public EnginePool(NBoardEngine[] engines)
        {
            _pool = Channel.CreateBounded<NBoardEngine>(new BoundedChannelOptions(engines.Length) { FullMode = BoundedChannelFullMode.Wait });
            foreach (var engine in engines)
                _pool.Writer.TryWrite(engine);
        }

        public async ValueTask<NBoardEngine> RentAsync(CancellationToken ct = default) => await _pool.Reader.ReadAsync(ct);

        public void Return(NBoardEngine engine) => _pool.Writer.TryWrite(engine);
    }

    internal record PlayerConfig(string Path, string Arguments, string WorkDir, IReadOnlyList<string> InitialCommands)
    {
        public static PlayerConfig? Load(string path) => JsonSerializer.Deserialize<PlayerConfig>(File.ReadAllText(path));
        public void Save(string path) => File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal class Player
    {
        public PlayerStats Stats { get; }
        public EnginePool EnginePool { get; }

        Player(PlayerStats stats, EnginePool enginePool)
        {
            Stats = stats;
            EnginePool = enginePool;
        }

        public static async Task<Player> CreatePlayerAsync(PlayerConfig config, int poolSize)
        {
            var engines = new NBoardEngine[poolSize];
            for (var i = 0; i < engines.Length; i++)
                engines[i] = await NBoardEngine.RunAsync(config.Path, config.Arguments, config.WorkDir, config.InitialCommands);

            var pool = new EnginePool(engines);
            var engine = await pool.RentAsync();
            var engineName = (engine.Name is not null) ? engine.Name : engine.ProcessInfo.Name;
            pool.Return(engine);

            return new Player(new PlayerStats(engineName), new EnginePool(engines));
        }
    }

    internal class GameServer(GameServerConfig config, PlayerConfig playerConfig0, PlayerConfig playerConfig1, int maxSessions)
    {
        readonly GameServerConfig _config = config;
        readonly PlayerConfig[] _playerConfigs = [playerConfig0, playerConfig1];
        readonly int _maxSessions = maxSessions;

        public GameServer(GameServerConfig config, PlayerConfig playerConfig0, PlayerConfig playerConfig1) : this(config, playerConfig0, playerConfig1, Environment.ProcessorCount) { }

        public async Task RunAsync(int numGames) 
        {
            var players = new Player[2];
            for (var i = 0; i < 2; i++)
                players[i] = await Player.CreatePlayerAsync(_playerConfigs[i], _maxSessions);


        }
    }
}
