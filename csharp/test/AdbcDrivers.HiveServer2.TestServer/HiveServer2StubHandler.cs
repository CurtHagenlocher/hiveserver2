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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Apache.Hive.Service.Rpc.Thrift.Reference;

namespace AdbcDrivers.HiveServer2.TestServer
{
    /// <summary>
    /// Configurable Hive-flavored implementation of
    /// <see cref="TCLIService.IAsync"/>. Every result-producing RPC is
    /// dispatched through a per-RPC callback that returns a
    /// <see cref="MockResult"/>; tests override callbacks (or set the
    /// property setters) to drive specific behaviors. Operation handles
    /// returned by ExecuteStatement / Get* are tracked internally so the
    /// subsequent GetResultSetMetadata / GetOperationStatus / FetchResults /
    /// CloseOperation calls return self-consistent data.
    /// </summary>
    public sealed class HiveServer2StubHandler : TCLIService.IAsync
    {
        // Session-level knobs
        public string DbmsName { get; set; } = "Test Hive";
        public string DbmsVersion { get; set; } = "0.0.0-test";

        /// <summary>
        /// Status returned from <c>OpenSession</c>. Defaults to SUCCESS;
        /// set to an ERROR status to exercise the driver's failed-open path.
        /// </summary>
        public TStatus OpenSessionStatus { get; set; } = new(TStatusCode.SUCCESS_STATUS);

        /// <summary>
        /// Optional hook invoked on the server thread when <c>OpenSession</c> arrives,
        /// before the response is built. Lets a test observe or delay session opening.
        /// </summary>
        public Action<TOpenSessionReq>? OnOpenSession { get; set; }

        /// <summary>Optional hook invoked when <c>CloseSession</c> arrives.</summary>
        public Action<TCloseSessionReq>? OnCloseSession { get; set; }

        // Per-RPC result generators. Defaults give the same behavior as the
        // original stub: ExecuteStatement returns a one-row BIGINT, every
        // metadata RPC returns an empty result with the schema the driver
        // expects.

        public Func<TExecuteStatementReq, MockResult> OnExecuteStatement { get; set; }
            = _ => MockResult.SingleBigint(42L);

        public Func<TGetCatalogsReq, MockResult> OnGetCatalogs { get; set; }
            = _ => MockResult.Empty(MockSchema.GetCatalogsSchema);

        public Func<TGetSchemasReq, MockResult> OnGetSchemas { get; set; }
            = _ => MockResult.Empty(MockSchema.GetSchemasSchema);

        public Func<TGetTablesReq, MockResult> OnGetTables { get; set; }
            = _ => MockResult.Empty(MockSchema.GetTablesSchema);

        public Func<TGetTableTypesReq, MockResult> OnGetTableTypes { get; set; }
            = _ => MockResult.Empty(MockSchema.GetTableTypesSchema);

        public Func<TGetColumnsReq, MockResult> OnGetColumns { get; set; }
            = _ => MockResult.Empty(MockSchema.GetColumnsSchema);

        public Func<TGetFunctionsReq, MockResult> OnGetFunctions { get; set; }
            = _ => MockResult.Empty(MockSchema.GetFunctionsSchema);

        public Func<TGetPrimaryKeysReq, MockResult> OnGetPrimaryKeys { get; set; }
            = _ => MockResult.Empty(MockSchema.GetPrimaryKeysSchema);

        public Func<TGetCrossReferenceReq, MockResult> OnGetCrossReference { get; set; }
            = _ => MockResult.Empty(MockSchema.GetCrossReferenceSchema);

        // Tracked operations
        private readonly ConcurrentDictionary<Guid, OperationEntry> _operations = new();

        public Task<TOpenSessionResp> OpenSession(TOpenSessionReq req, CancellationToken cancellationToken = default)
        {
            OnOpenSession?.Invoke(req);
            var resp = new TOpenSessionResp(OpenSessionStatus, req.Client_protocol);
            if (OpenSessionStatus.StatusCode == TStatusCode.SUCCESS_STATUS)
            {
                resp.SessionHandle = new TSessionHandle(NewHandleId());
            }
            return Task.FromResult(resp);
        }

        public Task<TCloseSessionResp> CloseSession(TCloseSessionReq req, CancellationToken cancellationToken = default)
        {
            OnCloseSession?.Invoke(req);
            return Task.FromResult(new TCloseSessionResp(Ok()));
        }

        public Task<TGetInfoResp> GetInfo(TGetInfoReq req, CancellationToken cancellationToken = default)
        {
            var value = new TGetInfoValue();
            switch (req.InfoType)
            {
                case TGetInfoType.CLI_DBMS_NAME:
                    value.StringValue = DbmsName;
                    break;
                case TGetInfoType.CLI_DBMS_VER:
                    value.StringValue = DbmsVersion;
                    break;
                default:
                    value.StringValue = string.Empty;
                    break;
            }
            return Task.FromResult(new TGetInfoResp(Ok(), value));
        }

        public Task<TExecuteStatementResp> ExecuteStatement(TExecuteStatementReq req, CancellationToken cancellationToken = default)
        {
            var handle = RegisterOperation(TOperationType.EXECUTE_STATEMENT, OnExecuteStatement(req));
            return Task.FromResult(new TExecuteStatementResp(Ok()) { OperationHandle = handle });
        }

        public Task<TGetCatalogsResp> GetCatalogs(TGetCatalogsReq req, CancellationToken cancellationToken = default)
        {
            var handle = RegisterOperation(TOperationType.GET_CATALOGS, OnGetCatalogs(req));
            return Task.FromResult(new TGetCatalogsResp(Ok()) { OperationHandle = handle });
        }

        public Task<TGetSchemasResp> GetSchemas(TGetSchemasReq req, CancellationToken cancellationToken = default)
        {
            var handle = RegisterOperation(TOperationType.GET_SCHEMAS, OnGetSchemas(req));
            return Task.FromResult(new TGetSchemasResp(Ok()) { OperationHandle = handle });
        }

        public Task<TGetTablesResp> GetTables(TGetTablesReq req, CancellationToken cancellationToken = default)
        {
            var handle = RegisterOperation(TOperationType.GET_TABLES, OnGetTables(req));
            return Task.FromResult(new TGetTablesResp(Ok()) { OperationHandle = handle });
        }

        public Task<TGetTableTypesResp> GetTableTypes(TGetTableTypesReq req, CancellationToken cancellationToken = default)
        {
            var handle = RegisterOperation(TOperationType.GET_TABLE_TYPES, OnGetTableTypes(req));
            return Task.FromResult(new TGetTableTypesResp(Ok()) { OperationHandle = handle });
        }

        public Task<TGetColumnsResp> GetColumns(TGetColumnsReq req, CancellationToken cancellationToken = default)
        {
            var handle = RegisterOperation(TOperationType.GET_COLUMNS, OnGetColumns(req));
            return Task.FromResult(new TGetColumnsResp(Ok()) { OperationHandle = handle });
        }

        public Task<TGetFunctionsResp> GetFunctions(TGetFunctionsReq req, CancellationToken cancellationToken = default)
        {
            var handle = RegisterOperation(TOperationType.GET_FUNCTIONS, OnGetFunctions(req));
            return Task.FromResult(new TGetFunctionsResp(Ok()) { OperationHandle = handle });
        }

        public Task<TGetPrimaryKeysResp> GetPrimaryKeys(TGetPrimaryKeysReq req, CancellationToken cancellationToken = default)
        {
            // TOperationType has no specific entry for primary-keys/cross-reference;
            // UNKNOWN is what upstream servers typically report.
            var handle = RegisterOperation(TOperationType.UNKNOWN, OnGetPrimaryKeys(req));
            return Task.FromResult(new TGetPrimaryKeysResp(Ok()) { OperationHandle = handle });
        }

        public Task<TGetCrossReferenceResp> GetCrossReference(TGetCrossReferenceReq req, CancellationToken cancellationToken = default)
        {
            var handle = RegisterOperation(TOperationType.UNKNOWN, OnGetCrossReference(req));
            return Task.FromResult(new TGetCrossReferenceResp(Ok()) { OperationHandle = handle });
        }

        public Task<TGetOperationStatusResp> GetOperationStatus(TGetOperationStatusReq req, CancellationToken cancellationToken = default)
        {
            var op = LookupOperation(req.OperationHandle);
            var resultStatus = op?.Result.OperationStatus ?? Ok();
            bool isError = resultStatus.StatusCode != TStatusCode.SUCCESS_STATUS
                && resultStatus.StatusCode != TStatusCode.SUCCESS_WITH_INFO_STATUS;
            var resp = new TGetOperationStatusResp(Ok())
            {
                OperationState = isError ? TOperationState.ERROR_STATE : TOperationState.FINISHED_STATE,
                HasResultSet = HasResultSet(op?.Result),
            };
            if (isError)
            {
#pragma warning disable CS0618 // ErrorMessage/SqlState/ErrorCode are marked obsolete in newer protocol revs
                resp.ErrorMessage = resultStatus.ErrorMessage ?? "operation error";
                if (resultStatus.SqlState != null) resp.SqlState = resultStatus.SqlState;
                resp.ErrorCode = resultStatus.ErrorCode;
#pragma warning restore CS0618
            }
            return Task.FromResult(resp);
        }

        public Task<TGetResultSetMetadataResp> GetResultSetMetadata(TGetResultSetMetadataReq req, CancellationToken cancellationToken = default)
        {
            var op = LookupOperation(req.OperationHandle);
            var resp = new TGetResultSetMetadataResp(Ok());
            if (op != null) resp.Schema = op.Result.Schema;
            return Task.FromResult(resp);
        }

        public Task<TFetchResultsResp> FetchResults(TFetchResultsReq req, CancellationToken cancellationToken = default)
        {
            var op = LookupOperation(req.OperationHandle);
            TRowSet rowSet;
            bool hasMore;
            if (op == null)
            {
                rowSet = new TRowSet(0L, new List<TRow>()) { Columns = new List<TColumn>() };
                hasMore = false;
            }
            else if (op.NextBatchIndex < op.Result.Batches.Count)
            {
                rowSet = op.Result.Batches[op.NextBatchIndex];
                Interlocked.Increment(ref op.NextBatchIndex);
                hasMore = op.NextBatchIndex < op.Result.Batches.Count;
            }
            else
            {
                rowSet = MockRowSet.EmptyFor(op.Result.Schema);
                hasMore = false;
            }
            return Task.FromResult(new TFetchResultsResp(Ok()) { HasMoreRows = hasMore, Results = rowSet });
        }

        public Task<TCloseOperationResp> CloseOperation(TCloseOperationReq req, CancellationToken cancellationToken = default)
        {
            _operations.TryRemove(GuidOf(req.OperationHandle), out _);
            return Task.FromResult(new TCloseOperationResp(Ok()));
        }

        public Task<TCancelOperationResp> CancelOperation(TCancelOperationReq req, CancellationToken cancellationToken = default) =>
            Task.FromResult(new TCancelOperationResp(Ok()));

        // Methods left unimplemented surface as TApplicationException on the wire
        // and are explicitly opted into by tests that need them.

        public Task<TGetTypeInfoResp> GetTypeInfo(TGetTypeInfoReq req, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TGetDelegationTokenResp> GetDelegationToken(TGetDelegationTokenReq req, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TCancelDelegationTokenResp> CancelDelegationToken(TCancelDelegationTokenReq req, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TRenewDelegationTokenResp> RenewDelegationToken(TRenewDelegationTokenReq req, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TGetQueryIdResp> GetQueryId(TGetQueryIdReq req, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TSetClientInfoResp> SetClientInfo(TSetClientInfoReq req, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TUploadDataResp> UploadData(TUploadDataReq req, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TDownloadDataResp> DownloadData(TDownloadDataReq req, CancellationToken cancellationToken = default) => throw new NotImplementedException();

        private TOperationHandle RegisterOperation(TOperationType type, MockResult result)
        {
            var handle = new TOperationHandle(NewHandleId(), type, HasResultSet(result));
            _operations[GuidOf(handle)] = new OperationEntry(result);
            return handle;
        }

        private OperationEntry? LookupOperation(TOperationHandle handle) =>
            _operations.TryGetValue(GuidOf(handle), out var entry) ? entry : null;

        /// <summary>
        /// A registered result has a result set whenever it carries a schema —
        /// the row count is irrelevant. This matches HiveServer2 semantics
        /// (an empty SELECT still has a result set) and keeps the value
        /// returned from <c>RegisterOperation</c> consistent with what
        /// <c>GetOperationStatus</c> reports later.
        /// </summary>
        private static bool HasResultSet(MockResult? result) =>
            result?.Schema.Columns.Count > 0;

        private static TStatus Ok() => new(TStatusCode.SUCCESS_STATUS);

        private static THandleIdentifier NewHandleId() =>
            new(Guid.NewGuid().ToByteArray(), Guid.NewGuid().ToByteArray());

        private static Guid GuidOf(TOperationHandle handle) => new(handle.OperationId.Guid);

        private sealed class OperationEntry
        {
            public OperationEntry(MockResult result) { Result = result; }
            public MockResult Result { get; }
            public int NextBatchIndex;
        }
    }
}
