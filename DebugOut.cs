using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using System.IO;

namespace NBoardLocalGameServer
{
    internal static class DebugOut
    {
        static StreamWriter Out = StreamWriter.Null;

        public static void SetOutFile(string path, bool autoFlush = true)
        {
            Out.Close();
            Out = new StreamWriter(path) { AutoFlush = autoFlush };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Out.Close();

            // With autoFlush:false, buffered lines are otherwise lost on a crash — ProcessExit does not
            // fire for an unhandled-exception termination, only for a graceful one. Flushing here means
            // the log up to the moment of the crash is still on disk for post-mortem debugging.
            AppDomain.CurrentDomain.UnhandledException += (_, _) => Out.Flush();
        }

        public static void WriteLine(object obj)
        {
#if DEBUG
            Debug.WriteLine(obj);
#endif

            lock(Out)
                Out.WriteLine(obj);
        }

        public static void Flush() => Out.Flush();

        public static void Close() => Out.Close();
    }
}
