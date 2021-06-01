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
        public byte ReadHwio8(uint addr)
        {
            DateAndTime[0] = 0x04;
            DateAndTime[1] = 0x08;
            DateAndTime[2] = 0x19;
            DateAndTime[3] = 0x03;
            DateAndTime[4] = 0x01;
            DateAndTime[5] = 0x01;
            DateAndTime[6] = 0x01;

            return Rtc;
        }

        byte Rtc;
        byte Command;
        int BitsWritten;
        byte Status1;

        byte[] DateAndTime = new byte[7];

        RtcNdsState State;

        public void WriteHwio8(uint addr, byte val)
        {
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
                            }
                            break;
                        case RtcNdsState.CommandEntered:
                            if (!BitTest(val, 4)) // Read
                            {
                                val &= 0xFE; // Erase bit 0
                                int commandBits = (Command >> 1) & 0b111;
                                switch (commandBits)
                                {
                                    case 0: // Status 1
                                            // Console.WriteLine("status 1 read");
                                        val |= (byte)((Status1 >> BitsWritten) & 1);
                                        break;

                                    case 2: // Date & Time (7 bytes)
                                        int byteNum = BitsWritten / 8;
                                        int bitNum = (BitsWritten % 8);
                                        val |= (byte)((DateAndTime[byteNum] >> bitNum) & 1);
                                        break;

                                    case 3: // Time (3 bytes);
                                        throw new NotImplementedException("time");

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
        }
    }
}