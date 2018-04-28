using System;
using System.Collections.Generic;
using System.Threading;
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
            TicksInterval = interval;
            DefaultTimeout = timeout;
            workerThread = null;
            workerWorkingFlag = new CountdownEvent(1);
            SetSleep(policy);
            InitializeSlots();
        }

        #endregion

        #region Public Methods

        public TimedCallback ScheduleTimeout(Action action, TimeSpan timeout)
        {
            var callback = new TimedCallback(action);
            ScheduleTimeoutImpl(callback, timeout);

            return callback;
        }


        #endregion

        #region Private Methods

        private void ScheduleTimeoutImpl(TimedCallback callback, TimeSpan timeout)
        {
            Interlocked.Increment(ref timeoutsCount);
            StartWorking();
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

            workerWorkingFlag.Wait();
        }

        private void DefaultInitialize()
        {
            DefaultTimeout = TimeSpan.FromSeconds(10);
            TicksInterval = TimeSpan.FromMilliseconds(10);
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
            workerWorkingFlag.Signal();
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

        private TimeSpan DefaultTimeout { get; set; }

        private TimeSpan TicksInterval { get; set; }
    }
}
