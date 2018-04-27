using System;
using System.Threading;

namespace ricefan123.Timer
{
    internal class HashedWheelSlot
    {
        // volatile is so confusing in .NET
        private volatile TimedCallback first = null;

        internal void Push(TimedCallback callback)
        {
            do {
                callback.Next = first;
            }
            while (Interlocked.CompareExchange(ref first, callback, callback.Next) != callback.Next);
        }


        internal TimedCallback TryPop()
        {
            TimedCallback retVal;

            do {
                retVal = first;
            }
            while (retVal != null && Interlocked.CompareExchange(ref first, retVal.Next, retVal) != retVal);

            return retVal;
        }

        internal void Clear()
        {
            first = null;
        }


    }
}