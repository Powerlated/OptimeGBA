using System;
using static OptimeGBA.Bits;

namespace OptimeGBA
{

    public enum RtcNdsState
    {
        ReceivingCommand,
        CommandEntered
    }

    public class RtcNds
    {
        public void UpdateTime()
        {
            var now = DateTime.Now;

            DateAndTime[0] = ConvertToBcd((byte)(now.Year % 100));
            DateAndTime[1] = ConvertToBcd((byte)now.Month);
            DateAndTime[2] = ConvertToBcd((byte)now.Day);
            DateAndTime[3] = ConvertToBcd((byte)now.DayOfWeek);
            DateAndTime[4] = ConvertToBcd((byte)now.Hour);
            DateAndTime[5] = ConvertToBcd((byte)now.Minute);
            DateAndTime[6] = ConvertToBcd((byte)now.Second);
        }

        public static byte ConvertToBcd(byte val)
        {
            uint upper = val / 10U;
            uint lower = val % 10U;

            return (byte)((upper << 4) | lower);
        }

        public byte ReadHwio8(uint addr)
        {
            switch (addr)
            {
                case 0x4000138:
                    return Rtc;
            }

            return 0;
        }

        byte Rtc;
        byte Command;
        int BitsWritten;
        byte Status1;

        byte[] DateAndTime = new byte[7];

        RtcNdsState State;

        public void WriteHwio8(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000138:
                    if (BitTest(val, 2)) // CS
                    {
                        if (BitTest(Rtc, 1) && !BitTest(val, 1)) // /SC to low
                        {
                            switch (State)
                            {
                                case RtcNdsState.ReceivingCommand:
                                    Command |= (byte)((val & 1) << (7 - BitsWritten));
                                    if (++BitsWritten == 8)
                                    {
                                        State = RtcNdsState.CommandEntered;
                                        // Console.WriteLine("RTC: command set " + Util.Hex(Command, 2));
                                        BitsWritten = 0;

                                        switch ((Command >> 1) & 0b111)
                                        {
                                            case 0:
                                                // Console.WriteLine("RTC status 1");
                                                break;

                                            case 1:
                                                // Console.WriteLine("RTC status 2");
                                                break;

                                            case 2:
                                                // Console.WriteLine("RTC date and time");
                                                UpdateTime();
                                                break;

                                            case 3:
                                                // Console.WriteLine("RTC time");
                                                UpdateTime();
                                                break;
                                        }
                                    }
                                    break;
                                case RtcNdsState.CommandEntered:
                                    if (!BitTest(val, 4)) // Read
                                    {
                                        val &= 0xFE; // Erase bit 0
                                        int commandBits = (Command >> 1) & 0b111;

                                        int byteNum = BitsWritten / 8;
                                        int bitNum = (BitsWritten % 8);
                                        switch (commandBits)
                                        {
                                            case 0: // Status 1
                                                    // Console.WriteLine("status 1 read");
                                                val |= (byte)((Status1 >> BitsWritten) & 1);
                                                break;

                                            case 2: // Date & Time (7 bytes)
                                                val |= (byte)((DateAndTime[byteNum] >> bitNum) & 1);
                                                break;

                                            case 3: // Time (3 bytes);
                                                val |= (byte)((DateAndTime[byteNum + 4] >> bitNum) & 1);
                                                break;

                                            default:
                                                // Console.WriteLine("RTC: unknown command read " + commandBits);
                                                break;
                                        }
                                    }
                                    else
                                    {
                                        byte bit = (byte)(val & 1U);
                                        switch ((Command >> 1) & 0b111)
                                        {
                                            case 0: // Status 1
                                                if (BitsWritten >= 1 && BitsWritten <= 3)
                                                {
                                                    // Console.WriteLine("status 1 write ");
                                                    Status1 &= (byte)(~(1 << BitsWritten));
                                                    Status1 |= (byte)(bit << BitsWritten);
                                                }
                                                break;
                                        }
                                    }
                                    BitsWritten++;
                                    break;
                            }
                        }
                        else if (!BitTest(Rtc, 1) && BitTest(val, 1) && !BitTest(val, 4))
                        {
                            val &= 0xFE;
                            val |= (byte)(Rtc & 1);
                        }
                    }
                    else
                    {
                        Command = 0;
                        BitsWritten = 0;
                        State = RtcNdsState.ReceivingCommand;
                    }

                    Rtc = val;
                    break;
            }
        }
    }
}