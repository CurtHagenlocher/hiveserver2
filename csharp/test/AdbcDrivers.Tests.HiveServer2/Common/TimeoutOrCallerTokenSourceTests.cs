/*
 * Copyright (c) 2026 ADBC Drivers Contributors
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *    http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Threading;
using AdbcDrivers.HiveServer2;
using Xunit;

namespace AdbcDrivers.Tests.HiveServer2.Common
{
    public class TimeoutOrCallerTokenSourceTests
    {
        private static readonly TimeSpan s_long = TimeSpan.FromMinutes(5);

        [Fact]
        public void CallerCancel_IsReportedAsCaller()
        {
            using var caller = new CancellationTokenSource();
            using var source = new TimeoutOrCallerTokenSource(s_long, caller.Token);

            caller.Cancel();

            Assert.True(source.Token.IsCancellationRequested);
            Assert.True(source.IsCancelledByCaller);
        }

        [Fact]
        public void Timeout_IsNotReportedAsCaller()
        {
            using var caller = new CancellationTokenSource();
            using var source = new TimeoutOrCallerTokenSource(TimeSpan.FromMilliseconds(1), caller.Token);

            Assert.True(SpinWait.SpinUntil(() => source.Token.IsCancellationRequested, TimeSpan.FromSeconds(10)));
            Assert.False(source.IsCancelledByCaller);
        }

        [Fact]
        public void CallerCancelAfterTimeout_StillReportedAsTimeout()
        {
            using var caller = new CancellationTokenSource();
            using var source = new TimeoutOrCallerTokenSource(TimeSpan.FromMilliseconds(1), caller.Token);
            Assert.True(SpinWait.SpinUntil(() => source.Token.IsCancellationRequested, TimeSpan.FromSeconds(10)));

            // The caller cancelling while the timed-out operation is still unwinding
            // must not change the recorded cause.
            caller.Cancel();

            Assert.False(source.IsCancelledByCaller);
        }

        [Fact]
        public void PreCancelledCaller_IsReportedAsCallerImmediately()
        {
            using var caller = new CancellationTokenSource();
            caller.Cancel();

            using var source = new TimeoutOrCallerTokenSource(s_long, caller.Token);

            Assert.True(source.Token.IsCancellationRequested);
            Assert.True(source.IsCancelledByCaller);
        }

        [Fact]
        public void CauseIsVisibleToAnythingObservingTheToken()
        {
            using var caller = new CancellationTokenSource();
            using var source = new TimeoutOrCallerTokenSource(s_long, caller.Token);
            bool? causeSeenByObserver = null;
            using CancellationTokenRegistration _ = source.Token.Register(() => causeSeenByObserver = source.IsCancelledByCaller);

            caller.Cancel();

            Assert.True(causeSeenByObserver);
        }

        [Fact]
        public void UncancellableCaller_OnlyTimeoutCanFire()
        {
            using var source = new TimeoutOrCallerTokenSource(s_long, CancellationToken.None);

            Assert.True(source.Token.CanBeCanceled);
            Assert.False(source.Token.IsCancellationRequested);
            Assert.False(source.IsCancelledByCaller);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DisposeFromInsideCancellationCallback_DoesNotThrowOrDeadlock(bool callerFires)
        {
            // Cancelling a token can run an awaiting operation's continuation inline, so
            // the operation can finish and dispose the source while Fire is still on the
            // stack. That re-entrant dispose must neither deadlock (waiting on its own
            // callback) nor throw out of Cancel().
            using var caller = new CancellationTokenSource();
            var source = new TimeoutOrCallerTokenSource(callerFires ? s_long : TimeSpan.FromMilliseconds(1), caller.Token);
            var disposed = new ManualResetEventSlim(false);
            source.Token.Register(() =>
            {
                source.Dispose();
                disposed.Set();
            });

            if (callerFires)
            {
                caller.Cancel();
            }

            Assert.True(disposed.Wait(TimeSpan.FromSeconds(10)), "Re-entrant dispose did not complete.");
            if (!callerFires)
            {
                caller.Cancel();
            }
        }

        [Fact]
        public void RacingCallerAndTimeout_CauseNeverChangesOnceObserved()
        {
            // Stress the race Copilot flagged: whichever source wins, the cause seen by
            // an observer of the token must be the cause reported afterwards.
            for (int i = 0; i < 500; i++)
            {
                using var caller = new CancellationTokenSource();
                using var source = new TimeoutOrCallerTokenSource(TimeSpan.FromMilliseconds(1), caller.Token);
                bool? observed = null;
                var seen = new ManualResetEventSlim(false);
                using CancellationTokenRegistration _ = source.Token.Register(() =>
                {
                    observed = source.IsCancelledByCaller;
                    seen.Set();
                });

                ThreadPool.QueueUserWorkItem(_ => caller.Cancel());

                Assert.True(seen.Wait(TimeSpan.FromSeconds(10)));
                Assert.Equal(observed, source.IsCancelledByCaller);
                SpinWait.SpinUntil(() => caller.IsCancellationRequested, TimeSpan.FromSeconds(10));
                Assert.Equal(observed, source.IsCancelledByCaller);
            }
        }

        [Fact]
        public void CallerCancelAfterDispose_DoesNotThrow()
        {
            using var caller = new CancellationTokenSource();
            var source = new TimeoutOrCallerTokenSource(s_long, caller.Token);
            source.Dispose();

            caller.Cancel();
        }
    }
}
