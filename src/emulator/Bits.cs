namespace OptimeGBA
{
    class Bits
    {
        public const uint BIT_0 = (1 << 0);
        public const uint BIT_1 = (1 << 1);
        public const uint BIT_2 = (1 << 2);
        public const uint BIT_3 = (1 << 3);
        public const uint BIT_4 = (1 << 4);
        public const uint BIT_5 = (1 << 5);
        public const uint BIT_6 = (1 << 6);
        public const uint BIT_7 = (1 << 7);
        public const uint BIT_8 = (1 << 8);
        public const uint BIT_9 = (1 << 9);
        public const uint BIT_10 = (1 << 10);
        public const uint BIT_11 = (1 << 11);
        public const uint BIT_12 = (1 << 12);
        public const uint BIT_13 = (1 << 13);
        public const uint BIT_14 = (1 << 14);
        public const uint BIT_15 = (1 << 15);
        public const uint BIT_16 = (1 << 16);
        public const uint BIT_17 = (1 << 17);
        public const uint BIT_18 = (1 << 18);
        public const uint BIT_19 = (1 << 19);
        public const uint BIT_20 = (1 << 20);
        public const uint BIT_21 = (1 << 21);
        public const uint BIT_22 = (1 << 22);
        public const uint BIT_23 = (1 << 23);
        public const uint BIT_24 = (1 << 24);
        public const uint BIT_25 = (1 << 25);
        public const uint BIT_26 = (1 << 26);
        public const uint BIT_27 = (1 << 27);
        public const uint BIT_28 = (1 << 28);
        public const uint BIT_29 = (1 << 29);
        public const uint BIT_30 = (1 << 30);

        public static bool BitTest(uint i, byte bit)
        {
            return (i & (1 << bit)) != 0;
        }

        public static uint BitSet(uint i, byte bit)
        {
            return (uint)(i | (uint)(1 << bit));
        }

        public static byte BitSet(byte i, byte bit)
        {
            return (byte)(i | (byte)(1 << bit));
        }

        public static uint BitRange(uint i, byte start, byte end)
        {
            return (i >> start) & (0xFFFFFFFF >> (31 - end));
        }

        // public bool bitReset(uint i, uint bit)
        // {
        //     return i & (~(1 << bit));
        // }

        // public bool bitSetValue(uint i, uint bit, bool value)
        // {
        //     if (value)
        //     {
        //         return i | (1 << bit);
        //     }
        //     else
        //     {
        //         return i & (~(1 << bit));
        //     }
        // }
    }
}