using System;
using static OptimeGBA.MemoryUtil;
using static Util;

namespace OptimeGBA
{
    public unsafe sealed class Nds
    {
        public ProviderNds Provider;

        public Nds7 Nds7;
        public Nds9 Nds9;
        public Scheduler Scheduler;

        public Cp15 Cp15;

        public Ipc[] Ipcs; // 0: ARM7 to ARM9, 1: ARM9 to ARM7

        public PpuNds Ppu;

        public Keypad Keypad = new Keypad();

        public byte[] MainRam = new byte[4194304];
        public byte[] SharedRam = new byte[32768];
        public byte SharedRamControl = 0;

        public int Arm9PendingTicks;

        public Nds(ProviderNds provider)
        {
            Provider = provider;
            Scheduler = new Scheduler();
            // AudioCallback = provider.AudioCallback;

            Ipcs = new Ipc[] {
                new Ipc(this, 0),
                new Ipc(this, 1),
            };

            Cp15 = new Cp15(this);

            Ppu = new PpuNds(this, Scheduler);
            Ppu.Renderer.DisableColorCorrection();

            Nds7 = new Nds7(this) { Scheduler = Scheduler };
            Nds9 = new Nds9(this) { Scheduler = Scheduler };

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
                    uint arm7RomOffset = GetUint(rom, 0x30);
                    uint arm7EntryAddr = GetUint(rom, 0x34);
                    uint arm7RamAddr = GetUint(rom, 0x38);
                    uint arm7Size = GetUint(rom, 0x3C);

                    // Firmware init
                    SharedRamControl = 3;

                    // ROM offset is aligned by 0x1000
                    Console.WriteLine("ARM7 ROM Offset: " + Hex(arm7RomOffset, 8));
                    Console.WriteLine("ARM7 RAM Address: " + Hex(arm7RamAddr, 8));
                    for (uint i = 0; i < arm7Size; i++)
                    {
                        Nds7.Mem.Write8(arm7RamAddr + i, rom[arm7RomOffset + i]);
                    }
                    Nds7.Cpu.R[13] = 0x3002F7C;
                    Nds7.Cpu.R13irq = 0x3003F80;
                    Nds7.Cpu.R13svc = 0x3003FC0;
                    Nds7.Cpu.R[12] = arm7RamAddr;
                    Nds7.Cpu.R[14] = arm7RamAddr;
                    Nds7.Cpu.R[15] = arm7RamAddr;
                    Nds7.Cpu.FlushPipeline();

                    Console.WriteLine("ARM9 ROM Offset: " + Hex(arm9RomOffset, 8));
                    Console.WriteLine("ARM9 RAM Address: " + Hex(arm9RamAddr, 8));
                    for (uint i = 0; i < arm9Size; i++)
                    {
                        Nds9.Mem.Write8(arm9RamAddr + i, rom[arm9RomOffset + i]);
                    }
                    Nds9.Cpu.R[13] = 0x380FD80;
                    Nds9.Cpu.R13irq = 0x380FF80;
                    Nds9.Cpu.R13svc = 0x380FFC0;
                    Nds9.Cpu.R[12] = arm9RamAddr;
                    Nds9.Cpu.R[14] = arm9RamAddr;
                    Nds9.Cpu.R[15] = arm9RamAddr;
                    Nds9.Cpu.FlushPipeline();

                }
            }
        }

        public uint Step()
        {
            long beforeTicks = Scheduler.CurrentTicks;
            
            Nds7.Cpu.Execute();
            Nds9.Cpu.Execute();

            // TODO: Proper NDS timings
            // uint ticks7 = Nds7.Cpu.Execute();
            // Arm9PendingTicks += (int)ticks7 * 2; // ARM9 runs at twice the speed of ARM7
            // while (Arm9PendingTicks > 0) {
            //     Arm9PendingTicks -= (int)Nds9.Cpu.Execute();
            // }
            Scheduler.CurrentTicks += 4;

            while (Scheduler.CurrentTicks >= Scheduler.NextEventTicks)
            {
                long current = Scheduler.CurrentTicks;
                long next = Scheduler.NextEventTicks;
                Scheduler.PopFirstEvent().Callback(current - next);
            }

            return (uint)(Scheduler.CurrentTicks - beforeTicks);
        }

        public void DoNothing(long cyclesLate) { }

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