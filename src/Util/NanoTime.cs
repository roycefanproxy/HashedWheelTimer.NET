using System;
using System.Diagnostics;

namespace ricefan123.Timer.Util
{
    public static class NanoTime 
    {

        public static long FromMilliseconds(double milli) => (long)(milli * 1000000);
        public static long FromSeconds(long sec) => sec * 1000000000;

        public static int ToMilliseconds(long nano) => (int) nano / 1000000;
    }

    public class ConcurrentStopwatch
    {
        private object l = new object();
        private Stopwatch watch = new Stopwatch();

        public void Start()
        {
            watch.Start();
        }
        public long Elapsed
        {
            get
            {
                double elapsed;
                lock (l)
                {
                    elapsed = watch.ElapsedTicks;
                }
                return (long) elapsed * Stopwatch.Frequency / 1000000000;
            }
        }
    }
}