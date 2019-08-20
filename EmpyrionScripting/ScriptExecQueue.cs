﻿using EmpyrionNetAPIDefinitions;
using EmpyrionScripting.DataWrapper;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace EmpyrionScripting
{
    public class ScriptExecQueue
    {
        public class ScriptInfo
        {
            public int Count { get; set; }
            public DateTime LastStart { get; set; }
            public TimeSpan ExecTime { get; set; }
        }
        public ConcurrentDictionary<string, ScriptInfo> ScriptRunInfo { get; } = new ConcurrentDictionary<string, ScriptInfo>();

        private Action<IScriptRootData> processScript;

        public static int Iteration => _Iteration;
        private static int _Iteration;

        public int MainCount => _MainCount;
        private int _MainCount;

        public DateTime LastIterationUpdate { get; set; }

        public ConcurrentDictionary<string, IScriptRootData> WaitForExec { get; } = new ConcurrentDictionary<string, IScriptRootData>();
        public ConcurrentQueue<IScriptRootData>              ExecQueue { get; private set; } = new ConcurrentQueue<IScriptRootData>();

        public ScriptExecQueue(Action<IScriptRootData> processScript)
        {
            this.processScript = processScript;
        }

        public void Add(IScriptRootData data)
        {
            if (WaitForExec.TryAdd(data.ScriptId, data)) ExecQueue.Enqueue(data);
            else lock (ExecQueue) WaitForExec.AddOrUpdate(data.ScriptId, data, (i, d) => data);
        }

        static public Action<string, LogLevel> Log { get; set; }
        public int ScriptsCount { get; set; }

        public bool ExecNext()
        {
            var              found = false;
            IScriptRootData  data  = null;
            lock (ExecQueue) found = ExecQueue.TryDequeue(out data);
            if (!found) return false;

            if (!ThreadPool.QueueUserWorkItem(ExecScript, data))
            {
                Log($"EmpyrionScripting Mod: ExecNext NorThreadPoolFree {data.ScriptId}", LogLevel.Debug);
                return false;
            }
            return true;
        }

        private void ExecScript(object state)
        {
            if (!(state is IScriptRootData data)) return;

            try
            {
                if (data.E.EntityType == EntityType.Proxy)
                {
                    WaitForExec.TryRemove(data.ScriptId, out _);
                    return;
                }

                if (!ScriptRunInfo.TryGetValue(data.ScriptId, out var info)) info = new ScriptInfo();

                info.LastStart = DateTime.Now;
                info.Count++;

                processScript(data);
                lock (ExecQueue) WaitForExec.TryRemove(data.ScriptId, out _);

                info.ExecTime += DateTime.Now - info.LastStart;

                ScriptRunInfo.AddOrUpdate(data.ScriptId, info, (id, i) => info);

                Interlocked.Increment(ref _MainCount);
                if(MainCount > ScriptsCount)
                {
                    if (Interlocked.Exchange(ref _MainCount, 0) > 0 && (DateTime.Now - LastIterationUpdate).TotalSeconds >= 1)
                    {
                        LastIterationUpdate = DateTime.Now;
                        Interlocked.Increment(ref _Iteration);
                    }
                }
            }
            catch (Exception error)
            {
                Log($"EmpyrionScripting Mod: ExecNext {data.ScriptId} => {error}", LogLevel.Debug);
            }
        }

        public void Clear()
        {
            ScriptRunInfo.Clear();
            WaitForExec.Clear();
            ExecQueue = new ConcurrentQueue<IScriptRootData>();
        }
    }
}