using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace NBoardLocalGameServer.Engine
{
    internal class EnginePool : IDisposable
    {
        const int QuitTimeoutMs = 10000;

        readonly NBoardEngine[] _engines;
        readonly Channel<NBoardEngine> _pool;

        public EnginePool(NBoardEngine[] engines)
        {
            _engines = engines;
            _pool = Channel.CreateBounded<NBoardEngine>(new BoundedChannelOptions(engines.Length) { FullMode = BoundedChannelFullMode.Wait });
            foreach (var engine in engines)
                _pool.Writer.TryWrite(engine);
        }

        public void Dispose()
        {
            Parallel.ForEach(_engines, engine =>
            {
                if (!engine.TryQuit(QuitTimeoutMs))
                    engine.TryQuitForcely(QuitTimeoutMs);
            });
        }

        public async ValueTask<NBoardEngine> RentAsync(CancellationToken ct = default) => await _pool.Reader.ReadAsync(ct);

        public void Return(NBoardEngine engine) => _pool.Writer.TryWrite(engine);
    }
}
