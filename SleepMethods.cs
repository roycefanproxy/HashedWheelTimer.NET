using System;
using System.Threading;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace ricefan123.Timer.Util
{
    public enum SleepPolicy { Default, Native, Precise };
    public static class SleepMethods
    {
        public delegate void SleepFunc(int milliseconds);
        
        public static SleepFunc NativeSleep => new SleepFunc(NativeSleepImpl);

        public static SleepFunc PreciseSleep => new SleepFunc(PreciseSleepImpl);

        private static void PreciseSleepImpl(int milliseconds)
        {
            if (milliseconds < 0)
                return;

            var watch = new Stopwatch();
            double freq_per_ms = Stopwatch.Frequency;

            watch.Start();
            if (IsUnix())
                Thread.Sleep(milliseconds - 1);
            else if (IsWindows())
                Thread.Sleep(milliseconds - 16);
            else 
                Thread.Sleep(milliseconds - (16 / 2));
            while ((watch.ElapsedTicks / freq_per_ms) < milliseconds)
                continue;
            
        }

        private static void NativeSleepImpl(int milliseconds)
        {
            if (milliseconds < 0)
                return;

            Thread.Sleep(milliseconds);
        }

        private static bool IsUnix() => RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || 
                                        RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        
        private static bool IsWindows() => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }
}