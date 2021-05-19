using System;
using static OptimeGBA.MemoryUtil;
using static Util;

namespace OptimeGBA
{
    public unsafe sealed class Nds
    {
        public ProviderNds Provider;

        public GbaAudio GbaAudio;
        public Keypad Keypad;

        public Nds7 Nds7;
        public Scheduler Scheduler;

        public byte[] MainRam = new byte[4194304];
        public byte[] SharedRam = new byte[32768];
        public byte SharedRamControl = 0;

        public Nds(ProviderNds provider)
        {
            Provider = provider;
            Scheduler = new Scheduler();
            // AudioCallback = provider.AudioCallback;

            Nds7 = new Nds7(this) { Scheduler = Scheduler };

#if UNSAFE
            Console.WriteLine("Starting in memory UNSAFE mode");
#else
            Console.WriteLine("Starting in memory SAFE mode");
#endif

            if (provider.DirectBoot)
            {
                var rom = provider.Rom;
                if (rom.Length >= 0x200)
                {
                    uint arm9RomOffset = GetUint(rom, 0x20);
                    uint arm9EntryAddr = GetUint(rom, 0x24);
                    uint arm9RamAddr = GetUint(rom, 0x28);
                    uint arm9Size = GetUint(rom, 0x2C);
                    uint arm7RomOffset = GetUint(rom, 0x30) & ~0xFFFu;
                    uint arm7EntryAddr = GetUint(rom, 0x34);
                    uint arm7RamAddr = GetUint(rom, 0x38);
                    uint arm7Size = GetUint(rom, 0x3C);

                    // Firmware init
                    SharedRamControl = 3;

                    // ROM offset is aligned by 0x1000
                    // Array.Copy(rom, arm9RomOffset & ~0xFFF, , arm9RamAddr, arm9Size);
                    Console.WriteLine("ARM7 RAM Address: " + Hex(arm7RamAddr, 8));
                    for (uint i = 0; i < arm7Size; i++)
                    {
                        Nds7.Mem.Write8(arm7RamAddr + i, rom[arm7RomOffset + i]);
                    }
                    Nds7.Cpu.R[15] = arm7RamAddr;
                    Nds7.Cpu.FlushPipeline();

                }
            }
        }

        public uint Step()
        {
            Nds7.Cpu.CheckInterrupts();
            long beforeTicks = Scheduler.CurrentTicks;
            if (!Nds7.Cpu.ThumbState)
            {
                Scheduler.CurrentTicks += Nds7.Cpu.ExecuteArm();
            }
            else
            {
                Scheduler.CurrentTicks += Nds7.Cpu.ExecuteThumb();
            }
            while (Scheduler.CurrentTicks >= Scheduler.NextEventTicks)
            {
                long current = Scheduler.CurrentTicks;
                long next = Scheduler.NextEventTicks;
                Scheduler.PopFirstEvent().Callback(current - next);
            }

            return (uint)(Scheduler.CurrentTicks - beforeTicks);
        }

        public void DoNothing(long cyclesLate) { }

        public uint StateStep()
        {
            Nds7.Cpu.CheckInterrupts();

            long beforeTicks = Scheduler.CurrentTicks;
            if (!Nds7.Cpu.ThumbState)
            {
                while (Scheduler.CurrentTicks < Scheduler.NextEventTicks)
                {
                    Scheduler.CurrentTicks += Nds7.Cpu.ExecuteArm();
                }
            }
            else
            {
                while (Scheduler.CurrentTicks < Scheduler.NextEventTicks)
                {
                    Scheduler.CurrentTicks += Nds7.Cpu.ExecuteThumb();
                }
            }

            while (Scheduler.CurrentTicks >= Scheduler.NextEventTicks)
            {
                long current = Scheduler.CurrentTicks;
                long next = Scheduler.NextEventTicks;
                Scheduler.PopFirstEvent().Callback(current - next);
            }

            // Return cycles executed
            return (uint)(Scheduler.CurrentTicks - beforeTicks);
        }

        public void Tick(uint cycles)
        {
            Scheduler.CurrentTicks += cycles;
        }

        public void StateChange()
        {
            Scheduler.AddEventRelative(SchedulerId.None, 0, DoNothing);
        }

        public void HaltSkip(long cyclesOffset) { }
    }
}