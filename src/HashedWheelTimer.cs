using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.CompilerServices;
using ricefan123.Timer.Util;
using System.Diagnostics;

namespace ricefan123.Timer
{
    public class HashedWheelTimer
    {
        #region Constructors

        public HashedWheelTimer()
        {
            SetSleep(SleepPolicy.Default);
            DefaultInitialize();
        }

        public HashedWheelTimer(SleepPolicy policy)
        {
            SetSleep(policy);
            DefaultInitialize();
        }

        public HashedWheelTimer(TimeSpan timeout, TimeSpan interval, SleepPolicy policy)
        {
            TicksInterval = NanoTime.FromMilliseconds(interval.TotalMilliseconds);
            DefaultTimeout = NanoTime.FromMilliseconds(timeout.TotalMilliseconds);
            ticked = 0;
            SetSleep(policy);
            InitializeSlots();
            workerThread = new Thread(WorkLoop);
            time.Start();
            workerThread.Start();
        }

        #endregion

        #region Public Methods

        public TimedCallback ScheduleTimeout(Action action, TimeSpan timeout)
        {
            if (Interlocked.CompareExchange(ref workerState, -1, -1) == WORKER_KILLED)
                throw new InvalidOperationException("Adding new timeout to stopped timer.");
            var ms = timeout.TotalMilliseconds;
            if (ms < 0)
                throw new ArgumentException("Expiry time cannot be negative.", "timeout");
            CheckTimerState();

            var nanoTimeout = NanoTime.FromMilliseconds(ms);
            var actualTimeout = time.Elapsed + nanoTimeout;
            var callback = new TimedCallback(action, actualTimeout);
            Interlocked.Increment(ref timeoutsCount);
            ScheduleTimeoutImpl(callback, actualTimeout);

            return callback;
        }

        public ICollection<TimedCallback> Stop() {
            if (Interlocked.CompareExchange(ref workerState, 0, 0) == WORKER_INIT)
            {
                while (Interlocked.CompareExchange(ref workerState, WORKER_KILLED, WORKER_INIT) != WORKER_INIT)
                    continue;
            }

            stopBarier.Wait();
            return unprocessedTimeouts;
        }


        #endregion

        #region Private Methods

        private void ScheduleTimeoutImpl(TimedCallback callback, long nanoseconds)
        {
            // TODO: Should always schedule to next tick
            var differredTimeout = nanoseconds + TicksInterval;
            var diff = ToWheelTicks(differredTimeout);
            var deadline = NanoTime.ToMilliseconds(CalculateDeadline());
            var due = diff + deadline;
            HashedWheelSlot slot;

            if (diff < WHEEL_SIZE)
            {
                var _ =(due & WHEEL_MASK);
                slot = slots[0, due & WHEEL_MASK];
            }
            else if (diff < 1 << (2 * WHEEL_BITS))
            {
                var _ =((due >> WHEEL_BITS) & WHEEL_MASK);
                slot = slots[1, (due >> WHEEL_BITS) & WHEEL_MASK];
            }
            else if (diff < 1 << (3 * WHEEL_BITS))
            {
                var _ =((due >> 2 * WHEEL_BITS) & WHEEL_MASK);
                slot = slots[2, (due >> 2 * WHEEL_BITS) & WHEEL_MASK];
            }
            else
            {
                if (diff > 0xffffffff)
                {
                    diff = 0xffffffff;
                    due = NanoTime.ToMilliseconds(diff + deadline);
                }
                var _ =((due >> 3 * WHEEL_BITS) & WHEEL_MASK);
                slot = slots[3, (due >> 3 * WHEEL_BITS) & WHEEL_MASK];
            }
            slot.Push(callback);


        }

        private void ExpireTimeouts()
        {

            var wheel0Index = (ticked & WHEEL_MASK);
            if (wheel0Index == 0)
            {
                if (CascadeTimers(1, (ticked >> WHEEL_BITS) & WHEEL_MASK) &&
                    CascadeTimers(2, (ticked >> (2 * WHEEL_BITS)) & WHEEL_MASK))
                    CascadeTimers(3, (ticked >> (3 * WHEEL_BITS)) & WHEEL_MASK);
            }

            var timeouts = slots[0, wheel0Index];

            for (var timeout = timeouts.TryPop(); timeout != null; timeout = timeouts.TryPop())
            {
                Interlocked.Decrement(ref timeoutsCount);
                timeout.Expire();
            }


        }

        private bool CascadeTimers(int hierarchyIdx, long tick)
        {
            // TODO
            // what if newly added timer has been assigned to cascading slot?

            var slot = new HashedWheelSlot(slots[hierarchyIdx, tick].Clear()); 

            for (TimedCallback callback = slot.TryPop(); 
                 callback != null;
                 callback = slot.TryPop())
            {
                ScheduleTimeoutImpl(callback, 
                                    callback.RemainingTime(time.Elapsed));
            }
            
            return tick == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long ToWheelTicks(long nanoTimeout)
        {
             return nanoTimeout / TicksInterval;
        }

        private void DefaultInitialize()
        {
            DefaultTimeout = NanoTime.FromSeconds(10);
            TicksInterval = NanoTime.FromMilliseconds(50);
            InitializeSlots();
            workerThread = new Thread(WorkLoop);
            time.Start();
            workerThread.Start();
            ticked = 0;
        }

        private void InitializeSlots()
        {
            slots = new HashedWheelSlot[WHEEL_BUCKETS, WHEEL_SIZE];

            for (int i = 0; i != WHEEL_BUCKETS; ++i)
                for (int j = 0; j != WHEEL_SIZE; ++j)
                    slots[i, j] = new HashedWheelSlot();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long CalculateDeadline()
        {
            return ticked * TicksInterval;
        }

        private void SetSleep(SleepPolicy policy)
        {
            switch (policy)
            {
            case SleepPolicy.Native:
            case SleepPolicy.Default:
                Sleep = SleepMethods.NativeSleep;
                break;
            case SleepPolicy.Precise:
                Sleep = SleepMethods.PreciseSleep;
                break;
            }
        }

        private void WorkLoop()
        {
            var test = Interlocked.CompareExchange(ref workerState, -1, -1);
            //if (Interlocked.CompareExchange(ref workerState, -1, -1) != WORKER_INIT)
            if (test == WORKER_KILLED)
            {
                throw new InvalidOperationException($"This should not happen! {test}");
            }

            while (Interlocked.CompareExchange(ref workerState, 0, 0) != WORKER_KILLED) 
            {
                ExpireTimeouts();
                long newDeadline = CalcNewDeadline();
                var sleepTime = newDeadline - time.Elapsed;
                // tick before sleep so that timeout added while sleeping
                // will be delayed by a tick to avoid early wake up.
                Sleep(NanoTime.ToMilliseconds(sleepTime));
                Interlocked.Increment(ref ticked);
            }
            
            unprocessedTimeouts = new HashSet<TimedCallback>();
            foreach (var slot in slots) 
            {
                TimedCallback timeout = null;
                while ((timeout = slot.TryPop()) != null)
                {
                    unprocessedTimeouts.Add(timeout);
                }
            }
            stopBarier.Signal();

        }

        private void CheckTimerState()
        {
            switch (Interlocked.CompareExchange(ref workerState, 0, 0))
            {
            case WORKER_INIT:
                break;
            case WORKER_KILLED:
                throw new InvalidOperationException("Adding Timeout to stopped timer.");
            }
        }

        private long CalcNewDeadline()
        {
            return (Interlocked.Read(ref ticked) + 1) * TicksInterval;
        }

        #endregion

        public int TimeoutsCount => Interlocked.CompareExchange(ref timeoutsCount, 0, 0);

        public int timeoutsCount = 0;

        private static readonly uint WHEEL_BUCKETS = 4;
        private static readonly int WHEEL_BITS = 8;
        private static readonly uint WHEEL_SIZE = (1u << WHEEL_BITS);

        private static readonly uint WHEEL_MASK = WHEEL_SIZE - 1;

        private Thread workerThread;

        private int workerState = WORKER_INIT;
        
        private const int WORKER_INIT = 0;
        private const int WORKER_KILLED = 1;
        
        private HashedWheelSlot[,] slots;
        private LinkedList<Action> timeouts = new LinkedList<Action>();

        private SleepMethods.SleepFunc Sleep { get; set; }

        private long ticked;

        private long DefaultTimeout { get; set; }

        /// <summary>
        /// In nanosecond scale.
        /// </summary>
        private long TicksInterval { get; set; }

        private ISet<TimedCallback> unprocessedTimeouts;

        private CountdownEvent stopBarier = new CountdownEvent(1);
        private ConcurrentStopwatch time = new ConcurrentStopwatch();

    }
}
