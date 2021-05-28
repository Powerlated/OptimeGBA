using static OptimeGBA.Bits;
using System;

namespace OptimeGBA
{
    public enum DmaStartTiming
    {
        Immediately = 0,
        VBlank = 1,
        HBlank = 2,
        Special = 3,
    }

    public enum DmaDestAddrCtrl
    {
        Increment = 0,
        Decrement = 1,
        Fixed = 2,
        IncrementReload = 3,
    }

    public enum DmaSrcAddrCtrl
    {
        Increment = 0,
        Decrement = 1,
        Fixed = 2,
        PROHIBITED = 3,
    }
}