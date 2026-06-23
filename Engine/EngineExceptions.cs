using NBoardLocalGameServer.Reversi;
using System;

namespace NBoardLocalGameServer.Engine
{
    internal class EngineException(string msg, EngineProcessInfo? info = null, Exception? innerEx = null)
        : Exception(CreateBaseMessage(msg, info, innerEx))
    {
        public EngineProcessInfo? EngineProcessInfo { get; } = info;

        static string CreateBaseMessage(string msg, EngineProcessInfo? info, Exception? innerEx)
        {
            var engineDetail = (info is not null) ? $"Engine Detail: {info.Name} (PID: {info.Pid}) at \"{info.Path}\"" : string.Empty;
            var innerExMsg = (innerEx is not null) ? $"Inner Exception Message: {innerEx?.Message}\nInner Stack Trace:\n{innerEx?.StackTrace}" : string.Empty;
            return $"{msg}\n{engineDetail}\n{innerExMsg}";
        }
    }

    internal class EngineStartException(string path, Exception? innerEx = null)
        : EngineException($"Failed to start engine's process from \"{path}\".", info: null, innerEx);

    internal class EngineStoppedUnexpectedlyException(EngineProcessInfo info) : EngineException("Engine process has stopped unexpectedly.", info);

    internal class EngineConnectionException(EngineProcessInfo info, Exception? innerEx = null)
        : EngineException("Connection to engine was lost.", info, innerEx);

    internal class EngineReturnedInvalidMoveException(EngineProcessInfo info, string moveStr)
        : EngineException($"Engine returned ivnalid move string \"{moveStr}\".", info);


    internal class EngineReturnedIllegalMoveException(EngineProcessInfo info, BoardCoordinate move)
        : EngineException($"Engine returned move {move} but it was illegal.", info)
    {
        public BoardCoordinate Move { get; } = move;
    }
}