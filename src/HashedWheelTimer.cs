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
            workerThread = null;
            workerWorkingFlag = new CountdownEvent(1);
            SetSleep(policy);
            InitializeSlots();
        }

        #endregion

        #region Public Methods

        public TimedCallback ScheduleTimeout(Action action, TimeSpan timeout)
        {
            var signedms = timeout.Milliseconds;
            if (signedms < 0)
                throw new ArgumentException("Expiry time cannot be negative.", "timeout");
            ulong ms = (ulong) signedms;
            var callback = new TimedCallback(action, NanoTime.FromMilliseconds(ms));
            ScheduleTimeoutImpl(callback, ms);

            return callback;
        }


        #endregion

        #region Private Methods

        private void ScheduleTimeoutImpl(TimedCallback callback, ulong milliseconds)
        {
            Interlocked.Increment(ref timeoutsCount);

            var nextTick = CalculateNextTick();
            var diff = ToWheelTicks(milliseconds);
            var due = diff + nextTick;

            var index = nextTick & WHEEL_MASK;



            StartWorking();
        }

        private bool CascadeTimers(int hierarchyIdx, ulong idx)
        {
            // TODO
            // what if newly added timer has been assigned to cascading slot?

            var slot = new HashedWheelSlot(slots[hierarchyIdx, idx].Clear()); 

            for (TimedCallback callback = slot.TryPop(); 
                 callback != null;
                 callback = slot.TryPop())
            {
                ScheduleTimeoutImpl(callback, callback.RemainingTime(currentTick));
            }
            

        }

        private ulong CalculateNextTick()
        {
            return prevTicks + TicksInterval;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong ToWheelTicks(ulong milliTimeout)
        {
             return milliTimeout / TicksInterval;
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
            InitializeSlots();
        }

        private void InitializeSlots()
        {
            slots = new HashedWheelSlot[WHEEL_BUCKETS, WHEEL_SIZE];

            for (int i = 0; i != WHEEL_BUCKETS; ++i)
                for (int j = 0; j != WHEEL_SIZE; ++j)
                    slots[i, j] = new HashedWheelSlot();
            currentTick = 0;
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
                var timeouts = slots[0, currentTick];

                var wheel0Index = (currentTick & WHEEL_MASK);
                if (wheel0Index == 0)
                {
                    if (CascadeTimers(1, (currentTick >> WHEEL_BITS) & WHEEL_MASK) &&
                        CascadeTimers(2, (currentTick >> (2 * WHEEL_BITS)) & WHEEL_MASK))
                        CascadeTimers(3, (currentTick >> (3 * WHEEL_BITS)) & WHEEL_MASK);
                }



                for (var timeout = timeouts.TryPop(); timeout != null; timeout = timeouts.TryPop())
                {
                    timeout.Expire();
                }

                ++currentTick;

                
            }
        }

        #endregion

        public int TimeoutsCount { get { return timeoutsCount; } }

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

        private ulong currentTick;

        private ulong DefaultTimeout { get; set; }

        /// <summary>
        /// In nanosecond scale.
        /// </summary>
        private ulong TicksInterval { get; set; }

        private ulong prevTicks;

        private NanoTime.Timer time = new NanoTime.Timer();

    }
}
