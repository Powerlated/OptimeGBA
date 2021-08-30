using System;
using static OptimeGBA.Bits;
using static OptimeGBA.CoreUtil;
using static Util;
using System.Runtime.CompilerServices;
using System.Diagnostics;

namespace OptimeGBA
{
    public struct Command
    {
        public byte Cmd;
        public uint Param;

        public Command(byte cmd, uint param)
        {
            Cmd = cmd;
            Param = param;
        }
    }

    public enum PrimitiveType
    {
        Tris,
        Quads,
        TriStrips,
        QuadStrips
    }

    public unsafe struct Matrix
    {
        public fixed int Data[16];

        public static Matrix GetIdentity()
        {
            var m = new Matrix();

            m.Data[0] = 0x00001000;
            m.Data[5] = 0x00001000;
            m.Data[10] = 0x00001000;
            m.Data[15] = 0x00001000;

            return m;
        }

        public Matrix Multiply(Matrix a)
        {
            var m = new Matrix();

            for (int i = 0; i < 16; i++)
            {
                long sum = 0;

                int ai = i & ~3; // Matrix A row
                int bi = i & 3; // Matrix B column

                for (int j = 0; j < 4; j++)
                {
                    sum += (long)a.Data[ai] * Data[bi];
                    ai++;
                    bi += 4;
                }

                // Trim multiplied fixed point digits off
                m.Data[i] = (int)(sum >> 12);
            }

            return m;
        }

        public Vector Multiply(Vector a)
        {
            var v = new Vector();

            for (int i = 0; i < 4; i++)
            {
                long sum = 0;

                for (int j = 0; j < 4; j++)
                {
                    sum += (long)Data[j * 4 + i] * a.Data[j];
                }

                // Trim multiplied fixed point digits off
                v.Data[i] = (int)(sum >> 12);
            }

            return v;
        }

        public void Print(string s)
        {
            PpuNds3D.Debug(s);
            PpuNds3D.Debug($"{HexN((uint)Data[0x0], 8)} {HexN((uint)Data[0x1], 8)} {HexN((uint)Data[0x2], 8)} {HexN((uint)Data[0x3], 8)}");
            PpuNds3D.Debug($"{HexN((uint)Data[0x4], 8)} {HexN((uint)Data[0x5], 8)} {HexN((uint)Data[0x6], 8)} {HexN((uint)Data[0x7], 8)}");
            PpuNds3D.Debug($"{HexN((uint)Data[0x8], 8)} {HexN((uint)Data[0x9], 8)} {HexN((uint)Data[0xA], 8)} {HexN((uint)Data[0xB], 8)}");
            PpuNds3D.Debug($"{HexN((uint)Data[0xC], 8)} {HexN((uint)Data[0xD], 8)} {HexN((uint)Data[0xE], 8)} {HexN((uint)Data[0xF], 8)}");
        }
    }

    public unsafe struct Vector
    {
        public fixed int Data[4];

        public void Print(string s)
        {
            PpuNds3D.Debug(s);
            PpuNds3D.Debug($"{HexN((uint)Data[0x0], 8)} {HexN((uint)Data[0x1], 8)} {HexN((uint)Data[0x2], 8)} {HexN((uint)Data[0x3], 8)}");
        }
    }

    public unsafe struct Vertex
    {
        public Vector Pos;
        public fixed byte Color[3];
    }

    public class MatrixStack
    {
        public Matrix[] Stack;
        public Matrix Current;

        public sbyte Sp;
        public sbyte SpMask;

        public MatrixStack(uint size, sbyte spMask)
        {
            Stack = new Matrix[size];
            SpMask = spMask;
        }

        public void Push()
        {
            // Debug("Matrix push");
            if ((Sp & 31) != 31)
            {
                Stack[Sp & 31] = Current;
            }
            Sp++;
            Sp &= SpMask;
        }

        public void Pop(sbyte offset)
        {
            // Debug("Matrix pop");
            Sp -= offset;
            Sp &= SpMask;
            if ((Sp & 31) != 31)
            {
                Current = Stack[Sp & 31];
            }
        }

        public void Store(byte addr)
        {
            Stack[addr] = Current;
        }

        public void Restore(byte addr)
        {
            Current = Stack[addr];
        }
    }

    public enum MatrixMode
    {
        Projection,
        Position,
        PositionDirection,
        Texture
    }

    public sealed unsafe class PpuNds3D
    {
        Nds Nds;
        Scheduler Scheduler;

        public PpuNds3D(Nds nds, Scheduler scheduler)
        {
            Nds = nds;
            Scheduler = scheduler;
        }

        public static readonly byte[] CommandParamLengths = new byte[256] { 
            /* 0x00 No operation          */ 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
            /* 0x10 Matrix ops            */ 1, 0, 1, 1, 1, 0, 16, 12, 16, 12, 9, 3, 3, 0, 0, 0,
            /* 0x20 Vertex data           */ 1, 1, 1, 2, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 
            /* 0x30 Lighting              */ 1, 1, 1, 1, 32, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 
            /* 0x40 Begin/end vertex list */ 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            /* 0x50 Swap buffers          */ 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            /* 0x60 Set viewport          */ 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            /* 0x70 Tests                 */ 3, 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        };


        // Screen
        public ushort[] Screen = new ushort[256 * 192];

        // GXSTAT
        public byte CommandFifoIrqMode;

        // GXFIFO
        public CircularBuffer<byte> PackedCommandQueue = new CircularBuffer<byte>(4, 0);
        public int PackedParamsQueued = 0;
        public CircularBuffer<Command> CommandFifo = new CircularBuffer<Command>(256, new Command());

        // GPU State
        public MatrixMode MatrixMode;
        public MatrixStack ProjectionStack = new MatrixStack(1, 0);
        public MatrixStack PositionStack = new MatrixStack(31, 63);
        public MatrixStack DirectionStack = new MatrixStack(31, 63);
        public MatrixStack TextureStack = new MatrixStack(1, 0);
        public Matrix ClipMatrix;
        public bool ClipMatrixDirty;

        public byte[] Viewport1 = new byte[2];
        public byte[] Viewport2 = new byte[2];

        // Debug State 
        public Matrix DebugProjectionMatrix;
        public Matrix DebugPositionMatrix;
        public Matrix DebugDirectionMatrix;




        public Matrix DebugTextureMatrix;

        public PrimitiveType PrimitiveType;
        public short[] VertexCoords = new short[3];
        public byte[] VertexColor = new byte[3];
        public CircularBuffer<Vertex> VertexQueueFront = new CircularBuffer<Vertex>(6144, new Vertex());
        public CircularBuffer<Vertex> VertexQueueBack = new CircularBuffer<Vertex>(6144, new Vertex());

        public uint ReadHwio32(uint addr)
        {
            uint val = 0;

            switch (addr)
            {
                case 0x4000600: // GXSTAT
                    val |= 0b10; // always true boxtest

                    val |= (uint)(PositionStack.Sp & 0b11111) << 8;
                    val |= (uint)(ProjectionStack.Sp & 0b1) << 13;

                    val |= CommandFifo.Entries << 16;
                    if (CommandFifo.Entries == 256) val = BitSet(val, 24);
                    if (CommandFifo.Entries < 128) val = BitSet(val, 25);
                    if (CommandFifo.Entries == 0) val = BitSet(val, 26);
                    if (CommandFifo.Entries > 0) val = BitSet(val, 27);
                    val |= (uint)CommandFifoIrqMode << 30;
                    break;
            }

            return val;
        }

        public void WriteHwio32(uint addr, uint val)
        {
            if (addr >= 0x4000440 && addr < 0x4000600)
            {
                Debug2("3D: MMIO insert cmd " + Hex((byte)(addr >> 2), 2) + " param " + Hex(val, 8));

                QueueCommand((byte)(addr >> 2), val);
                // Debug("3D: Port command send");
            }

            // GXFIFO
            if (addr >= 0x4000400 && addr < 0x4000440)
            {
                // QueueCommand(0);
                // TODO: GXFIFO commands
                // Console.WriteLine("GXFIFO command send");
                if (PackedCommandQueue.Entries == 0)
                {
                    // Console.WriteLine(CommandFifo.Entries);
                    for (int i = 0; i < 4; i++)
                    {
                        byte cmd = (byte)val;

                        if (cmd != 0)
                        {
                            if (cmd > 0x72)
                            {
                                throw new Exception("3D: GXFIFO insert invalid cmd " + Hex(cmd, 2) + " from addr " + Hex(addr, 8));
                            }

                            Debug2("3D: GXFIFO insert " + Hex(cmd, 2) + " from addr " + Hex(addr, 8));
                            if (CommandParamLengths[cmd] != 0)
                            {
                                PackedCommandQueue.Insert(cmd);
                            }
                            else
                            {
                                // if no params just queue it up
                                QueueCommand(cmd, 0);
                            }
                        }

                        val >>= 8;
                    }
                }
                else
                {
                    // Console.WriteLine("quued");
                    byte cmd = PackedCommandQueue.Peek();
                    QueueCommand(cmd, val);

                    PackedParamsQueued++;

                    Debug2("3D: GXFIFO take param cmd " + Hex(cmd, 2) + " param " + Hex(val, 8) + " remaining " + (CommandParamLengths[cmd] - PackedParamsQueued) + " from addr " + Hex(addr, 8));

                    if (PackedParamsQueued >= CommandParamLengths[cmd])
                    {
                        PackedParamsQueued = 0;

                        PackedCommandQueue.Pop();

                        Debug("execu");
                    }
                }
                return;
            }

            switch (addr)
            {
                case 0x4000600:
                    CommandFifoIrqMode = (byte)((val >> 30) & 0b11);
                    return;
            }
        }

        public void QueueCommand(byte cmd, uint val)
        {
            if (!CommandFifo.Insert(new Command(cmd, val)))
            {
                Console.Error.WriteLine("3D: GXFIFO overflow");
            }
        }

        public void RunCommand(Command cmd)
        {
            Debug2("3D CMD: " + Hex(cmd.Cmd, 2) + " take " + CommandParamLengths[cmd.Cmd] + " params " + "queue " + CommandFifo.Entries);

            switch (cmd.Cmd)
            {
                case 0x10: // Set Matrix Mode
                    PopCommand();
                    MatrixMode = (MatrixMode)(cmd.Param & 0b11);
                    Debug("Set Matrix Mode: " + MatrixMode);
                    break;
                case 0x11: // Push Current Matrix
                    Debug("Push Current Matrix");
                    PopCommand();
                    switch (MatrixMode)
                    {
                        case MatrixMode.Projection:
                            // ProjectionStack.Current.Print("Push Projection");
                            ProjectionStack.Push();
                            break;
                        case MatrixMode.Position:
                        case MatrixMode.PositionDirection:
                            // PositionStack.Current.Print("Push PositionDirection");
                            PositionStack.Push();
                            DirectionStack.Push();
                            break;
                        case MatrixMode.Texture:
                            TextureStack.Push();
                            break;
                    }
                    break;
                case 0x12: // Pop Current Matrix
                    Debug("Pop Current Matrix");
                    PopCommand();
                    switch (MatrixMode)
                    {
                        case MatrixMode.Projection:
                            ProjectionStack.Pop(SignExtend8((byte)cmd.Param, 5));
                            break;
                        case MatrixMode.Position:
                        case MatrixMode.PositionDirection:
                            // PositionStack.Current.Print("Pre-pop position matrix");
                            PositionStack.Pop(SignExtend8((byte)cmd.Param, 5));
                            DirectionStack.Pop(SignExtend8((byte)cmd.Param, 5));
                            break;
                        case MatrixMode.Texture:
                            TextureStack.Pop(SignExtend8((byte)cmd.Param, 5));
                            break;
                    }
                    break;
                case 0x13: // Store Current Matrix
                    PopCommand();
                    switch (MatrixMode)
                    {
                        case MatrixMode.Projection:
                            ProjectionStack.Store((byte)(cmd.Param & 0b11111));
                            break;
                        case MatrixMode.Position:
                        case MatrixMode.PositionDirection:
                            PositionStack.Store((byte)(cmd.Param & 0b11111));
                            DirectionStack.Store((byte)(cmd.Param & 0b11111));
                            break;
                        case MatrixMode.Texture:
                            TextureStack.Store((byte)(cmd.Param & 0b11111));
                            break;
                    }
                    break;
                case 0x14: // Restore Current Matrix
                    PopCommand();
                    switch (MatrixMode)
                    {
                        case MatrixMode.Projection:
                            ProjectionStack.Restore((byte)(cmd.Param & 0b11111));
                            break;
                        case MatrixMode.Position:
                        case MatrixMode.PositionDirection:
                            PositionStack.Restore((byte)(cmd.Param & 0b11111));
                            DirectionStack.Restore((byte)(cmd.Param & 0b11111));
                            break;
                        case MatrixMode.Texture:
                            TextureStack.Restore((byte)(cmd.Param & 0b11111));
                            break;
                    }
                    break;
                case 0x15: // Load Identity Matrix
                    Debug("Load Identity Matrix");
                    PopCommand();
                    switch (MatrixMode)
                    {
                        case MatrixMode.Projection:
                            ProjectionStack.Current = Matrix.GetIdentity();
                            ClipMatrixDirty = true;
                            break;
                        case MatrixMode.Position:
                            PositionStack.Current = Matrix.GetIdentity();
                            ClipMatrixDirty = true;
                            break;
                        case MatrixMode.PositionDirection:
                            PositionStack.Current = Matrix.GetIdentity();
                            DirectionStack.Current = Matrix.GetIdentity();
                            ClipMatrixDirty = true;
                            break;
                        case MatrixMode.Texture:
                            TextureStack.Current = Matrix.GetIdentity();
                            break;
                    }
                    break;
                case 0x16: // Load 4x4 Matrix to Current Matrix
                    {
                        Matrix m = new Matrix();

                        for (int i = 0; i < 16; i++)
                        {
                            m.Data[i] = (int)PopCommand().Param;
                        }

                        LoadCurrentMatrix(ref m);
                    }
                    break;
                case 0x17: // Load 4x3 Matrix to Current Matrix
                    {
                        Matrix m = Matrix.GetIdentity();

                        for (int i = 0; i < 4; i++)
                        {
                            for (int j = 0; j < 3; j++)
                            {
                                m.Data[i * 4 + j] = (int)PopCommand().Param;
                            }
                        }

                        LoadCurrentMatrix(ref m);
                    }
                    break;
                case 0x18: // Multiply Current Matrix by 4x4 Matrix
                    Debug("Multiply Current Matrix by 4x4 Matrix");
                    {
                        Matrix m = new Matrix();

                        for (int i = 0; i < 16; i++)
                        {
                            m.Data[i] = (int)PopCommand().Param;
                        }

                        MultiplyCurrentMatrixBy(ref m);
                    }
                    break;
                case 0x19: // Multiply Current Matrix by 4x3 Matrix
                    Debug("Multiply Current Matrix by 4x3 Matrix");
                    {
                        Matrix m = Matrix.GetIdentity();

                        for (int i = 0; i < 4; i++)
                        {
                            for (int j = 0; j < 3; j++)
                            {
                                m.Data[i * 4 + j] = (int)PopCommand().Param;
                            }
                        }

                        MultiplyCurrentMatrixBy(ref m);
                    }
                    break;
                case 0x1A: // Multiply Current Matrix by 3x3 Matrix
                    Debug("Multiply Current Matrix by 3x3 Matrix");
                    {
                        Matrix m = Matrix.GetIdentity();

                        for (int i = 0; i < 3; i++)
                        {
                            for (int j = 0; j < 3; j++)
                            {
                                m.Data[i * 4 + j] = (int)PopCommand().Param;
                            }
                        }

                        MultiplyCurrentMatrixBy(ref m);
                    }
                    break;
                case 0x1B: // Multiply Current Matrix by Scale Matrix
                    Debug("Multiply Current Matrix by Scale Matrix");
                    {
                        Matrix m = Matrix.GetIdentity();

                        m.Data[0] = (int)PopCommand().Param;
                        m.Data[5] = (int)PopCommand().Param;
                        m.Data[10] = (int)PopCommand().Param;

                        switch (MatrixMode)
                        {
                            case MatrixMode.Projection:
                                ProjectionStack.Current = ProjectionStack.Current.Multiply(m);
                                ClipMatrixDirty = true;
                                break;
                            case MatrixMode.Position:
                            case MatrixMode.PositionDirection:
                                PositionStack.Current = PositionStack.Current.Multiply(m);
                                ClipMatrixDirty = true;
                                break;
                            case MatrixMode.Texture:
                                TextureStack.Current = TextureStack.Current.Multiply(m);
                                break;
                        }
                    }
                    break;
                case 0x1C: // Multiply Current Matrix by Translation Matrix
                    Debug("Multiply Current Matrix by Translation Matrix");
                    {
                        Matrix m = Matrix.GetIdentity();

                        m.Data[12] = (int)PopCommand().Param;
                        m.Data[13] = (int)PopCommand().Param;
                        m.Data[14] = (int)PopCommand().Param;

                        MultiplyCurrentMatrixBy(ref m);
                    }
                    break;
                case 0x20: // Directly Set Vertex Color
                    Debug("Directly Set Vertex Color");
                    PopCommand();
                    for (int i = 0; i < 3; i++)
                    {
                        uint color = cmd.Param & 0b11111;

                        VertexColor[i] = (byte)(color * 2 + (color + 31) / 32);
                        cmd.Param >>= 5;
                    }
                    break;
                case 0x21: // TODO: Set Normal Vector
                    PopCommand();
                    break;
                case 0x22: // TODO: Set Texture Coordinates
                    PopCommand();
                    break;
                case 0x23: // Set Vertex XYZ 12-bit fraction
                    Debug("Set Vertex XYZ 12-bit fraction");
                    PopCommand();
                    VertexCoords[0] = (short)cmd.Param;
                    VertexCoords[1] = (short)(cmd.Param >> 16);
                    cmd = PopCommand();
                    VertexCoords[2] = (short)cmd.Param;

                    TransformAndAddVertex();
                    break;
                case 0x24: // Set Vertex XYZ 6-bit fraction
                    PopCommand();
                    VertexCoords[0] = (short)(((cmd.Param >> 0) & 1023) << 6);
                    VertexCoords[1] = (short)(((cmd.Param >> 10) & 1023) << 6);
                    VertexCoords[2] = (short)(((cmd.Param >> 20) & 1023) << 6);

                    TransformAndAddVertex();
                    break;
                case 0x25: // Set Vertex XY 12-bit fraction
                    PopCommand();
                    VertexCoords[0] = (short)cmd.Param;
                    VertexCoords[1] = (short)(cmd.Param >> 16);

                    TransformAndAddVertex();
                    break;
                case 0x26: // Set Vertex XZ 12-bit fraction
                    PopCommand();
                    VertexCoords[0] = (short)cmd.Param;
                    VertexCoords[2] = (short)(cmd.Param >> 16);

                    TransformAndAddVertex();
                    break;
                case 0x27: // Set Vertex YZ 12-bit fraction
                    PopCommand();
                    VertexCoords[1] = (short)cmd.Param;
                    VertexCoords[2] = (short)(cmd.Param >> 16);

                    TransformAndAddVertex();
                    break;
                case 0x28: // Relative add vertex coordinates
                    PopCommand();
                    VertexCoords[0] += (short)((((cmd.Param >> 0) & 1023) << 6) >> 6);
                    VertexCoords[1] += (short)((((cmd.Param >> 10) & 1023) << 6) >> 6);
                    VertexCoords[2] += (short)((((cmd.Param >> 20) & 1023) << 6) >> 6);

                    TransformAndAddVertex();
                    break;
                case 0x29: // TODO: Polygon Attributes
                    Debug("Polygon Attributes");
                    PopCommand();
                    break;
                case 0x2A: // TODO: Texture Parameters
                    Debug("Texture Parameters");
                    PopCommand();
                    break;
                case 0x2B: // TODO: Set Texture Palette Base Address
                    PopCommand();
                    break;
                case 0x30: // TODO: Diffise/Ambient Reflections
                    PopCommand();
                    break;
                case 0x31: // TODO: Specular Reflections & Emission
                    PopCommand();
                    break;
                case 0x32: // TODO: Light Vector
                    PopCommand();
                    break;
                case 0x33: // TODO: Set Light Color
                    PopCommand();
                    break;
                case 0x34: // TODO: Shininess
                    for (uint i = 0; i < 32; i++)
                    {
                        PopCommand();
                    }
                    break;
                case 0x40: // Begin Vertex List
                    PopCommand();
                    PrimitiveType = (PrimitiveType)(cmd.Param & 0b11);
                    // Console.WriteLine("Begin Vertex List: " + PrimitiveType);
                    break;
                case 0x41: // End Vertex List - essentially a NOP
                    Debug("End Vertex List");
                    PopCommand();
                    break;
                case 0x50: // Swap Buffers
                    Debug("Swap Buffers");
                    PopCommand();
                    // TODO: swap buffers parameters
                    Render();
                    Swap(ref VertexQueueFront, ref VertexQueueBack);
                    break;
                case 0x60: // Set Viewport
                    Debug("Set Viewport");
                    PopCommand();
                    Viewport1[0] = (byte)cmd.Param;
                    Viewport1[1] = (byte)(cmd.Param >> 8);
                    Viewport2[0] = (byte)(cmd.Param >> 16);
                    Viewport2[1] = (byte)(cmd.Param >> 24);
                    break;
                case 0x70: // TODO: Box Test
                    PopCommand();
                    PopCommand();
                    PopCommand();
                    break;
                case 0x71: // TODO: Position Test
                    PopCommand();
                    PopCommand();
                    break;
                default:
                    throw new Exception(Hex(cmd.Cmd, 2));

                    if (CommandParamLengths[cmd.Cmd] == 0)
                    {
                        PopCommand();
                    }
                    else
                    {
                        for (uint i = 0; i < CommandParamLengths[cmd.Cmd]; i++)
                        {
                            PopCommand();
                        }
                    }
                    break;
            }
        }

        public void Run()
        {
            if (CommandFifo.Entries > 0)
            {
                Command cmd = PeekCommand();

                if (CommandFifo.Entries >= CommandParamLengths[cmd.Cmd])
                {
                    RunCommand(cmd);
                }
            }

            if (
                (CommandFifo.Entries == 0 && CommandFifoIrqMode == 2) ||
                (CommandFifo.Entries < 128 && CommandFifoIrqMode == 1)
            )
            {
                Nds.HwControl9.FlagInterrupt((uint)InterruptNds.GeometryFifo);
            }
        }

        public Command PopCommand()
        {
            if (CommandFifo.Entries == 0) Console.Error.WriteLine("3D: Tried popping with no more commands left!");

            return CommandFifo.Pop();
        }

        public Command PeekCommand()
        {
            return CommandFifo.Peek();
        }

        public void LoadCurrentMatrix(ref Matrix m)
        {
            switch (MatrixMode)
            {
                case MatrixMode.Projection:
                    ProjectionStack.Current = m;
                    ClipMatrixDirty = true;
                    break;
                case MatrixMode.Position:
                    PositionStack.Current = m;
                    ClipMatrixDirty = true;
                    break;
                case MatrixMode.PositionDirection:
                    PositionStack.Current = m;
                    DirectionStack.Current = m;
                    ClipMatrixDirty = true;
                    break;
                case MatrixMode.Texture:
                    TextureStack.Current = m;
                    break;
            }
        }

        public void MultiplyCurrentMatrixBy(ref Matrix m)
        {
            switch (MatrixMode)
            {
                case MatrixMode.Projection:
                    ProjectionStack.Current = ProjectionStack.Current.Multiply(m);
                    ClipMatrixDirty = true;
                    // ProjectionStack.Current.Print("Projection after multiply");
                    break;
                case MatrixMode.Position:
                    PositionStack.Current = PositionStack.Current.Multiply(m);
                    ClipMatrixDirty = true;
                    break;
                case MatrixMode.PositionDirection:
                    PositionStack.Current.Print("Before translation mul");
                    PositionStack.Current = PositionStack.Current.Multiply(m);
                    DirectionStack.Current = DirectionStack.Current.Multiply(m);
                    PositionStack.Current.Print("After translation mul");
                    break;
                case MatrixMode.Texture:
                    TextureStack.Current = TextureStack.Current.Multiply(m);
                    break;
            }
        }

        public void TransformAndAddVertex()
        {
            var v = new Vertex();

            for (int i = 0; i < 3; i++)
            {
                v.Color[i] = VertexColor[i];
                v.Pos.Data[i] = VertexCoords[i];
            }

            if (ClipMatrixDirty)
            {
                ClipMatrixDirty = false;

                ClipMatrix = ProjectionStack.Current.Multiply(PositionStack.Current);
            }

            v.Pos.Data[3] = 0x1000; // Set W coordinate to 1 
            v.Pos = ClipMatrix.Multiply(v.Pos);
            // PositionStack.Current.Print("Position Matrix");
            VertexQueueBack.Insert(v);

        }

        public void Render()
        {
            // Fill screen with black for now
            for (uint i = 0; i < Screen.Length; i++)
            {
                Screen[i] = 0;
            }

            Span<Vertex> vertices = stackalloc Vertex[3];
            while (VertexQueueFront.Entries >= 1)
            {
                var v = VertexQueueFront.Pop();

                int x = v.Pos.Data[0];
                int y = v.Pos.Data[1];
                int z = v.Pos.Data[2];
                int w = v.Pos.Data[3];

                if (w == 0)
                {
                    continue;
                }

                // int screenX = (((x * (Viewport2[0] - Viewport1[0] + 1)) >> 12) / 8) + Viewport2[0] / 2;
                // int screenY = (((y * (Viewport2[1] - Viewport1[1] + 1)) >> 12) / 8) + Viewport2[1] / 2;
                // int screenX = (x * (Viewport2[0] - Viewport1[0] + 1)) / (2 * w) + Viewport2[0] / 2;
                // int screenY = (y * (Viewport2[1] - Viewport1[1] + 1)) / (2 * w) + Viewport2[1] / 2;

                // int screenX = (x * 256) / (2 * w) + 128;
                // int screenY = (y * 192) / (2 * w) + 96;

                int screenX = x * 128 / w + 128;
                int screenY = -y * 96 / w + 96;

                // Console.WriteLine($"{screenX} {screenY}");

                // 0,0 is bottom left on NDS
                // screenY = 191 - screenY;

                SetPixel(screenX, screenY);
            }

            // Console.WriteLine(VertexQueueFront.Entries);

            // while (VertexQueue.Entries >= 3)
            // {
            //     for (int i = 0; i < 3; i++)
            //     {
            //         vertices[i] = VertexQueue.Pop();

            //         int x = vertices[i].Pos.Data[0];
            //         int y = vertices[i].Pos.Data[1];
            //         int z = vertices[i].Pos.Data[2];
            //         int w = vertices[i].Pos.Data[3];

            //         if (w == 0)
            //         {
            //             w = 0x1000;
            //         }

            //         // int screenX = (((x * (Viewport2[0] - Viewport1[0] + 1)) >> 12) / 8) + Viewport2[0] / 2;
            //         // int screenY = (((y * (Viewport2[1] - Viewport1[1] + 1)) >> 12) / 8) + Viewport2[1] / 2;
            //         int screenX = (x * (Viewport2[0] - Viewport1[0] + 1)) / (2 * w) + Viewport2[0] / 2;
            //         int screenY = (y * (Viewport2[1] - Viewport1[1] + 1)) / (2 * w) + Viewport2[1] / 2;

            //         // 0,0 is bottom left on NDS
            //         screenY = 191 - screenY;

            //         // Console.WriteLine($"{screenX}, {screenY}, {z}, {w}");

            //         vertices[i].Pos.Data[0] = screenX;
            //         vertices[i].Pos.Data[1] = screenY;

            //         SetPixel(screenX, screenY);
            //     }

            //     DrawLine(vertices[0].Pos.Data[0], vertices[0].Pos.Data[1], vertices[1].Pos.Data[0], vertices[1].Pos.Data[1]);
            //     DrawLine(vertices[1].Pos.Data[0], vertices[1].Pos.Data[1], vertices[2].Pos.Data[0], vertices[2].Pos.Data[1]);
            //     DrawLine(vertices[2].Pos.Data[0], vertices[2].Pos.Data[1], vertices[0].Pos.Data[0], vertices[0].Pos.Data[1]);
            // }

            // var m = PositionStack.Current;
            var m = ProjectionStack.Current;

        }

        void DrawLine(int x0, int y0, int x1, int y1)
        {
            bool low;
            bool swap;
            int dx0;
            int dy0;
            int dx1;
            int dy1;

            if (Math.Abs(y1 - y0) < Math.Abs(x1 - x0))
            {
                low = true;
                swap = x0 > x1;
            }
            else
            {
                low = false;
                swap = y0 > y1;
            }

            if (swap)
            {
                dx0 = x1;
                dy0 = y1;
                dx1 = x0;
                dy1 = y0;
            }
            else
            {
                dx0 = x0;
                dy0 = y0;
                dx1 = x1;
                dy1 = y1;
            }

            if (low)
            {
                int dx = dx1 - dx0;
                int dy = dy1 - dy0;

                int yi = 1;

                if (dy < 0)
                {
                    yi = -1;
                    dy = -dy;
                }

                int d = (2 * dy) - dx;
                int y = dy0;

                for (int x = dx0; x <= dx1; x++)
                {
                    SetPixel(x, y);
                    if (d > 0)
                    {
                        y = y + yi;
                        d = d + (2 * (dy - dx));
                    }
                    else
                    {
                        d = d + 2 * dy;
                    }
                }
            }
            else
            {
                int dx = dx1 - dx0;
                int dy = dy1 - dy0;

                int xi = 1;

                if (dx < 0)
                {
                    xi = -1;
                    dx = -dx;
                }

                int d = (2 * dx) - dy;
                int x = dx0;

                for (int y = dy0; y <= dy1; y++)
                {
                    SetPixel(x, y);
                    if (d > 0)
                    {
                        x = x + xi;
                        d = d + (2 * (dx - dy));
                    }
                    else
                    {
                        d = d + 2 * dx;
                    }
                }
            }
        }

        void SetPixel(int x, int y)
        {
            if ((uint)x >= 256 || (uint)y >= 192)
            {
                return;
            }

            var screenIndex = y * 256 + x;

            Screen[screenIndex] = 0x7FFF;
        }

        [Conditional("NEVER")]
        public static void Debug(string s)
        {
            Console.WriteLine("3D: " + s);
        }

        [Conditional("NEVER")]
        public void Debug2(string s)
        {
            Console.WriteLine("[" + Hex(Nds.Cpu9.GetCurrentInstrAddr(), 8) + "] " + s);
        }
    }
}
