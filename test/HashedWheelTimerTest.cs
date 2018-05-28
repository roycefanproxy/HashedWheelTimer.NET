using System;
using Xunit;
using ricefan123.Timer;
using System.Threading;
using System.Diagnostics;
using System.Threading.Tasks;
using ricefan123.Timer.Util;
using System.Collections.Concurrent;

namespace test
{
    public class HashedWheelTimerTest
    {
        [Theory]
        [InlineData(10, 9.7)]
        [InlineData(2, 1.3)]
        [InlineData(1, 0.5)]
        [InlineData(4, 3)]
        public void TestScheduleTimeoutShouldNotRunBeforeDelay(double expiryTime, double waitTime) 
        {
            var timer = new HashedWheelTimer();
            var barrier = new CountdownEvent(1);
            bool flag = false;
            Action cb = () => {
                flag = true;
                barrier.Signal();
            };
            TimedCallback timeout = timer.ScheduleTimeout(cb, TimeSpan.FromSeconds(expiryTime));
            Assert.False(barrier.Wait(TimeSpan.FromSeconds(waitTime)));
            Assert.False(timeout.IsExpired, "TimedTask should not be expired");
            Assert.False(flag);
            timer.Stop();
        }

        [Fact]
        public void TestScheduleTimeoutShouldRunAfterDelay() 
        {

            var timer = new HashedWheelTimer();
            var barrier = new CountdownEvent(1);
            Action cb = () => {
                Console.WriteLine("Test");
                barrier.Signal();;
            };
            TimedCallback timeout = timer.ScheduleTimeout(cb, TimeSpan.FromSeconds(4));
            Assert.False(timeout.IsExpired, "TimedTask should not expire");
            Assert.True(barrier.Wait(TimeSpan.FromSeconds(5)), "TimedTask should expire");
            timer.Stop();
        }       

        [Fact]
        public void TestStopTimer()  /* throws InterruptedException */ 
        {
            bool unprocessedFlag = false;
            bool processedFlag = false;
            Task.Run(() => 
            {
                CountdownEvent barrier = new CountdownEvent(3);
                var timerProcessed = new HashedWheelTimer();
                for (int i = 0; i < 3; i ++) {
                    timerProcessed.ScheduleTimeout(() => {
                        barrier.Signal();
                    }, TimeSpan.FromMilliseconds(1));
                }

                barrier.Wait();
                var timeouts = timerProcessed.Stop();
                unprocessedFlag = (0 == timeouts.Count);

                var timerUnprocessed = new HashedWheelTimer();
                for (int i = 0; i < 5; i ++) {
                    timerUnprocessed.ScheduleTimeout(() => {}, TimeSpan.FromSeconds(5));
                }
                Thread.Sleep(TimeSpan.FromSeconds(1));
                processedFlag = (0 != timerUnprocessed.Stop().Count);
            });
            Task.Delay(TimeSpan.FromSeconds(3)).Wait();
            Assert.True(unprocessedFlag, "unprocessedFlag");
            Assert.True(processedFlag, "processedFlag");
        }

        [Fact]
        public void TestTimerShouldThrowExceptionAfterShutdownForNewTimeouts()         
        {
            bool flag = false;
            Task.Run(() => {
                CountdownEvent barrier = new CountdownEvent(3);
                var timer = new HashedWheelTimer();
                for (int i = 0; i < 3; i ++) {
                    timer.ScheduleTimeout(() => 
                    {
                        barrier.Signal();
                    }, TimeSpan.FromMilliseconds(1));
                }

                barrier.Wait();
                timer.Stop();

                try
                {
                    timer.ScheduleTimeout(() => {}, TimeSpan.FromMilliseconds(1));
                }
                catch (InvalidOperationException)
                {
                    flag = true;
                }
            });
            Task.Delay(TimeSpan.FromSeconds(3)).Wait();
            Assert.True(flag, "Expected exception didn't occur");
        }

        [Fact]
        public void TestExecutionOnTime() 
        {
            var tickDuration = TimeSpan.FromMilliseconds(200);
            var timeout = TimeSpan.FromMilliseconds(125);
            var nanoTimeout = NanoTime.FromMilliseconds(125);
            var maxTimeout = 2 * (tickDuration + timeout);
            var nanoMaxTimeout = NanoTime.FromMilliseconds(maxTimeout.TotalMilliseconds);
            HashedWheelTimer timer = new HashedWheelTimer(tickDuration);
            var queue = new BlockingCollection<long>();

            var watch = new ConcurrentStopwatch();
            watch.Start();
            int scheduledTasks = 100000;
            for (int i = 0; i < scheduledTasks; i++) 
            {
                var start = watch.Elapsed;
                timer.ScheduleTimeout(() => 
                {
                    queue.Add(watch.Elapsed - start);
                }, timeout);
            }

            for (int i = 0; i < scheduledTasks; i++) {
                long delay = queue.Take();
                Assert.True(delay >= nanoTimeout && delay < nanoMaxTimeout, i + ": Timeout + " + scheduledTasks + " delay " + delay + " must be " + timeout + " < " + maxTimeout);
            }

            timer.Stop();
        }
    }
}
