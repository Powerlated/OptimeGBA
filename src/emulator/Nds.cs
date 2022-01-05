using System;
using static OptimeGBA.MemoryUtil;
using static OptimeGBA.Bits;
using static Util;

namespace OptimeGBA
{
    public unsafe sealed class Nds
    {
        public ProviderNds Provider;

        // ARM9 side
        public MemoryNds9 Mem9;
        public Arm7 Cpu9;
        public HwControlNds HwControl9;
        public DmaNds Dma9;
        public Timers Timers9;
        public Nds9Math Math;

        // ARM7 side
        public MemoryNds7 Mem7;
        public Arm7 Cpu7;
        public HwControlNds HwControl7;
        public Spi Spi;
        public NdsAudio Audio;
        public DmaNds Dma7;
        public Timers Timers7;

        public Scheduler Scheduler;

        public Cp15 Cp15;

        public CartridgeNds Cartridge;

        // Based off of EXMEMCNT ownership rules, there 1 is ARM7
        public Ipc[] Ipcs; // 0: ARM9 to ARM7, 1: ARM7 to ARM9

        public PpuNds Ppu;
        public PpuNds3D Ppu3D;

        public MemoryControlNds MemoryControl;

        public Keypad Keypad = new Keypad();

        public RtcNds Rtc;

        public byte[] MainRam = new byte[4194304];
        public byte[] SharedRam = new byte[32768];

        public int Arm9PendingTicks;

        public ulong Steps;

        public Nds(ProviderNds provider)
        {
            Provider = provider;
            Scheduler = new Scheduler();

            Ipcs = new Ipc[] {
                new Ipc(this, 0),
                new Ipc(this, 1),
            };

            Cp15 = new Cp15(this);

            Cartridge = new CartridgeNds(this);

            Ppu = new PpuNds(this, Scheduler);
            Ppu3D = new PpuNds3D(this, Scheduler);

            MemoryControl = new MemoryControlNds();

            Rtc = new RtcNds();

            // ARM9 Init
            Mem9 = new MemoryNds9(this, Provider);
            Cpu9 = new Arm7(StateChangeArm9, Mem9, true, true, Cp15);
            HwControl9 = new HwControlNds(Cpu9);
            Dma9 = new DmaNds(false, Mem9, HwControl9);
            Timers9 = new Timers(null, HwControl9, Scheduler, true, false);
            Math = new Nds9Math(this);
            Mem9.InitPageTables();
            Cpu9.InitFlushPipeline();
            Cpu9.SetVectorMode(true);
            // screw it 
            Cpu9.SetTimingsTable(
                Cpu9.Timing8And16,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            );
            Cpu9.SetTimingsTable(
                Cpu9.Timing32,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            );
            Cpu9.SetTimingsTable(
                Cpu9.Timing8And16InstrFetch,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            );
            Cpu9.SetTimingsTable(
                Cpu9.Timing32InstrFetch,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            );

            // ARM7 init
            Mem7 = new MemoryNds7(this, Provider);
            Spi = new Spi(this);
            Audio = new NdsAudio(this);
            Cpu7 = new Arm7(StateChangeArm7, Mem7, false, false, null);
            HwControl7 = new HwControlNds(Cpu7);
            Dma7 = new DmaNds(true, Mem7, HwControl7);
            Timers7 = new Timers(null, HwControl7, Scheduler, true, true);
            Mem7.InitPageTables();
            Cpu7.InitFlushPipeline();
            // screw it 
            Cpu7.SetTimingsTable(
                Cpu7.Timing8And16,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            );
            Cpu7.SetTimingsTable(
                Cpu7.Timing32,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            );
            Cpu7.SetTimingsTable(
                Cpu7.Timing8And16InstrFetch,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            );
            Cpu7.SetTimingsTable(
                Cpu7.Timing32InstrFetch,
                1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1
            );

#if UNSAFE
            Console.WriteLine("Starting in memory UNSAFE mode");
#else
            Console.WriteLine("Starting in memory SAFE mode");
#endif

            if (provider.DirectBoot)
            {
                var rom = provider.Rom;

                // Firmware init
                MemoryControl.SharedRamControl = 3;
                HwControl7.Postflg = 1;
                HwControl9.Postflg = 1;

                Cartridge.Slot1Enable = true;

                Cpu9.IRQDisable = true;
                Cpu9.FIQDisable = true;

                // Thanks Hydr8gon / fleroviux lol
                Mem7.Write16(0x4000184, 0x8501); // IPCFIFOCNT7
                Mem9.Write16(0x4000184, 0x8501); // IPCFIFOCNT9

                Cp15.TransferTo(0, 0x0005707D, 1, 0, 0); // CP15 Control
                Cp15.TransferTo(0, 0x0300000A, 9, 1, 0); // Data TCM base/size
                Cp15.TransferTo(0, 0x00000020, 9, 1, 1); // Instruction TCM size
                Mem9.Write8(0x4000247, 0x03); // WRAMCNT
                Mem9.Write16(0x4000304, 0x0001); // POWCNT1
                Mem7.Write16(0x4000504, 0x0200); // SOUNDBIAS

                Mem9.Write32(0x027FF800, 0x1FC2); // Chip ID 1
                Mem9.Write32(0x027FF804, 0x1FC2); // Chip ID 2
                Mem9.Write16(0x027FF850, 0x5835); // ARM7 BIOS CRC
                Mem9.Write16(0x027FF880, 0x0007); // Message from ARM9 to ARM7
                Mem9.Write16(0x027FF884, 0x0006); // ARM7 boot task
                Mem9.Write32(0x027FFC00, 0x1FC2); // Copy of chip ID 1
                Mem9.Write32(0x027FFC04, 0x1FC2); // Copy of chip ID 2
                Mem9.Write16(0x027FFC10, 0x5835); // Copy of ARM7 BIOS CRC
                Mem9.Write16(0x027FFC40, 0x0001); // Boot indicator

                Mem9.Write32(0x027FF864, 0);
                Mem9.Write32(0x027FF868, (uint)(GetUshort(Provider.Firmware, 0x20) << 3));

                Mem9.Write16(0x027FF874, GetUshort(Provider.Firmware, 0x26));
                Mem9.Write16(0x027FF876, GetUshort(Provider.Firmware, 0x04));

                // Copy in header
                if (rom.Length >= 0x170)
                {
                    for (uint i = 0; i < 0x170; i++)
                    {
                        Mem9.Write8(0x027FFE00 + i, rom[i]);
                    }
                }

                for (uint i = 0; i < 0x70; i++)
                {
                    Mem9.Write8(0x27FFC80 + i, Provider.Firmware[0x3FF00 + i]);
                }

                Mem9.Write32(0x027FF864, 0);
                Mem9.Write32(0x027FF868, (uint)(GetUshort(Provider.Firmware, 0x20) << 3));

                Mem9.Write16(0x027FF874, GetUshort(Provider.Firmware, 0x26));
                Mem9.Write16(0x027FF876, GetUshort(Provider.Firmware, 0x04));

                if (rom.Length >= 0x20)
                {
                    uint arm7RomOffset = GetUint(rom, 0x30);
                    uint arm7EntryAddr = GetUint(rom, 0x34);
                    uint arm7RamAddr = GetUint(rom, 0x38);
                    uint arm7Size = GetUint(rom, 0x3C);

                    // ROM offset is aligned by 0x1000
                    Console.WriteLine("ARM7 ROM Offset: " + Hex(arm7RomOffset, 8));
                    Console.WriteLine("ARM7 RAM Address: " + Hex(arm7RamAddr, 8));
                    Console.WriteLine("ARM7 Entry: " + Hex(arm7EntryAddr, 8));
                    Console.WriteLine("ARM7 Size: " + arm7Size);
                    for (uint i = 0; i < arm7Size; i++)
                    {
                        Mem7.Write8(arm7RamAddr + i, rom[arm7RomOffset + i]);
                    }
                    Cpu7.R[13] = 0x3002F7C;
                    Cpu7.SetModeReg(13, Arm7Mode.IRQ, 0x3003F80);
                    Cpu7.SetModeReg(13, Arm7Mode.SVC, 0x3003FC0);
                    Cpu7.R[12] = arm7EntryAddr;
                    Cpu7.R[14] = arm7EntryAddr;
                    Cpu7.R[15] = arm7EntryAddr;
                    Cpu7.InitFlushPipeline();

                    uint arm9RomOffset = GetUint(rom, 0x20);
                    uint arm9EntryAddr = GetUint(rom, 0x24);
                    uint arm9RamAddr = GetUint(rom, 0x28);
                    uint arm9Size = GetUint(rom, 0x2C);

                    Console.WriteLine("ARM9 ROM Offset: " + Hex(arm9RomOffset, 8));
                    Console.WriteLine("ARM9 RAM Address: " + Hex(arm9RamAddr, 8));
                    Console.WriteLine("ARM9 Entry: " + Hex(arm9EntryAddr, 8));
                    Console.WriteLine("ARM9 Size: " + arm9Size);
                    for (uint i = 0; i < arm9Size; i++)
                    {
                        Mem9.Write8(arm9RamAddr + i, rom[arm9RomOffset + i]);
                    }
                    Cpu9.R[13] = 0x380FD80;
                    Cpu9.SetModeReg(13, Arm7Mode.IRQ, 0x380FF80);
                    Cpu9.SetModeReg(13, Arm7Mode.SVC, 0x380FFC0);
                    Cpu9.R[12] = arm9EntryAddr;
                    Cpu9.R[14] = arm9EntryAddr;
                    Cpu9.R[15] = arm9EntryAddr;
                    Cpu9.InitFlushPipeline();
                }
            }
        }

        public uint Step()
        {
            Steps++;

            long beforeTicks = Scheduler.CurrentTicks;

            while (Scheduler.CurrentTicks < Scheduler.NextEventTicks)
            {
                // Running both CPUs at 1CPI at 32 MHz causes the firmware to loop the setup screen,
                // so don't do that when not debugging simple test ROMs
                // Cpu7.Execute();
                // Cpu9.Execute();
                // Scheduler.CurrentTicks += 1;

                // TODO: Proper NDS timings
                // TODO: Figure out a better way to implement halting
                uint ticks7 = 0;
                // Run 32 ARM7 instructions at a time, who needs tight synchronization
                const uint instrsAtATime = 32;
                if (!Cpu7.Halted)
                {
                    for (uint i = 0; i < instrsAtATime; i++)
                    {
                        if (!Cpu7.Halted)
                        {
                            ticks7 += Cpu7.Execute();
                        }
                        else
                        {
                            ticks7 += instrsAtATime;
                            break;
                        }
                    }
                }
                else
                {
                    ticks7 += instrsAtATime;
                }

                Arm9PendingTicks += (int)ticks7 * 2; // ARM9 runs at twice the speed of ARM7
                while (Arm9PendingTicks > 0)
                {
                    if (!Cpu9.Halted)
                    {
                        Arm9PendingTicks -= (int)Cpu9.Execute();
                    }
                    else
                    {
                        Arm9PendingTicks -= (int)(Scheduler.NextEventTicks - Scheduler.CurrentTicks) * 2;
                        break;
                    }
                }

                Ppu3D.Run();

                Scheduler.CurrentTicks += ticks7;
            }

            long current = Scheduler.CurrentTicks;
            long next = Scheduler.NextEventTicks;
            Scheduler.PopFirstEvent().Callback(current - next);

            return (uint)(Scheduler.CurrentTicks - beforeTicks);
        }

        public void DoNothing(long cyclesLate) { }

        public void Tick(uint cycles)
        {
            Scheduler.CurrentTicks += cycles;
        }

        public void HaltSkip(long cyclesOffset) { }

        // POWCNT1
        public bool EnableDisplay;
        public bool Enable2DEngineA;
        public bool Enable3DRenderingEngine;
        public bool Enable3DGeometryEngine;
        public bool Enable2DEngineB;
        public bool DisplaySwap;

        public byte ReadHwio8Arm9(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000304:
                    if (EnableDisplay) val = BitSet(val, 0);
                    if (Enable2DEngineA) val = BitSet(val, 1);
                    if (Enable3DRenderingEngine) val = BitSet(val, 2);
                    if (Enable3DGeometryEngine) val = BitSet(val, 3);
                    break;
                case 0x4000305:
                    if (Enable2DEngineB) val = BitSet(val, 1);
                    if (DisplaySwap) val = BitSet(val, 7);
                    break;
            }
            return val;
        }

        public void WriteHwio8Arm9(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000304:
                    EnableDisplay = BitTest(val, 0);
                    Enable2DEngineA = BitTest(val, 1);
                    Enable3DRenderingEngine = BitTest(val, 2);
                    Enable3DGeometryEngine = BitTest(val, 3);
                    break;
                case 0x4000305:
                    Enable2DEngineB = BitTest(val, 1);
                    DisplaySwap = BitTest(val, 7);
                    break;
            }
        }

        public void StateChangeArm9() { }

        // POWCNT2
        public bool EnableSpeakers;
        public bool EnableWifi;

        public byte ReadHwio8Arm7(uint addr)
        {
            byte val = 0;
            switch (addr)
            {
                case 0x4000304:
                    if (EnableSpeakers) val = BitSet(val, 0);
                    if (EnableWifi) val = BitSet(val, 1);
                    break;
            }
            return val;
        }

        public void WriteHwio8Arm7(uint addr, byte val)
        {
            switch (addr)
            {
                case 0x4000304:
                    EnableSpeakers = BitTest(val, 0);
                    EnableWifi = BitTest(val, 1);
                    break;
            }
        }

        public void StateChangeArm7() { }
    }
}