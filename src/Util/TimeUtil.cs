using System;
using System.Diagnostics;

namespace ricefan123.Timer.Util
{
    public static class NanoTime 
    {
        public class Timer
        {
            private Stopwatch watch = new Stopwatch();

            public void Start()
            {
                watch.Start();
            }
            public long NanoElapsed => watch.ElapsedTicks *  (Stopwatch.Frequency / 1000000000);
        }

        public static ulong FromMilliseconds(long milli) => ((ulong) milli) * 1000000;
        public static ulong FromSeconds(long sec) => ((ulong) sec) * 1000000000;

    }
}