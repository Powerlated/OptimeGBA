using System;
using static OptimeGBA.Bits;
using static OptimeGBA.CoreUtil;
using static Util;

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
                int sum = 0;

                int ai = i & ~3; // Matrix A row
                int bi = i & 3; // Matrix B column

                for (int j = 0; j < 4; j++)
                {
                    sum += a.Data[ai] * Data[bi];
                    ai++;
                    bi += 4;
                }

                // Trim multiplied fixed point digits off
                m.Data[i] = sum >> 12;
            }

            return m;
        }

        public Vector Multiply(Vector a)
        {
            var v = new Vector();

            for (int i = 0; i < 4; i++)
            {
                int sum = 0;

                for (int j = 0; j < 4; j++)
                {
                    int mat = Data[j * 4 + i];
                    int vec = a.Data[j];
                    sum += mat * vec;
                }

                // Trim multiplied fixed point digits off
                v.Data[i] = sum >> 12;
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
        public uint PendingCommand;
        public CircularBuffer<Command> CommandFifo = new CircularBuffer<Command>(256, new Command());

        // GPU State
        public MatrixMode MatrixMode;
        public MatrixStack ProjectionStack = new MatrixStack(1, 0);
        public MatrixStack PositionStack = new MatrixStack(31, 63);
        public MatrixStack DirectionStack = new MatrixStack(31, 63);
        public MatrixStack TextureStack = new MatrixStack(1, 0);

        public byte[] Viewport1 = new byte[2];
        public byte[] Viewport2 = new byte[2];

        // Debug State 
        public Matrix DebugProjectionMatrix;
        public Matrix DebugPositionMatrix;
        public Matrix DebugDirectionMatrix;
        public Matrix DebugTextureMatrix;

        public PrimitiveType PrimitiveType;
        public byte[] VertexColor = new byte[3];
        public CircularBuffer<Vertex> VertexQueue = new CircularBuffer<Vertex>(1024, new Vertex());

        public CircularBuffer<byte> PackedCommandQueue = new CircularBuffer<byte>(1024, 0);

        public uint ReadHwio32(uint addr)
        {
            uint val = 0;

            switch (addr)
            {
                case 0x4000600:
                    val |= (uint)(PositionStack.Sp & 0b11111) << 8;
                    val |= (uint)(ProjectionStack.Sp & 0b1) << 13;

                    val |= CommandFifo.Entries << 16;
                    if (CommandFifo.Entries == 256) val = BitSet(val, 24);
                    if (CommandFifo.Entries < 128) val = BitSet(val, 25);
                    if (CommandFifo.Entries == 0) val = BitSet(val, 26);
                    val |= (uint)CommandFifoIrqMode << 30;
                    break;
            }

            return val;
        }

        public void WriteHwio32(uint addr, uint val)
        {
            if (addr >= 0x4000440 && addr < 0x4000600)
            {
                QueueCommand((byte)(addr >> 2), val);
                // Debug("3D: Port command send");
            }

            switch (addr)
            {
                case 0x4000400:
                    // QueueCommand(0);
                    // TODO: GXFIFO commands
                    // Console.WriteLine("GXFIFO command send");
                    if (PackedCommandQueue.Entries == 0)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            byte cmd = (byte)val;

                            if (cmd != 0)
                            {
                                PackedCommandQueue.Insert(cmd);
                            }

                            cmd >>= 8;
                        }
                    }
                    else
                    {
                        // Console.WriteLine("quued");
                        QueueCommand(PackedCommandQueue.Peek(), val);

                        byte cmd = PackedCommandQueue.Peek();

                        if (CommandFifo.Entries >= CommandParamLengths[cmd])
                        {
                            PackedCommandQueue.Pop();
                            Debug("execu");

                            RunCommand(CommandFifo.Peek());
                        }
                    }
                    return;

                case 0x4000600:
                    CommandFifoIrqMode = (byte)((val >> 30) & 0b11);
                    return;
            }
        }

        public void QueueCommand(byte cmd, uint val)
        {
            CommandFifo.Insert(new Command(cmd, val));
        }

        public void RunCommand(Command cmd)
        {
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
                            ProjectionStack.Pop(SignExtend((byte)cmd.Param, 5));
                            break;
                        case MatrixMode.Position:
                        case MatrixMode.PositionDirection:
                            // PositionStack.Current.Print("Pre-pop position matrix");
                            PositionStack.Pop(SignExtend((byte)cmd.Param, 5));
                            DirectionStack.Pop(SignExtend((byte)cmd.Param, 5));
                            break;
                        case MatrixMode.Texture:
                            TextureStack.Pop(SignExtend((byte)cmd.Param, 5));
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
                            break;
                        case MatrixMode.Position:
                            PositionStack.Current = Matrix.GetIdentity();
                            break;
                        case MatrixMode.PositionDirection:
                            PositionStack.Current = Matrix.GetIdentity();
                            DirectionStack.Current = Matrix.GetIdentity();
                            break;
                        case MatrixMode.Texture:
                            TextureStack.Current = Matrix.GetIdentity();
                            break;
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
                case 0x23: // Set Vertex XYZ 12-bit fraction
                    Debug("Set Vertex XYZ 12-bit fraction");
                    {
                        var v = new Vector();

                        PopCommand();
                        v.Data[0] = (short)cmd.Param;
                        v.Data[1] = (short)(cmd.Param >> 16);
                        cmd = PopCommand();
                        v.Data[2] = (short)cmd.Param;

                        TransformAndAddVertex(v);
                    }
                    break;
                case 0x29: // Polygon Attributes
                           // TODO
                    Debug("Polygon Attributes");
                    PopCommand();
                    break;
                case 0x2A: // Texture Parameters
                           // TODO
                    Debug("Texture Parameters");
                    PopCommand();
                    break;
                case 0x40: // Begin Vertex List
                    PopCommand();
                    Debug("Begin Vertex List");
                    PrimitiveType = (PrimitiveType)(cmd.Param & 0b11);
                    break;
                case 0x41: // End Vertex List
                           // This does nothing lmao
                    Debug("End Vertex List");
                    PopCommand();
                    break;
                case 0x50: // Swap Buffers
                    Debug("Swap Buffers");
                    PopCommand();
                    // TODO: swap buffers parameters
                    Render();
                    VertexQueue.Reset();
                    break;
                case 0x60: // Set Viewport
                    Debug("Set Viewport");
                    PopCommand();
                    Viewport1[0] = (byte)cmd.Param;
                    Viewport1[1] = (byte)(cmd.Param >> 8);
                    Viewport2[0] = (byte)(cmd.Param >> 16);
                    Viewport2[1] = (byte)(cmd.Param >> 24);
                    break;
                default:
                    // throw new Exception(Hex(cmd.Cmd, 2));
                    PopCommand();
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
            if (CommandFifo.Entries == 0) Debug("Tried popping with no more commands left!");

            return CommandFifo.Pop();
        }

        public Command PeekCommand()
        {
            return CommandFifo.Peek();
        }

        public void MultiplyCurrentMatrixBy(ref Matrix m)
        {
            switch (MatrixMode)
            {
                case MatrixMode.Projection:
                    ProjectionStack.Current = ProjectionStack.Current.Multiply(m);
                    // ProjectionStack.Current.Print("Projection after multiply");
                    break;
                case MatrixMode.Position:
                    PositionStack.Current = PositionStack.Current.Multiply(m);
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

        public void TransformAndAddVertex(Vector vec)
        {
            var v = new Vertex();

            for (int i = 0; i < 3; i++)
            {
                v.Color[i] = VertexColor[i];
            }

            v.Pos = vec;
            v.Pos.Data[3] = 0x1000; // Set W coordinate to 1 
            v.Pos = PositionStack.Current.Multiply(v.Pos);
            v.Pos = ProjectionStack.Current.Multiply(v.Pos);
            // PositionStack.Current.Print("Position Matrix");
            VertexQueue.Insert(v);
        }

        public void Render()
        {
            // Fill screen with black for now
            for (uint i = 0; i < Screen.Length; i++)
            {
                Screen[i] = 0;
            }

            while (VertexQueue.Entries >= 3)
            {
                Span<Vertex> vertices = stackalloc Vertex[3];

                for (int i = 0; i < 3; i++)
                {
                    vertices[i] = VertexQueue.Pop();

                    int x = vertices[i].Pos.Data[0];
                    int y = vertices[i].Pos.Data[1];
                    int z = vertices[i].Pos.Data[2];
                    int w = vertices[i].Pos.Data[3];

                    int screenX = (((x * (Viewport2[0] - Viewport1[0] + 1)) >> 12) / 8) + Viewport2[0] / 2;
                    int screenY = (((y * (Viewport2[1] - Viewport1[1] + 1)) >> 12) / 8) + Viewport2[1] / 2;

                    // 0,0 is bottom left on NDS
                    screenY = 191 - screenY;

                    vertices[i].Pos.Data[0] = screenX;
                    vertices[i].Pos.Data[1] = screenY;

                    if (w != 0)
                    {
                        Debug($"{screenX}, {screenY}, {z}, {w}");

                        if ((uint)screenX < 256 && (uint)screenY < 192)
                        {
                            SetPixel(screenX, screenY);
                        }
                    }
                }

                DrawLine(vertices[0].Pos.Data[0], vertices[0].Pos.Data[1], vertices[1].Pos.Data[0], vertices[1].Pos.Data[1]);
                DrawLine(vertices[1].Pos.Data[0], vertices[1].Pos.Data[1], vertices[2].Pos.Data[0], vertices[2].Pos.Data[1]);
                DrawLine(vertices[2].Pos.Data[0], vertices[2].Pos.Data[1], vertices[0].Pos.Data[0], vertices[0].Pos.Data[1]);
            }

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
            var screenIndex = y * 256 + x;

            Screen[screenIndex] = 0x7FFF;
        }

        public static void Debug(string s)
        {
            if (false)
                Console.WriteLine("3D: " + s);
        }
    }
}
