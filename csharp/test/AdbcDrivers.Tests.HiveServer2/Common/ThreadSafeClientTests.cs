/*
 * Copyright (c) 2025 ADBC Drivers Contributors
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
using System.Threading.Tasks;
using AdbcDrivers.HiveServer2.Hive2;
using Apache.Hive.Service.Rpc.Thrift;
using Xunit;

namespace AdbcDrivers.Tests.HiveServer2.Common
{
    /// <summary>
    /// Direct unit tests for the synchronization and forwarding behavior of
    /// <see cref="ThreadSafeClient"/>. Standard-transport cancellation and
    /// invalidation are exercised against the mock server.
    /// </summary>
    public class ThreadSafeClientTests
    {
        private static ThreadSafeClient NewClient(TCLIService.IAsync? inner = null) =>
            new ThreadSafeClient(inner ?? new StubClient());

        [Fact]
        public void Constructor_NullClient_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new ThreadSafeClient(null!));

        [Fact]
        public void Constructor_ValidClient_Succeeds()
        {
            using ThreadSafeClient c = NewClient();
            Assert.NotNull(c);
        }

        [Fact]
        public void Dispose_WithoutTransport_DisposesWrappedClientOnce()
        {
            var inner = new StubClient();
            ThreadSafeClient c = NewClient(inner);
            c.Dispose();
            c.Dispose();
            Assert.Equal(1, inner.DisposeCalls);
        }

        [Fact]
        public async Task AnyMethod_AfterDispose_ThrowsObjectDisposed()
        {
            ThreadSafeClient c = NewClient();
            c.Dispose();
            await Assert.ThrowsAsync<ObjectDisposedException>(
                () => c.OpenSession(new TOpenSessionReq()));
        }

        [Fact]
        public async Task OpenSession_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.OpenSession(new TOpenSessionReq())); Assert.Equal(1, s.OpenSessionCalls); }

        [Fact]
        public async Task CloseSession_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.CloseSession(new TCloseSessionReq())); Assert.Equal(1, s.CloseSessionCalls); }

        [Fact]
        public async Task GetInfo_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetInfo(new TGetInfoReq())); Assert.Equal(1, s.GetInfoCalls); }

        [Fact]
        public async Task ExecuteStatement_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.ExecuteStatement(new TExecuteStatementReq())); Assert.Equal(1, s.ExecuteStatementCalls); }

        [Fact]
        public async Task GetTypeInfo_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetTypeInfo(new TGetTypeInfoReq())); Assert.Equal(1, s.GetTypeInfoCalls); }

        [Fact]
        public async Task GetCatalogs_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetCatalogs(new TGetCatalogsReq())); Assert.Equal(1, s.GetCatalogsCalls); }

        [Fact]
        public async Task GetSchemas_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetSchemas(new TGetSchemasReq())); Assert.Equal(1, s.GetSchemasCalls); }

        [Fact]
        public async Task GetTables_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetTables(new TGetTablesReq())); Assert.Equal(1, s.GetTablesCalls); }

        [Fact]
        public async Task GetTableTypes_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetTableTypes(new TGetTableTypesReq())); Assert.Equal(1, s.GetTableTypesCalls); }

        [Fact]
        public async Task GetColumns_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetColumns(new TGetColumnsReq())); Assert.Equal(1, s.GetColumnsCalls); }

        [Fact]
        public async Task GetFunctions_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetFunctions(new TGetFunctionsReq())); Assert.Equal(1, s.GetFunctionsCalls); }

        [Fact]
        public async Task GetPrimaryKeys_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetPrimaryKeys(new TGetPrimaryKeysReq())); Assert.Equal(1, s.GetPrimaryKeysCalls); }

        [Fact]
        public async Task GetCrossReference_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetCrossReference(new TGetCrossReferenceReq())); Assert.Equal(1, s.GetCrossReferenceCalls); }

        [Fact]
        public async Task GetOperationStatus_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetOperationStatus(new TGetOperationStatusReq())); Assert.Equal(1, s.GetOperationStatusCalls); }

        [Fact]
        public async Task CancelOperation_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.CancelOperation(new TCancelOperationReq())); Assert.Equal(1, s.CancelOperationCalls); }

        [Fact]
        public async Task CloseOperation_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.CloseOperation(new TCloseOperationReq())); Assert.Equal(1, s.CloseOperationCalls); }

        [Fact]
        public async Task GetResultSetMetadata_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetResultSetMetadata(new TGetResultSetMetadataReq())); Assert.Equal(1, s.GetResultSetMetadataCalls); }

        [Fact]
        public async Task FetchResults_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.FetchResults(new TFetchResultsReq())); Assert.Equal(1, s.FetchResultsCalls); }

        [Fact]
        public async Task GetDelegationToken_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetDelegationToken(new TGetDelegationTokenReq())); Assert.Equal(1, s.GetDelegationTokenCalls); }

        [Fact]
        public async Task CancelDelegationToken_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.CancelDelegationToken(new TCancelDelegationTokenReq())); Assert.Equal(1, s.CancelDelegationTokenCalls); }

        [Fact]
        public async Task RenewDelegationToken_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.RenewDelegationToken(new TRenewDelegationTokenReq())); Assert.Equal(1, s.RenewDelegationTokenCalls); }

        [Fact]
        public async Task GetQueryId_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.GetQueryId(new TGetQueryIdReq())); Assert.Equal(1, s.GetQueryIdCalls); }

        [Fact]
        public async Task SetClientInfo_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.SetClientInfo(new TSetClientInfoReq())); Assert.Equal(1, s.SetClientInfoCalls); }

        [Fact]
        public async Task UploadData_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.UploadData(new TUploadDataReq())); Assert.Equal(1, s.UploadDataCalls); }

        [Fact]
        public async Task DownloadData_PassesThrough() { var s = new StubClient(); using var c = NewClient(s); Assert.NotNull(await c.DownloadData(new TDownloadDataReq())); Assert.Equal(1, s.DownloadDataCalls); }

        [Fact]
        public async Task ConcurrentCalls_AreSerialized()
        {
            // Track max-observed concurrency inside the inner client. If
            // ThreadSafeClient's semaphore is doing its job we should never
            // see more than one in-flight call; if the semaphore were removed
            // the small delay below would let several execute simultaneously
            // and MaxConcurrent would climb above 1.
            var s = new ConcurrencyProbeClient(delayPerCall: TimeSpan.FromMilliseconds(5));
            using ThreadSafeClient c = NewClient(s);
            var tasks = new Task[10];
            for (int i = 0; i < tasks.Length; i++)
            {
                tasks[i] = c.ExecuteStatement(new TExecuteStatementReq { Statement = $"SELECT {i}" });
            }
            await Task.WhenAll(tasks);
            Assert.Equal(10, s.TotalCalls);
            Assert.Equal(1, s.MaxConcurrent);
        }

        [Fact]
        public async Task CancelledToken_Propagates()
        {
            using ThreadSafeClient c = NewClient();
            var cts = new CancellationTokenSource();
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => c.OpenSession(new TOpenSessionReq(), cts.Token));
        }

        /// <summary>
        /// Minimal in-memory implementation of the driver-side TCLIService.IAsync
        /// that just counts invocations and returns a fresh response object —
        /// enough to exercise every pass-through path in ThreadSafeClient.
        /// </summary>
        private sealed class StubClient : TCLIService.IAsync, IDisposable
        {
            public int DisposeCalls;
            public int OpenSessionCalls;
            public int CloseSessionCalls;
            public int GetInfoCalls;
            public int ExecuteStatementCalls;
            public int GetTypeInfoCalls;
            public int GetCatalogsCalls;
            public int GetSchemasCalls;
            public int GetTablesCalls;
            public int GetTableTypesCalls;
            public int GetColumnsCalls;
            public int GetFunctionsCalls;
            public int GetPrimaryKeysCalls;
            public int GetCrossReferenceCalls;
            public int GetOperationStatusCalls;
            public int CancelOperationCalls;
            public int CloseOperationCalls;
            public int GetResultSetMetadataCalls;
            public int FetchResultsCalls;
            public int GetDelegationTokenCalls;
            public int CancelDelegationTokenCalls;
            public int RenewDelegationTokenCalls;
            public int GetQueryIdCalls;
            public int SetClientInfoCalls;
            public int UploadDataCalls;
            public int DownloadDataCalls;

            public void Dispose() => Interlocked.Increment(ref DisposeCalls);

            public Task<TOpenSessionResp> OpenSession(TOpenSessionReq req, CancellationToken ct = default) { Interlocked.Increment(ref OpenSessionCalls); return Task.FromResult(new TOpenSessionResp()); }
            public Task<TCloseSessionResp> CloseSession(TCloseSessionReq req, CancellationToken ct = default) { Interlocked.Increment(ref CloseSessionCalls); return Task.FromResult(new TCloseSessionResp()); }
            public Task<TGetInfoResp> GetInfo(TGetInfoReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetInfoCalls); return Task.FromResult(new TGetInfoResp()); }
            public Task<TExecuteStatementResp> ExecuteStatement(TExecuteStatementReq req, CancellationToken ct = default) { Interlocked.Increment(ref ExecuteStatementCalls); return Task.FromResult(new TExecuteStatementResp()); }
            public Task<TGetTypeInfoResp> GetTypeInfo(TGetTypeInfoReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetTypeInfoCalls); return Task.FromResult(new TGetTypeInfoResp()); }
            public Task<TGetCatalogsResp> GetCatalogs(TGetCatalogsReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetCatalogsCalls); return Task.FromResult(new TGetCatalogsResp()); }
            public Task<TGetSchemasResp> GetSchemas(TGetSchemasReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetSchemasCalls); return Task.FromResult(new TGetSchemasResp()); }
            public Task<TGetTablesResp> GetTables(TGetTablesReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetTablesCalls); return Task.FromResult(new TGetTablesResp()); }
            public Task<TGetTableTypesResp> GetTableTypes(TGetTableTypesReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetTableTypesCalls); return Task.FromResult(new TGetTableTypesResp()); }
            public Task<TGetColumnsResp> GetColumns(TGetColumnsReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetColumnsCalls); return Task.FromResult(new TGetColumnsResp()); }
            public Task<TGetFunctionsResp> GetFunctions(TGetFunctionsReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetFunctionsCalls); return Task.FromResult(new TGetFunctionsResp()); }
            public Task<TGetPrimaryKeysResp> GetPrimaryKeys(TGetPrimaryKeysReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetPrimaryKeysCalls); return Task.FromResult(new TGetPrimaryKeysResp()); }
            public Task<TGetCrossReferenceResp> GetCrossReference(TGetCrossReferenceReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetCrossReferenceCalls); return Task.FromResult(new TGetCrossReferenceResp()); }
            public Task<TGetOperationStatusResp> GetOperationStatus(TGetOperationStatusReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetOperationStatusCalls); return Task.FromResult(new TGetOperationStatusResp()); }
            public Task<TCancelOperationResp> CancelOperation(TCancelOperationReq req, CancellationToken ct = default) { Interlocked.Increment(ref CancelOperationCalls); return Task.FromResult(new TCancelOperationResp()); }
            public Task<TCloseOperationResp> CloseOperation(TCloseOperationReq req, CancellationToken ct = default) { Interlocked.Increment(ref CloseOperationCalls); return Task.FromResult(new TCloseOperationResp()); }
            public Task<TGetResultSetMetadataResp> GetResultSetMetadata(TGetResultSetMetadataReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetResultSetMetadataCalls); return Task.FromResult(new TGetResultSetMetadataResp()); }
            public Task<TFetchResultsResp> FetchResults(TFetchResultsReq req, CancellationToken ct = default) { Interlocked.Increment(ref FetchResultsCalls); return Task.FromResult(new TFetchResultsResp()); }
            public Task<TGetDelegationTokenResp> GetDelegationToken(TGetDelegationTokenReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetDelegationTokenCalls); return Task.FromResult(new TGetDelegationTokenResp()); }
            public Task<TCancelDelegationTokenResp> CancelDelegationToken(TCancelDelegationTokenReq req, CancellationToken ct = default) { Interlocked.Increment(ref CancelDelegationTokenCalls); return Task.FromResult(new TCancelDelegationTokenResp()); }
            public Task<TRenewDelegationTokenResp> RenewDelegationToken(TRenewDelegationTokenReq req, CancellationToken ct = default) { Interlocked.Increment(ref RenewDelegationTokenCalls); return Task.FromResult(new TRenewDelegationTokenResp()); }
            public Task<TGetQueryIdResp> GetQueryId(TGetQueryIdReq req, CancellationToken ct = default) { Interlocked.Increment(ref GetQueryIdCalls); return Task.FromResult(new TGetQueryIdResp()); }
            public Task<TSetClientInfoResp> SetClientInfo(TSetClientInfoReq req, CancellationToken ct = default) { Interlocked.Increment(ref SetClientInfoCalls); return Task.FromResult(new TSetClientInfoResp()); }
            public Task<TUploadDataResp> UploadData(TUploadDataReq req, CancellationToken ct = default) { Interlocked.Increment(ref UploadDataCalls); return Task.FromResult(new TUploadDataResp()); }
            public Task<TDownloadDataResp> DownloadData(TDownloadDataReq req, CancellationToken ct = default) { Interlocked.Increment(ref DownloadDataCalls); return Task.FromResult(new TDownloadDataResp()); }
        }

        /// <summary>
        /// TCLIService.IAsync stub that records concurrent in-flight calls.
        /// Only ExecuteStatement is instrumented (the only method the
        /// concurrency test drives); the rest are unused. ExecuteStatement
        /// awaits a small delay while holding the in-flight slot so callers
        /// that bypass mutual exclusion will overlap and bump MaxConcurrent.
        /// </summary>
        private sealed class ConcurrencyProbeClient : TCLIService.IAsync
        {
            private readonly TimeSpan _delay;
            private int _inFlight;
            public int TotalCalls;
            public int MaxConcurrent;

            public ConcurrencyProbeClient(TimeSpan delayPerCall) => _delay = delayPerCall;

            public async Task<TExecuteStatementResp> ExecuteStatement(TExecuteStatementReq req, CancellationToken ct = default)
            {
                int now = Interlocked.Increment(ref _inFlight);
                // Climb MaxConcurrent if this call is the highest observed.
                int observed;
                do { observed = MaxConcurrent; } while (now > observed && Interlocked.CompareExchange(ref MaxConcurrent, now, observed) != observed);
                try
                {
                    await Task.Delay(_delay, ct).ConfigureAwait(false);
                    Interlocked.Increment(ref TotalCalls);
                    return new TExecuteStatementResp();
                }
                finally
                {
                    Interlocked.Decrement(ref _inFlight);
                }
            }

            // Unused — required by the interface but never called in this test.
            public Task<TOpenSessionResp> OpenSession(TOpenSessionReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TCloseSessionResp> CloseSession(TCloseSessionReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetInfoResp> GetInfo(TGetInfoReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetTypeInfoResp> GetTypeInfo(TGetTypeInfoReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetCatalogsResp> GetCatalogs(TGetCatalogsReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetSchemasResp> GetSchemas(TGetSchemasReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetTablesResp> GetTables(TGetTablesReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetTableTypesResp> GetTableTypes(TGetTableTypesReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetColumnsResp> GetColumns(TGetColumnsReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetFunctionsResp> GetFunctions(TGetFunctionsReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetPrimaryKeysResp> GetPrimaryKeys(TGetPrimaryKeysReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetCrossReferenceResp> GetCrossReference(TGetCrossReferenceReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetOperationStatusResp> GetOperationStatus(TGetOperationStatusReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TCancelOperationResp> CancelOperation(TCancelOperationReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TCloseOperationResp> CloseOperation(TCloseOperationReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetResultSetMetadataResp> GetResultSetMetadata(TGetResultSetMetadataReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TFetchResultsResp> FetchResults(TFetchResultsReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetDelegationTokenResp> GetDelegationToken(TGetDelegationTokenReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TCancelDelegationTokenResp> CancelDelegationToken(TCancelDelegationTokenReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TRenewDelegationTokenResp> RenewDelegationToken(TRenewDelegationTokenReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TGetQueryIdResp> GetQueryId(TGetQueryIdReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TSetClientInfoResp> SetClientInfo(TSetClientInfoReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TUploadDataResp> UploadData(TUploadDataReq req, CancellationToken ct = default) => throw new NotSupportedException();
            public Task<TDownloadDataResp> DownloadData(TDownloadDataReq req, CancellationToken ct = default) => throw new NotSupportedException();
        }
    }
}
