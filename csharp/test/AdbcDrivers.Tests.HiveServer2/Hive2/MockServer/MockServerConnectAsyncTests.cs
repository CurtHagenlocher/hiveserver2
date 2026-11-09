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

        [Fact]
        public async Task Standard_CallerCancellation_InterruptsSaslHandshake()
        {
            using var server = new RawSaslServer(respondWithError: false);
            using AdbcDatabase database = new HiveServer2Driver().Open(server.Parameters);

            using var cts = new CancellationTokenSource();
            Task<AdbcConnection> connect = database.ConnectAsync(server.Parameters, cts.Token);
            Assert.True(await server.WaitForHandshakeAsync(), "The server never received the SASL handshake.");
            cts.Cancel();

            Assert.Same(connect, await Task.WhenAny(connect, Task.Delay(s_waitLimit)));
            OperationCanceledException ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connect);
            Assert.Equal(cts.Token, ex.CancellationToken);
            Assert.True(await server.WaitForClientCloseAsync(), "The driver left the socket open after cancellation.");
        }

        [Fact]
        public async Task Standard_ConnectTimeout_DuringSaslHandshake_IsReportedAsTimeout()
        {
            using var server = new RawSaslServer(respondWithError: false);
            var parameters = MockServerScenario.CopyParameters(server.Parameters);
            parameters[HiveServer2Parameters.ConnectTimeoutMilliseconds] = "500";
            using AdbcDatabase database = new HiveServer2Driver().Open(parameters);

            using var cts = new CancellationTokenSource();
            Task<AdbcConnection> connect = database.ConnectAsync(parameters, cts.Token);

            Assert.Same(connect, await Task.WhenAny(connect, Task.Delay(s_waitLimit)));
            await Assert.ThrowsAsync<TimeoutException>(() => connect);
            Assert.True(await server.WaitForClientCloseAsync(), "The driver left the socket open after the timeout.");
        }

        [Fact]
        public async Task Standard_SaslFailure_ClosesSocket()
        {
            using var server = new RawSaslServer(respondWithError: true);
            using AdbcDatabase database = new HiveServer2Driver().Open(server.Parameters);

            Task<AdbcConnection> connect = database.ConnectAsync(server.Parameters);
            Assert.Same(connect, await Task.WhenAny(connect, Task.Delay(s_waitLimit)));
            await Assert.ThrowsAnyAsync<Exception>(() => connect);
            Assert.True(await server.WaitForClientCloseAsync(), "The driver left the socket open after SASL negotiation failed.");
        }

        /// <summary>
        /// A TCP server that accepts one connection and either never answers the SASL
        /// handshake or answers it with an error, and reports when the client closes the socket.
        /// </summary>
        private sealed class RawSaslServer : IDisposable
        {
            private const byte SaslStatusError = 0x04;

            private readonly System.Net.Sockets.TcpListener _listener;
            private readonly bool _respondWithError;
            private readonly TaskCompletionSource<bool> _handshakeReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly TaskCompletionSource<bool> _clientClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private System.Net.Sockets.TcpClient? _client;

            public RawSaslServer(bool respondWithError)
            {
                _respondWithError = respondWithError;
                _listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
                _listener.Start();
                int port = ((System.Net.IPEndPoint)_listener.LocalEndpoint).Port;
                var parameters = MockServerScenario.CopyParameters(HiveMockServer.DefaultStandardParameters);
                parameters[HiveServer2Parameters.HostName] = "127.0.0.1";
                parameters[HiveServer2Parameters.Port] = port.ToString(CultureInfo.InvariantCulture);
                Parameters = parameters;
                _ = Task.Run(ServeAsync);
            }

            public IReadOnlyDictionary<string, string> Parameters { get; }

            public async Task<bool> WaitForHandshakeAsync() =>
                await Task.WhenAny(_handshakeReceived.Task, Task.Delay(s_waitLimit)) == _handshakeReceived.Task;

            public async Task<bool> WaitForClientCloseAsync() =>
                await Task.WhenAny(_clientClosed.Task, Task.Delay(s_waitLimit)) == _clientClosed.Task;

            private async Task ServeAsync()
            {
                try
                {
                    _client = await _listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    var stream = _client.GetStream();
                    byte[] buffer = new byte[1024];
                    while (await stream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false) > 0)
                    {
                        if (_handshakeReceived.TrySetResult(true) && _respondWithError)
                        {
                            byte[] error = { SaslStatusError, 0, 0, 0, 0 };
                            await stream.WriteAsync(error, 0, error.Length).ConfigureAwait(false);
                        }
                    }
                }
                catch (Exception)
                {
                    // A reset connection counts as closed too.
                }
                _clientClosed.TrySetResult(true);
            }

            public void Dispose()
            {
                _listener.Stop();
                _client?.Dispose();
            }
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
