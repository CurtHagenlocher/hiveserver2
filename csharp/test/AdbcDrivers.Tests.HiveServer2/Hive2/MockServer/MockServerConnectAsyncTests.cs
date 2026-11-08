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
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AdbcDrivers.HiveServer2.Hive2;
using AdbcDrivers.HiveServer2.TestServer;
using AdbcDrivers.Tests.HiveServer2.MockServer;
using Apache.Arrow;
using Apache.Arrow.Adbc;
using Xunit;

namespace AdbcDrivers.Tests.HiveServer2.Hive2.MockServer
{
    /// <summary>
    /// Exercises <see cref="AdbcDatabase.ConnectAsync"/>: it opens a working connection,
    /// the caller's token interrupts an in-flight session open, and the connect timeout
    /// is honored on both transports and still reported as a timeout.
    /// </summary>
    [Trait("Category", "MockServer")]
    public class MockServerConnectAsyncTests
    {
        private static readonly TimeSpan s_waitLimit = TimeSpan.FromSeconds(10);

        public enum Transport
        {
            Http,
            Standard,
        }

        [Theory]
        [InlineData(Transport.Http)]
        [InlineData(Transport.Standard)]
        public async Task ConnectAsync_OpensWorkingConnection(Transport transport)
        {
            using var server = new TestServer(transport, new HiveServer2StubHandler());
            using AdbcDatabase database = new HiveServer2Driver().Open(server.Parameters);

            using AdbcConnection connection = await database.ConnectAsync(server.Parameters);
            using AdbcStatement statement = connection.CreateStatement();
            statement.SqlQuery = "SELECT 42";
            QueryResult result = await statement.ExecuteQueryAsync();
            using var stream = result.Stream!;
            using RecordBatch? batch = await stream.ReadNextRecordBatchAsync();
            Assert.NotNull(batch);
            Assert.Equal(42L, Assert.IsType<Int64Array>(batch.Column(0)).GetValue(0));
        }

        [Theory]
        [InlineData(Transport.Http)]
        [InlineData(Transport.Standard)]
        public async Task CallerCancellation_InterruptsSessionOpen(Transport transport)
        {
            using var gate = new ServerGate();
            var stub = new HiveServer2StubHandler { OnOpenSession = _ => gate.Pass() };
            using var server = new TestServer(transport, stub);
            using AdbcDatabase database = new HiveServer2Driver().Open(server.Parameters);

            using var cts = new CancellationTokenSource();
            Task<AdbcConnection> connect = database.ConnectAsync(server.Parameters, cts.Token);
            try
            {
                Assert.True(await gate.WaitForRequestAsync(), "The server never received OpenSession.");
                cts.Cancel();
                Assert.Same(connect, await Task.WhenAny(connect, Task.Delay(s_waitLimit)));
                OperationCanceledException ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
                Assert.Equal(cts.Token, ex.CancellationToken);
                Assert.Equal(1, gate.RequestCount);
            }
            finally
            {
                gate.Release();
            }
        }

        [Theory]
        [InlineData(Transport.Http)]
        [InlineData(Transport.Standard)]
        public async Task PreCancelledToken_DoesNotReachServer(Transport transport)
        {
            using var gate = new ServerGate { Blocking = false };
            var stub = new HiveServer2StubHandler { OnOpenSession = _ => gate.Pass() };
            using var server = new TestServer(transport, stub);
            using AdbcDatabase database = new HiveServer2Driver().Open(server.Parameters);

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => database.ConnectAsync(server.Parameters, cts.Token));
            Assert.Equal(0, gate.RequestCount);
        }

        [Theory]
        [InlineData(Transport.Http)]
        [InlineData(Transport.Standard)]
        public async Task ConnectTimeout_IsReportedAsTimeoutNotCancellation(Transport transport)
        {
            using var gate = new ServerGate();
            var stub = new HiveServer2StubHandler { OnOpenSession = _ => gate.Pass() };
            using var server = new TestServer(transport, stub);
            var parameters = MockServerScenario.CopyParameters(server.Parameters);
            parameters[HiveServer2Parameters.ConnectTimeoutMilliseconds] = "500";
            using AdbcDatabase database = new HiveServer2Driver().Open(parameters);

            // A cancellable caller token that never fires: the timeout must still win.
            using var cts = new CancellationTokenSource();
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await Assert.ThrowsAsync<TimeoutException>(() => database.ConnectAsync(parameters, cts.Token));

                // Guards against the option being ignored (the Standard transport used to
                // fall back to the 30-second default regardless of the setting).
                Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(10), $"Connect timeout took {elapsed.Elapsed}; the configured 500 ms was not honored.");
            }
            finally
            {
                gate.Release();
            }
        }

        [Theory]
        [InlineData(Transport.Http)]
        [InlineData(Transport.Standard)]
        public void SyncConnect_StillOpensWorkingConnection(Transport transport)
        {
            var stub = new HiveServer2StubHandler();
            using var server = new TestServer(transport, stub);
            using AdbcDatabase database = new HiveServer2Driver().Open(server.Parameters);

            using AdbcConnection connection = database.Connect(server.Parameters);
            using var tableTypes = connection.GetTableTypes();
            Assert.NotNull(tableTypes);
        }

        /// <summary>An in-process server of either transport plus matching driver parameters.</summary>
        private sealed class TestServer : IDisposable
        {
            private readonly IDisposable _server;

            public TestServer(Transport transport, HiveServer2StubHandler stub)
            {
                if (transport == Transport.Http)
                {
                    var server = new HiveServer2TestServer(stub);
                    var parameters = MockServerScenario.CopyParameters(HiveMockServer.DefaultParameters);
                    parameters[AdbcOptions.Uri] = server.Uri.AbsoluteUri;
                    _server = server;
                    Parameters = parameters;
                }
                else
                {
                    var server = new HiveServer2StandardTestServer(stub);
                    var parameters = MockServerScenario.CopyParameters(HiveMockServer.DefaultStandardParameters);
                    parameters[HiveServer2Parameters.HostName] = server.HostName;
                    parameters[HiveServer2Parameters.Port] = server.Port.ToString(CultureInfo.InvariantCulture);
                    _server = server;
                    Parameters = parameters;
                }
            }

            public IReadOnlyDictionary<string, string> Parameters { get; }

            public void Dispose() => _server.Dispose();
        }

        /// <summary>
        /// Holds a stub RPC open on the server thread until released, and counts arrivals.
        /// </summary>
        private sealed class ServerGate : IDisposable
        {
            private readonly TaskCompletionSource<bool> _requestReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly ManualResetEventSlim _release = new(false);
            private int _requestCount;

            public bool Blocking { get; set; } = true;

            public int RequestCount => Volatile.Read(ref _requestCount);

            public void Pass()
            {
                Interlocked.Increment(ref _requestCount);
                _requestReceived.TrySetResult(true);
                if (Blocking && !_release.Wait(TimeSpan.FromSeconds(15)))
                {
                    throw new TimeoutException("The test did not release the server response.");
                }
            }

            public async Task<bool> WaitForRequestAsync() =>
                await Task.WhenAny(_requestReceived.Task, Task.Delay(s_waitLimit)) == _requestReceived.Task;

            public void Release() => _release.Set();

            public void Dispose() => _release.Dispose();
        }
    }
}
