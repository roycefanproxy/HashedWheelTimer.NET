using System;
using System.Threading;

namespace ricefan123.Timer
{
    internal class HashedWheelSlot
    {
        // volatile is so confusing in .NET
        private TimedCallback first = null;

        internal HashedWheelSlot() {}

        internal HashedWheelSlot(TimedCallback firstItem)
        {
            first = firstItem;
        }


        internal void Push(TimedCallback callback)
        {
            do {
                callback.Next = Interlocked.CompareExchange(ref first, null, null);
            }
            while (Interlocked.CompareExchange(ref first, callback, callback.Next) != callback.Next);
        }


        internal TimedCallback TryPop()
        {
            TimedCallback retVal;

            do {
                retVal = Interlocked.CompareExchange(ref first, null, null);
            }
            while (retVal != null && Interlocked.CompareExchange(ref first, retVal.Next, retVal) != retVal);
            if (retVal != null)
                retVal.Next = null;

            return retVal;
        }

        internal TimedCallback Clear()
        {
            TimedCallback currentFirst;
            do
            {
                currentFirst = first;
                currentFirst = Interlocked.CompareExchange(ref first, null, null);
            }
            while (Interlocked.CompareExchange(ref first, null, currentFirst) != currentFirst);

            return currentFirst;
        }

    }
}