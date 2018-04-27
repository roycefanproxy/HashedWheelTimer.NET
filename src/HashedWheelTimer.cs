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
            SetSleep(policy);
        }

        #endregion

        #region Public Methods

        public void ScheduleTimeout(Action callback, TimeSpan timeout)
        {
            ScheduleTimeoutImpl(callback, timeout);
        }


        #endregion

        #region Private Methods

        private void ScheduleTimeoutImpl(Action callback, TimeSpan timeout)
        {
            Interlocked.Increment(ref timeoutsCount);
            Start();
        }

        private void Start()
        {

        }

        private void DefaultInitialize()
        {
            DefaultTimeout = TimeSpan.FromSeconds(10);
            TicksInterval = TimeSpan.FromMilliseconds(10);
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

        #endregion

        public int TimeoutsCount { get { return timeoutsCount; } }

        public int timeoutsCount = 0;

        private static readonly uint WHEEL_BUCKETS = 4;
        private static readonly int WHEEL_BITS = 8;
        private static readonly uint WHEEL_SIZE = (1u << WHEEL_BITS);

        private static readonly uint WHEEL_MASK = WHEEL_SIZE - 1;


        private LinkedList<Action>[,] buckets = new LinkedList<Action>[WHEEL_BUCKETS, WHEEL_SIZE];
        private LinkedList<Action> timeouts = new LinkedList<Action>();

        private SleepMethods.SleepFunc Sleep { get; set; }

        private TimeSpan DefaultTimeout { get; set; }

        private TimeSpan TicksInterval { get; set; }

        public LinkedList<Action>[,] Buckets { get => buckets; set => buckets = value; }
        public LinkedList<Action>[,] Buckets1 { get => buckets; set => buckets = value; }
    }
}
