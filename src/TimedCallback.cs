using System;
using System.Threading;
using ricefan123.Timer.Util;
using NLog;

namespace ricefan123.Timer
{
    public class TimedCallback
    {
        static Logger logger = LogManager.GetCurrentClassLogger();

        /// <summary>
        /// Atomically read current state with Thread-safe guarantee.
        /// </summary>
        private volatile int state;
        public const int STATE_INIT = 0;
        public const int STATE_WORKING = 1;
        public const int STATE_EXPIRED = 2;
        public const int STATE_CANCELED = 3;

        private long expiryTime;

        #region Constructor
        internal TimedCallback(Action callback, long expiryTime)
        {
            Callback = callback;
            Next = null;
            state = STATE_INIT;
            this.expiryTime = expiryTime;
        }

        #endregion

        public bool TryCancel()
        {
            return STATE_INIT == stateCompareExchange(STATE_CANCELED, STATE_INIT);
        }

        public bool IsCanceled => state == STATE_CANCELED;

        public long RemainingTime(long currentTime)
        {
            return expiryTime > currentTime ? expiryTime - currentTime : 0;
        }

        internal void Expire()
        {
            if (STATE_INIT != stateCompareExchange(STATE_WORKING, STATE_INIT))
                return;
            
            try {
                Callback();
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"HashedWheelTimer timout callback threw an exception.");
            }
            finally
            {
                stateCompareExchange(STATE_EXPIRED, STATE_WORKING);
            }
        }

        internal volatile TimedCallback Next;

        internal Action Callback { get; set; }
        public long ExpiryTime { get => expiryTime; set => expiryTime = value; }

        private int stateCompareExchange(int value, int comparand)
        {
            return Interlocked.CompareExchange(ref state, value, comparand);
        }
    }
}