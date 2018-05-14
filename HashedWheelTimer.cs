using System;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.CompilerServices;
using ricefan123.Timer.Util;

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
            TicksInterval = NanoTime.FromMilliseconds(interval.Milliseconds);
            DefaultTimeout = NanoTime.FromMilliseconds(timeout.Milliseconds);
            ticked = 0;
            workerThread = null;
            workerWorkingFlag = new CountdownEvent(1);
            SetSleep(policy);
            InitializeSlots();
        }

        #endregion

        #region Public Methods

        public TimedCallback ScheduleTimeout(Action action, TimeSpan timeout)
        {
            var ms = timeout.Milliseconds;
            if (ms < 0)
                throw new ArgumentException("Expiry time cannot be negative.", "timeout");
            var nanoTimeout = NanoTime.FromMilliseconds(ms);
            var callback = new TimedCallback(action, time.Elapsed + nanoTimeout);
            Interlocked.Increment(ref timeoutsCount);
            ScheduleTimeoutImpl(callback, nanoTimeout);

            return callback;
        }


        #endregion

        #region Private Methods

        private void ScheduleTimeoutImpl(TimedCallback callback, long nanoseconds)
        {
            var diff = ToWheelTicks(nanoseconds);
            var deadline = CalculateDeadline();
            var due = diff + deadline;
            var dueMs = NanoTime.ToMilliseconds(due);
            HashedWheelSlot slot;

            if (diff < WHEEL_SIZE)
            {
                slot = slots[0, dueMs & WHEEL_MASK];
            }
            else if (diff < 1 << (2 * WHEEL_BITS))
            {
                slot = slots[1, (dueMs >> WHEEL_BITS) & WHEEL_MASK];
            }
            else if (diff < 1 << (3 * WHEEL_BITS))
            {
                slot = slots[2, (dueMs >> 2 * WHEEL_BITS) & WHEEL_MASK];
            }
            else
            {
                if (diff > 0xffffffff)
                {
                    diff = 0xffffffff;
                    dueMs = NanoTime.ToMilliseconds(diff + deadline);
                }
                slot = slots[3, (dueMs >> 3 * WHEEL_BITS) & WHEEL_MASK];
            }
            slot.Push(callback);


            /// TODO
            // tobe deleted.
            StartWorking();
        }

        private void ExpireTimeouts()
        {
            var timeouts = slots[0, ticked];

            var wheel0Index = (ticked & WHEEL_MASK);
            if (wheel0Index == 0)
            {
                if (CascadeTimers(1, (ticked >> WHEEL_BITS) & WHEEL_MASK) &&
                    CascadeTimers(2, (ticked >> (2 * WHEEL_BITS)) & WHEEL_MASK))
                    CascadeTimers(3, (ticked >> (3 * WHEEL_BITS)) & WHEEL_MASK);
            }


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


        private void StartWorking()
        {
            switch (workerState)
            {
            case WORKER_INIT:
                if (Interlocked.CompareExchange(ref workerState, WORKER_STARTED, WORKER_INIT) == WORKER_INIT)
                    workerThread.Start();
                break;
            case WORKER_STARTED:
                break;
            case WORKER_KILLED:
                throw new InvalidOperationException("Cannot start a killed thread.");
            default:
                throw new InvalidOperationException("Unknown worker state.");
            }

            workerWorkingFlag.Signal();
        }

        private void DefaultInitialize()
        {
            DefaultTimeout = NanoTime.FromSeconds(10);
            TicksInterval = NanoTime.FromMilliseconds(10);
            workerThread = null;
            workerWorkingFlag = new CountdownEvent(1);
            ticked = 0;
            InitializeSlots();
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
            workerWorkingFlag.Wait();
            time.Start();

            while (workerState != WORKER_KILLED) 
            {
                ExpireTimeouts();
                var sleepTime =  - time.Elapsed;
                // tick before sleep so that timeout added while sleeping
                // will be delayed by a tick to avoid early wake up.
                Interlocked.Increment(ref ticked);
                Sleep(NanoTime.ToMilliseconds(sleepTime));
            }
        }

        #endregion

        public int TimeoutsCount => Interlocked.CompareExchange(ref timeoutsCount, 0, 0);

        CountdownEvent workerWorkingFlag;
        public int timeoutsCount = 0;

        private static readonly uint WHEEL_BUCKETS = 4;
        private static readonly int WHEEL_BITS = 8;
        private static readonly uint WHEEL_SIZE = (1u << WHEEL_BITS);

        private static readonly uint WHEEL_MASK = WHEEL_SIZE - 1;

        private Thread workerThread;

        private volatile int workerState;
        
        private const int WORKER_INIT = 0;
        private const int WORKER_STARTED = 1;
        private const int WORKER_KILLED = 2;
        
        private HashedWheelSlot[,] slots;
        private LinkedList<Action> timeouts = new LinkedList<Action>();

        private SleepMethods.SleepFunc Sleep { get; set; }

        private long ticked;

        private long DefaultTimeout { get; set; }

        /// <summary>
        /// In nanosecond scale.
        /// </summary>
        private long TicksInterval { get; set; }

        private readonly ulong startTime;

        private HighResolutionTimer time = new HighResolutionTimer();

    }
}
