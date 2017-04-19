using System;

namespace PerfAlgorithms
{
    public enum HillClimbingStateTransition
    {
        Warmup,
        Initializing,
        RandomMove,
        ClimbingMove,
        ChangePoint,
        Stabilizing,
        Starvation, //used by ThreadpoolMgr
        ThreadTimedOut, //used by ThreadpoolMgr
        Undefined,
    }
}