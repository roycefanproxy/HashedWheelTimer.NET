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
        [InlineData(100, 97)]
        [InlineData(20, 13)]
        [InlineData(10, 5)]
        [InlineData(40, 30)]
        public void TestScheduleTimeoutShouldNotRunBeforeDelay(double expiryTime, double waitTime) 
        {
            var timer = new HashedWheelTimer();
            var barrier = new CountdownEvent(1);
            bool flag = false;
            Action cb = () => {
                flag = true;
                barrier.Signal();
            };
            TimedCallback timeout = timer.ScheduleTimeout(cb, TimeSpan.FromMilliseconds(expiryTime));
            Assert.False(barrier.Wait(TimeSpan.FromMilliseconds(waitTime)));
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

        [Theory]
        [InlineData(1000, 2000, 1000)]
        [InlineData(500, 2000, 1000)]
        [InlineData(100, 1000, 300)]
        public void TestCancelledTaskShouldNotBeExecuted(int interval, int timeout, int cancelDelayTime)
        {
            var timer = new HashedWheelTimer(TimeSpan.FromMilliseconds(interval));
            var barrier = new CountdownEvent(1);

            var timedCallback = timer.ScheduleTimeout(() => {
                barrier.Signal();
            }, timeout);
            Task.Delay(TimeSpan.FromMilliseconds(cancelDelayTime)).Wait();
            Assert.True(timedCallback.TryCancel());
            Assert.False(barrier.Wait(timeout));
            
        }

        [Fact]
        public void TestActivePendingTimeoutsShouldBeZero()  
        {
            CountdownEvent barrier = new CountdownEvent(1);
            var timer = new HashedWheelTimer();
            var t1 = timer.ScheduleTimeout(EmptyCallback(), TimeSpan.FromMinutes(100));
            var t2 = timer.ScheduleTimeout(EmptyCallback(), TimeSpan.FromMinutes(100));
            timer.ScheduleTimeout(() => 
            {
                barrier.Signal();
            }, TimeSpan.FromMilliseconds(90));

            Assert.Equal(3, timer.ActiveTimeoutsCount);
            Assert.True(t1.TryCancel());
            Assert.True(t2.TryCancel());
            barrier.Wait();

            Assert.Equal(0, timer.ActiveTimeoutsCount);
            timer.Stop();
        }

        [Fact]
        public void TestOverflow()
        {
            CountdownEvent barrier = new CountdownEvent(1);
            const int intervalMs = 50;
            var timer = new HashedWheelTimer(interval:TimeSpan.FromMilliseconds(intervalMs));
            TimeSpan superDureTimeSpan = TimeSpan.FromMilliseconds(intervalMs) * 0xffffffff;
            var timeout = timer.ScheduleTimeout(() =>
            {
                barrier.Signal();
            }, superDureTimeSpan);
            Assert.False(barrier.Wait(TimeSpan.FromSeconds(1)));
            Assert.True(timeout.TryCancel());
            timer.Stop();
        }

        [Theory]
        [InlineData(20, 200)]
        [InlineData(20, 100)]
        [InlineData(50, 300)]
        [InlineData(20, 10)]
        public void TestDelayTimeoutShouldNotLargerThanSingleTickDuration(int tickInterval, int timeout)
        {
            var watch = new ConcurrentStopwatch();
            var barrier = new CountdownEvent(1);
            var timer = new HashedWheelTimer(interval:TimeSpan.FromMilliseconds(tickInterval));
            long elapsed = 0;

            watch.Start();
            timer.ScheduleTimeout(() => 
            {
                Interlocked.Exchange(ref elapsed, watch.Elapsed);
                barrier.Signal();
            }, TimeSpan.FromMilliseconds(timeout));

            Assert.True(barrier.Wait(tickInterval * 2 + timeout), $"Elapsed: {NanoTime.ToMilliseconds(elapsed)}, ticks interval: {tickInterval}, timeout: {timeout}.");

        }

        public Action EmptyCallback()
        {
            return () => {};
        }
    }
}
