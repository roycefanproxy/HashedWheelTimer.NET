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

        #region Constructor
        internal TimedCallback(Action callback)
        {
            Callback = callback;
            Next = null;
            state = STATE_INIT;
        }

        #endregion

        public bool TryCancel()
        {
            return STATE_INIT == stateCompareExchange(STATE_CANCELED, STATE_INIT);
        }

        public bool IsCanceled()
        {
            return STATE_CANCELED == state;
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

        internal TimedCallback Next;

        internal TimedCallback Prev;

        internal Action Callback { get; set; }

        private int stateCompareExchange(int value, int comparand)
        {
            return Interlocked.CompareExchange(ref state, value, comparand);
        }
    }
}