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
using System.Threading;
using System.Threading.Tasks;
using AdbcDrivers.HiveServer2;
using AdbcDrivers.HiveServer2.Hive2;
using AdbcDrivers.HiveServer2.TestServer;
using AdbcDrivers.Tests.HiveServer2.MockServer;
using Apache.Arrow;
using Apache.Arrow.Adbc;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using Xunit;

namespace AdbcDrivers.Tests.HiveServer2.Hive2.MockServer
{
    /// <summary>
    /// Exercises the async connection metadata overrides (<c>GetObjectsAsync</c>,
    /// <c>GetTableSchemaAsync</c>, <c>GetTableTypesAsync</c>): results match the
    /// sync path, the caller's <see cref="CancellationToken"/> interrupts an
    /// in-flight call, and caller cancellation is reported distinctly from the
    /// driver's own query timeout.
    /// </summary>
    [Trait("Category", "MockServer")]
    public class MockServerMetadataAsyncTests
    {
        private static readonly TimeSpan s_waitLimit = TimeSpan.FromSeconds(10);

        public enum MetadataCall
        {
            GetObjects,
            GetTableSchema,
            GetTableTypes,
        }

        [Fact]
        public async Task GetTableTypesAsync_ReturnsConfiguredTypes()
        {
            using var scenario = HiveMockServer.Create();
            scenario.Stub.OnGetTableTypes = _ => TableTypesResult();

            using IArrowArrayStream stream = await scenario.Connection.GetTableTypesAsync();
            using RecordBatch? batch = await stream.ReadNextRecordBatchAsync();
            Assert.NotNull(batch);
            var types = Assert.IsType<StringArray>(batch.Column(0));
            Assert.Equal(new[] { "TABLE", "VIEW" }, new[] { types.GetString(0), types.GetString(1) });
        }

        [Fact]
        public async Task GetTableSchemaAsync_RoundTripsServerColumns()
        {
            using var scenario = HiveMockServer.Create();
            scenario.Stub.OnGetColumns = _ => ColumnsResult();

            Schema schema = await scenario.Connection.GetTableSchemaAsync("main", "public", "events");
            Field field = Assert.Single(schema.FieldsList);
            Assert.Equal("id", field.Name);
            Assert.IsType<Int64Type>(field.DataType);
        }

        [Fact]
        public async Task GetObjectsAsync_CatalogsDepth_ReturnsCatalogList()
        {
            using var scenario = HiveMockServer.Create();
            scenario.Stub.OnGetCatalogs = _ => CatalogsResult();

            using IArrowArrayStream stream = await scenario.Connection.GetObjectsAsync(
                AdbcConnection.GetObjectsDepth.Catalogs, null, null, null, null, null);
            using RecordBatch? batch = await stream.ReadNextRecordBatchAsync();
            Assert.NotNull(batch);
            var catalogs = Assert.IsType<StringArray>(batch.Column(0));
            Assert.Equal("main", catalogs.GetString(0));
        }

        [Theory]
        [InlineData(MetadataCall.GetObjects)]
        [InlineData(MetadataCall.GetTableSchema)]
        [InlineData(MetadataCall.GetTableTypes)]
        public async Task CallerCancellation_InterruptsInFlightCall(MetadataCall call)
        {
            using var scenario = HiveMockServer.Create();
            using var gate = new ServerGate();
            ConfigureStub(scenario.Stub, gate);

            using var cts = new CancellationTokenSource();
            Task operation = Invoke(scenario.Connection, call, cts.Token);
            try
            {
                Assert.True(await gate.WaitForRequestAsync(), "The server never received the metadata request.");
                cts.Cancel();
                Assert.Same(operation, await Task.WhenAny(operation, Task.Delay(s_waitLimit)));
                OperationCanceledException ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
                Assert.Equal(cts.Token, ex.CancellationToken);
            }
            finally
            {
                gate.Release();
            }

            // The connection must stay usable after a caller-initiated cancel.
            gate.Blocking = false;
            await Invoke(scenario.Connection, call, CancellationToken.None);
        }

        [Theory]
        [InlineData(MetadataCall.GetObjects)]
        [InlineData(MetadataCall.GetTableSchema)]
        [InlineData(MetadataCall.GetTableTypes)]
        public async Task PreCancelledToken_DoesNotReachServer(MetadataCall call)
        {
            using var scenario = HiveMockServer.Create();
            using var gate = new ServerGate { Blocking = false };
            ConfigureStub(scenario.Stub, gate);

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Invoke(scenario.Connection, call, cts.Token));
            Assert.Equal(0, gate.RequestCount);
        }

        [Theory]
        [InlineData(MetadataCall.GetObjects)]
        [InlineData(MetadataCall.GetTableSchema)]
        [InlineData(MetadataCall.GetTableTypes)]
        public async Task QueryTimeout_IsReportedAsTimeoutNotCancellation(MetadataCall call)
        {
            var parameters = MockServerScenario.CopyParameters(HiveMockServer.DefaultParameters);
            parameters[ApacheParameters.QueryTimeoutSeconds] = "1";
            using var scenario = HiveMockServer.Create(parameters: parameters);
            using var gate = new ServerGate();
            ConfigureStub(scenario.Stub, gate);

            // A cancellable caller token that never fires: the timeout must still
            // win, and must not be misreported as a caller cancellation.
            using var cts = new CancellationTokenSource();
            try
            {
                await Assert.ThrowsAsync<TimeoutException>(() => Invoke(scenario.Connection, call, cts.Token));
            }
            finally
            {
                gate.Release();
            }
        }

        // Over the Standard (TCP) transport, cancelling an in-flight RPC closes the
        // socket (ThreadSafeClient can't otherwise guarantee the framed stream is
        // intact), so the connection is unusable afterwards. Caller cancellation of a
        // metadata call now reaches the RPC, so it has the same effect as
        // Statement.Cancel() does for queries.

        [Theory]
        [InlineData(MetadataCall.GetObjects)]
        [InlineData(MetadataCall.GetTableSchema)]
        [InlineData(MetadataCall.GetTableTypes)]
        public async Task Standard_CallerCancellation_InterruptsCallAndInvalidatesConnection(MetadataCall call)
        {
            using var scenario = HiveMockServer.CreateStandard();
            using var gate = new ServerGate();
            ConfigureStub(scenario.Stub, gate);

            using var cts = new CancellationTokenSource();
            Task operation = Invoke(scenario.Connection, call, cts.Token);
            try
            {
                Assert.True(await gate.WaitForRequestAsync(), "The server never received the metadata request.");
                cts.Cancel();
                Assert.Same(operation, await Task.WhenAny(operation, Task.Delay(s_waitLimit)));
                OperationCanceledException ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
                Assert.Equal(cts.Token, ex.CancellationToken);
            }
            finally
            {
                gate.Release();
            }

            gate.Blocking = false;
            int requestsBefore = gate.RequestCount;
            HiveServer2Exception error = await Assert.ThrowsAsync<HiveServer2Exception>(
                () => Invoke(scenario.Connection, call, CancellationToken.None));
            Assert.Equal(AdbcStatusCode.InvalidState, error.Status);
            Assert.Equal(requestsBefore, gate.RequestCount);
        }

        [Theory]
        [InlineData(MetadataCall.GetObjects)]
        [InlineData(MetadataCall.GetTableSchema)]
        [InlineData(MetadataCall.GetTableTypes)]
        public async Task Standard_PreCancelledToken_LeavesConnectionUsable(MetadataCall call)
        {
            using var scenario = HiveMockServer.CreateStandard();
            using var gate = new ServerGate { Blocking = false };
            ConfigureStub(scenario.Stub, gate);

            using var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Invoke(scenario.Connection, call, cts.Token));
            Assert.Equal(0, gate.RequestCount);

            // Nothing was written to the socket, so there is nothing to invalidate.
            await Invoke(scenario.Connection, call, CancellationToken.None);
            Assert.Equal(1, gate.RequestCount);
        }

        [Theory]
        [InlineData(MetadataCall.GetObjects)]
        [InlineData(MetadataCall.GetTableSchema)]
        [InlineData(MetadataCall.GetTableTypes)]
        public async Task Standard_QueryTimeout_IsReportedAsTimeoutNotCancellation(MetadataCall call)
        {
            var parameters = MockServerScenario.CopyParameters(HiveMockServer.DefaultStandardParameters);
            parameters[ApacheParameters.QueryTimeoutSeconds] = "1";
            using var scenario = HiveMockServer.CreateStandard(parameters: parameters);
            using var gate = new ServerGate();
            ConfigureStub(scenario.Stub, gate);

            using var cts = new CancellationTokenSource();
            try
            {
                await Assert.ThrowsAsync<TimeoutException>(() => Invoke(scenario.Connection, call, cts.Token));
            }
            finally
            {
                gate.Release();
            }
        }

        private static Task Invoke(AdbcConnection connection, MetadataCall call, CancellationToken cancellationToken) => call switch
        {
            MetadataCall.GetObjects => connection.GetObjectsAsync(
                AdbcConnection.GetObjectsDepth.Catalogs, null, null, null, null, null, cancellationToken),
            MetadataCall.GetTableSchema => connection.GetTableSchemaAsync("main", "public", "events", cancellationToken),
            MetadataCall.GetTableTypes => connection.GetTableTypesAsync(cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(call)),
        };

        private static void ConfigureStub(HiveServer2StubHandler stub, ServerGate gate)
        {
            stub.OnGetCatalogs = _ => gate.Pass(CatalogsResult);
            stub.OnGetColumns = _ => gate.Pass(ColumnsResult);
            stub.OnGetTableTypes = _ => gate.Pass(TableTypesResult);
        }

        private static MockResult CatalogsResult() =>
            MockResult.Builder().String("TABLE_CAT", "main").Build();

        private static MockResult TableTypesResult() =>
            MockResult.Builder().String("TABLE_TYPE", "TABLE", "VIEW").Build();

        private static MockResult ColumnsResult() =>
            MockResult.Builder()
                .String("TABLE_CAT", "main")
                .String("TABLE_SCHEM", "public")
                .String("TABLE_NAME", "events")
                .String("COLUMN_NAME", "id")
                .Int("DATA_TYPE", -5) // java.sql.Types.BIGINT
                .String("TYPE_NAME", "BIGINT")
                .Int("COLUMN_SIZE", 19)
                .Tinyint("BUFFER_LENGTH", 8)
                .Int("DECIMAL_DIGITS", 0)
                .Int("NUM_PREC_RADIX", 10)
                .Int("NULLABLE", 1)
                .Build();

        /// <summary>
        /// Lets a test hold a stub RPC open on the server thread until released,
        /// and observe whether (and how often) the request arrived.
        /// </summary>
        private sealed class ServerGate : IDisposable
        {
            private readonly TaskCompletionSource<bool> _requestReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly ManualResetEventSlim _release = new(false);
            private int _requestCount;

            public bool Blocking { get; set; } = true;

            public int RequestCount => Volatile.Read(ref _requestCount);

            public MockResult Pass(Func<MockResult> result)
            {
                Interlocked.Increment(ref _requestCount);
                _requestReceived.TrySetResult(true);
                if (Blocking && !_release.Wait(TimeSpan.FromSeconds(15)))
                {
                    throw new TimeoutException("The test did not release the server response.");
                }
                return result();
            }

            public async Task<bool> WaitForRequestAsync() =>
                await Task.WhenAny(_requestReceived.Task, Task.Delay(s_waitLimit)) == _requestReceived.Task;

            public void Release() => _release.Set();

            public void Dispose() => _release.Dispose();
        }
    }
}
