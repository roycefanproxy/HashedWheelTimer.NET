using System;
using System.Threading;
using System.Threading.Tasks;
using ricefan123.Timer.Util;
using NLog;

namespace ricefan123.Timer
{
    public class TimedCallback
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        public static readonly int STATE_INIT = 0;
        public static readonly int STATE_WORKING = 1;
        public static readonly int STATE_EXPIRED = 2;
        public static readonly int STATE_CANCELED = 3;

        public TimedCallback(Action callback)
        {
            Callback = callback;
            Next = null;
            state = STATE_INIT;
        }

        public bool TryCancel()
        {
            return STATE_INIT == StateCompareExchange(STATE_CANCELED, STATE_INIT);
        }

        /// <summary>
        /// Atomically read current state with Thread-safe guarantee.
        /// </summary>
        public int State => StateCompareExchange(0, 0);

        internal void Expire()
        {
            if (STATE_INIT != StateCompareExchange(STATE_WORKING, STATE_INIT))
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
                StateCompareExchange(STATE_EXPIRED, STATE_WORKING);
            }
        }

        private int state;

        internal TimedCallback Next;

        internal TimedCallback Prev;

        internal Action Callback { get; set; }

        private int StateCompareExchange(int value, int comparand)
        {
            return Interlocked.CompareExchange(ref state, value, comparand);
        }
    }
}