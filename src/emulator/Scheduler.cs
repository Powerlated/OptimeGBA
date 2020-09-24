using System;

namespace OptimeGBA
{
    public delegate void SchedulerCallback(long cyclesLate);

    public enum SchedulerId : byte
    {
        None = 255
    }

    public class SchedulerEvent
    {
        public SchedulerId Id;
        public long Ticks;
        public SchedulerCallback Callback;
        public byte Number = 0;

        public SchedulerEvent(SchedulerId id, long ticks, SchedulerCallback callback)
        {
            this.Id = id;
            this.Ticks = ticks;
            this.Callback = callback;
        }
    }

    public class Scheduler
    {
        static uint Parent(uint n) { return (n - 1) >> 1; }
        static uint LeftChild(uint n) { return n * 2 + 1; }
        static uint RightChild(uint n) { return n * 2 + 2; }

        public Scheduler()
        {
            for (uint i = 0; i < 64; i++)
            {
                Heap[i] = new SchedulerEvent(SchedulerId.None, 0, (long ticks) => { });
            }
        }

        public long CurrentTicks = 0;
        public long NextEventTicks = 0;

        SchedulerEvent[] Heap = new SchedulerEvent[64];
        uint HeapSize = 0;

        SchedulerEvent ReturnEvent = Scheduler.createEmptyEvent();

        static SchedulerEvent createEmptyEvent()
        {
            return new SchedulerEvent(SchedulerId.None, 0, (long ticks) => { });
        }

        public void AddEventRelative(SchedulerId id, long ticks, SchedulerCallback callback)
        {
            var origTicks = ticks;
            ticks += CurrentTicks;
            if (HeapSize >= Heap.Length)
            {
                throw new Exception("Heap overflow!");
            }

            var index = HeapSize;
            HeapSize++;
            Heap[index].Id = id;
            Heap[index].Ticks = ticks;
            Heap[index].Callback = callback;

            while (index != 0)
            {
                var parentIndex = Parent(index);
                if (Heap[parentIndex].Ticks > Heap[index].Ticks)
                {
                    Swap(index, parentIndex);
                    index = parentIndex;
                }
                else
                {
                    break;
                }
            }
            UpdateNextEvent();
        }

        public void CancelEventsById(SchedulerId id)
        {
            var go = true;
            while (go)
            {
                go = false;
                for (uint i = 0; i < HeapSize; i++)
                {
                    if (Heap[i].Id == id)
                    {
                        DeleteEvent(i);
                        go = true;
                        break;
                    }
                }
            }
        }

        public void UpdateNextEvent()
        {
            if (HeapSize > 0)
            {
                NextEventTicks = Heap[0].Ticks;
            }
        }

        public SchedulerEvent GetFirstEvent()
        {
            if (HeapSize <= 0)
            {
                Console.Error.WriteLine("Tried to get from empty heap!");
                return Heap[0]; // This isn't supposed to happen.
            }

            return Heap[0];
        }

        public SchedulerEvent PopFirstEvent()
        {
            var firstEvent = GetFirstEvent();

            ReturnEvent.Ticks = firstEvent.Ticks;
            ReturnEvent.Id = firstEvent.Id;
            ReturnEvent.Callback = firstEvent.Callback;

            if (HeapSize == 1)
            {
                HeapSize--;
                return ReturnEvent;
            }

            Swap(0, HeapSize - 1);

            HeapSize--;

            // Satisfy the heap property again
            uint index = 0;
            while (true)
            {
                var left = LeftChild(index);
                var right = RightChild(index);
                var smallest = index;

                if (left < HeapSize && Heap[left].Ticks < Heap[index].Ticks)
                {
                    smallest = left;
                }
                if (right < HeapSize && Heap[right].Ticks < Heap[smallest].Ticks)
                {
                    smallest = right;
                }

                if (smallest != index)
                {
                    Swap(index, smallest);
                    index = smallest;
                }
                else
                {
                    break;
                }
            }

            UpdateNextEvent();
            return ReturnEvent;
        }

        public void SetTicksLower(uint index, long newVal)
        {
            Heap[index].Ticks = newVal;

            while (index != 0)
            {
                var parentIndex = Parent(index);
                if (Heap[parentIndex].Ticks > Heap[index].Ticks)
                {
                    Swap(index, parentIndex);
                    index = parentIndex;
                }
                else
                {
                    break;
                }
            }
        }

        public void DeleteEvent(uint index)
        {
            SetTicksLower(index, -9999);
            PopFirstEvent();
        }

        public void Swap(uint ix, uint iy)
        {
            // console.log(`Swapped ${ix} with ${iy}`);
            var temp = Heap[ix];
            Heap[ix] = Heap[iy];
            Heap[iy] = temp;
        }
    }
}
