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

namespace AdbcDrivers.HiveServer2
{
    /// <summary>
    /// A cancellation source that fires when either a driver timeout elapses or the
    /// caller's token is cancelled, and remembers which of the two fired first.
    /// </summary>
    /// <remarks>
    /// Checking <c>callerToken.IsCancellationRequested</c> at catch time can't establish
    /// the cause: the caller may cancel after the timeout already interrupted the call,
    /// and a linked source can wake the operation before the caller's token reads as
    /// cancelled. Here the cause is recorded before <see cref="Token"/> is cancelled, so
    /// anything that observes the cancellation also sees the cause that triggered it.
    /// </remarks>
    internal sealed class TimeoutOrCallerTokenSource : IDisposable
    {
        private const int NotCancelled = 0;
        private const int CauseTimeout = 1;
        private const int CauseCaller = 2;

        private static readonly Action<object?> s_onTimeout = state => ((TimeoutOrCallerTokenSource)state!).Fire(CauseTimeout);
        private static readonly Action<object?> s_onCaller = state => ((TimeoutOrCallerTokenSource)state!).Fire(CauseCaller);

        private readonly CancellationTokenSource _combined = new();
        private readonly CancellationTokenSource _timer = new();
        private readonly CancellationTokenRegistration _timerRegistration;
        private readonly CancellationTokenRegistration _callerRegistration;
        private int _cause;

        public TimeoutOrCallerTokenSource(TimeSpan timeout, CancellationToken callerToken)
        {
            // Register on the caller first: if it is already cancelled the callback runs
            // synchronously here and the caller is recorded as the cause.
            _callerRegistration = callerToken.Register(s_onCaller, this);
            _timerRegistration = _timer.Token.Register(s_onTimeout, this);
            _timer.CancelAfter(timeout);
        }

        /// <summary>The token to pass to the operation.</summary>
        public CancellationToken Token => _combined.Token;

        /// <summary>True when the caller's token fired before the timeout.</summary>
        public bool IsCancelledByCaller => Volatile.Read(ref _cause) == CauseCaller;

        private void Fire(int cause)
        {
            if (Interlocked.CompareExchange(ref _cause, cause, NotCancelled) != NotCancelled)
            {
                return;
            }

            try
            {
                _combined.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Lost a race with Dispose; nothing is listening any more.
            }
        }

        public void Dispose()
        {
            // Disposing a registration waits for an in-flight callback to finish, so
            // _combined is not disposed underneath a running Fire.
            _callerRegistration.Dispose();
            _timerRegistration.Dispose();
            _timer.Dispose();
            _combined.Dispose();
        }
    }
}
