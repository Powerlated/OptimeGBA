using System;

namespace OptimeGBA
{
    public delegate void SchedulerCallback(long cyclesLate);

    public enum SchedulerId : byte
    {
        None = 255,
        RootNode = 254,
        Ppu = 0,
        ApuSample = 1,
        Timer0 = 2,
        Timer1 = 3,
        Timer2 = 4,
        Timer3 = 5,
        HaltSkip = 6,
    }

    public class SchedulerEvent
    {
        public SchedulerId Id;
        public long Ticks;
        public SchedulerCallback Callback;
        public SchedulerEvent NextEvent;
        public SchedulerEvent PrevEvent;

        public SchedulerEvent(SchedulerId id, long ticks, SchedulerCallback callback)
        {
            this.Id = id;
            this.Ticks = ticks;
            this.Callback = callback;
        }
    }

    public sealed class Scheduler
    {
        public Scheduler()
        {
            for (uint i = 0; i < 64; i++)
            {
                FreeEventStack[i] = new SchedulerEvent(SchedulerId.None, 0, (long ticks) => { });
            }

            var evt = PopStack();
            evt.NextEvent = null;
            evt.PrevEvent = null;
            evt.Id = SchedulerId.RootNode;
            evt.Ticks = 0;
            RootEvent = evt;
        }

        public long CurrentTicks = 0;
        public long NextEventTicks = long.MaxValue;

        public uint FreeEventStackIndex = 0;
        public SchedulerEvent[] FreeEventStack = new SchedulerEvent[64];

        public SchedulerEvent PopStack()
        {
            return FreeEventStack[FreeEventStackIndex++];
        }

        public void PushStack(SchedulerEvent schedulerEvent)
        {
            FreeEventStack[--FreeEventStackIndex] = schedulerEvent;
        }

        public SchedulerEvent RootEvent;
        public uint EventsQueued = 0;

        static SchedulerEvent createEmptyEvent()
        {
            return new SchedulerEvent(SchedulerId.None, 0, (long ticks) => { });
        }

        public static string ResolveId(SchedulerId id)
        {
            switch (id)
            {
                case SchedulerId.None: return "None";
                case SchedulerId.Ppu: return "PPU Event";
                case SchedulerId.ApuSample: return "APU Sample";
                case SchedulerId.Timer0: return "Timer 0 Overflow";
                case SchedulerId.Timer1: return "Timer 1 Overflow";
                case SchedulerId.Timer2: return "Timer 2 Overflow";
                case SchedulerId.Timer3: return "Timer 3 Overflow";
                case SchedulerId.RootNode: return "<root node>";
                default:
                    return "<SchedulerId not found>";
            }
        }

        public void AddEventRelative(SchedulerId id, long ticks, SchedulerCallback callback)
        {
            var origTicks = ticks;
            ticks += CurrentTicks;

            var newEvt = PopStack();
            newEvt.Id = id;
            newEvt.Ticks = ticks;
            newEvt.Callback = callback;

            var prevEvt = RootEvent;
            // Traverse linked list and splice at correct location
            while (prevEvt.NextEvent != null)
            {
                if (ticks >= prevEvt.Ticks && ticks <= prevEvt.NextEvent?.Ticks) {
                    break;
                }
                prevEvt = prevEvt.NextEvent;
            }

            var nextEvt = prevEvt.NextEvent;
            if (nextEvt != null) {
                nextEvt.PrevEvent = newEvt;
            }
            prevEvt.NextEvent = newEvt;
            newEvt.NextEvent = nextEvt;
            newEvt.PrevEvent = prevEvt;

            EventsQueued++;
            UpdateNextEvent();
        }

        public void CancelEventsById(SchedulerId id)
        {
            SchedulerEvent evt = RootEvent.NextEvent;
            while (evt != null)
            {
                if (evt.Id == id)
                {
                    RemoveEvent(evt);
                }
                evt = evt.NextEvent;
            }
        }

        public void UpdateNextEvent()
        {
            if (EventsQueued > 0)
            {
                NextEventTicks = RootEvent.NextEvent.Ticks;
            }
        }

        public SchedulerEvent GetFirstEvent()
        {
            return RootEvent.NextEvent;
        }

        public SchedulerEvent PopFirstEvent()
        {
            var evt = RootEvent.NextEvent;
            RemoveEvent(evt);
            return evt;
        }

        public void RemoveEvent(SchedulerEvent schedulerEvent)
        {
            if (schedulerEvent == RootEvent) {
                throw new Exception("Cannot remove root event!");
            }
            var prev = schedulerEvent.PrevEvent;
            var next = schedulerEvent.NextEvent;
            if (schedulerEvent.NextEvent != null)
            {
                next.PrevEvent = prev;
            }
            prev.NextEvent = next;
            schedulerEvent.NextEvent = null;
            schedulerEvent.PrevEvent = null;
            EventsQueued--;
            UpdateNextEvent();
            PushStack(schedulerEvent);
        }
    }
}
