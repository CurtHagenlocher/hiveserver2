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
using Apache.Hive.Service.Rpc.Thrift;
using Thrift.Protocol;
using Thrift.Protocol.Entities;
using Thrift.Transport;
using Xunit;

namespace AdbcDrivers.Tests.HiveServer2.Hive2.MockServer
{
    [Trait("Category", "MockServer")]
    public class MockServerCancellationTests
    {
        [Theory]
        [InlineData("ExecuteStatement", false)]
        [InlineData("ExecuteStatement", true)]
        [InlineData("GetOperationStatus", false)]
        [InlineData("GetOperationStatus", true)]
        public async Task CancelDuringSerialization_PreservesCleanupAndNextQuery(string method, bool afterField)
        {
            var statements = new List<string>();
            var stub = new HiveServer2StubHandler
            {
                OnExecuteStatement = request =>
                {
                    statements.Add(request.Statement);
                    return MockResult.SingleBigint(42);
                },
            };
            using var server = new HiveServer2TestServer(stub);
            var parameters = MockServerScenario.CopyParameters(HiveMockServer.DefaultParameters);
            parameters[AdbcOptions.Uri] = server.Uri.AbsoluteUri;
            using var connection = new CancellationTestConnection(parameters);
            await connection.OpenAsync();
            using var statement = connection.CreateStatement();
            statement.SqlQuery = "SELECT cancelled";

            bool cancelled = false;
            connection.Protocol.OnWrite = (message, fieldWritten) =>
            {
                if (!cancelled && message == method && fieldWritten == afterField)
                {
                    cancelled = true;
                    statement.Cancel();
                }
            };

            await Assert.ThrowsAsync<TimeoutException>(() => statement.ExecuteQueryAsync().AsTask());
            Assert.True(cancelled);
            if (method == "GetOperationStatus")
            {
                // The driver's best-effort CancelOperation catches failures, so
                // require a real Thrift reply, not merely a cancellation exception.
                Assert.Contains("CancelOperation", connection.Protocol.Responses);
            }
            Assert.Equal(method == "ExecuteStatement" ? 0 : 1, statements.Count);

            statement.SqlQuery = "SELECT 42";
            QueryResult result = await statement.ExecuteQueryAsync();
            using var stream = result.Stream!;
            using var batch = await stream.ReadNextRecordBatchAsync();
            Assert.NotNull(batch);
            Assert.Equal(42L, Assert.IsType<Int64Array>(batch.Column(0)).GetValue(0));
            Assert.Equal("SELECT 42", statements[statements.Count - 1]);
        }

        [Fact]
        public async Task CancelDuringHttpRequest_DoesNotWaitForServerResponse()
        {
            var requestReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stub = new HiveServer2StubHandler
            {
                OnExecuteStatement = _ =>
                {
                    requestReceived.SetResult(true);
                    if (!releaseResponse.Task.Wait(TimeSpan.FromSeconds(15)))
                    {
                        throw new TimeoutException("The test did not release the server response.");
                    }
                    return MockResult.SingleBigint(42);
                },
            };
            using var scenario = HiveMockServer.Create(stub);
            using var statement = scenario.NewStatement();
            statement.SqlQuery = "SELECT 42";
            Task<QueryResult> execution = statement.ExecuteQueryAsync().AsTask();
            try
            {
                Assert.Same(requestReceived.Task,
                    await Task.WhenAny(requestReceived.Task, Task.Delay(TimeSpan.FromSeconds(10))));
                statement.Cancel();
                Assert.Same(execution, await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(10))));
                await Assert.ThrowsAsync<TimeoutException>(() => execution);
            }
            finally
            {
                releaseResponse.SetResult(true);
            }
        }

        [Theory]
        [InlineData("ExecuteStatement", false)]
        [InlineData("ExecuteStatement", true)]
        [InlineData("GetOperationStatus", false)]
        [InlineData("GetOperationStatus", true)]
        public async Task CancelDuringStandardSerialization_InvalidatesConnection(string method, bool afterField)
        {
            using var server = new HiveServer2StandardTestServer(new HiveServer2StubHandler());
            using var connection = new StandardCancellationTestConnection(StandardParameters(server));
            await connection.OpenAsync();
            using var statement = connection.CreateStatement();
            statement.SqlQuery = "SELECT 42";
            bool cancelled = false;
            connection.Protocol.OnWrite = (message, fieldWritten) =>
            {
                if (!cancelled && message == method && fieldWritten == afterField)
                {
                    cancelled = true;
                    statement.Cancel();
                }
            };

            await Assert.ThrowsAsync<TimeoutException>(() => statement.ExecuteQueryAsync().AsTask());
            Assert.True(cancelled);
            await AssertStandardConnectionInvalidated(connection);
        }

        [Theory]
        [InlineData("ExecuteStatement")]
        [InlineData("GetOperationStatus")]
        public async Task CancelDuringStandardResponse_InvalidatesConnection(string method)
        {
            using var server = new HiveServer2StandardTestServer(new HiveServer2StubHandler());
            using var connection = new StandardCancellationTestConnection(StandardParameters(server));
            await connection.OpenAsync();
            using var statement = connection.CreateStatement();
            statement.SqlQuery = "SELECT 42";
            bool cancelled = false;
            connection.Protocol.OnRead = message =>
            {
                if (!cancelled && message == method)
                {
                    cancelled = true;
                    statement.Cancel();
                }
            };

            await Assert.ThrowsAsync<TimeoutException>(() => statement.ExecuteQueryAsync().AsTask());
            Assert.True(cancelled);
            await AssertStandardConnectionInvalidated(connection);
        }

        [Fact]
        public async Task CancelWhileAwaitingStandardResponse_AbortsReadAndInvalidatesConnection()
        {
            var requestReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stub = new HiveServer2StubHandler
            {
                OnExecuteStatement = _ =>
                {
                    requestReceived.SetResult(true);
                    if (!releaseResponse.Task.Wait(TimeSpan.FromSeconds(15)))
                    {
                        throw new TimeoutException("The test did not release the server response.");
                    }
                    return MockResult.SingleBigint(42);
                },
            };
            using var server = new HiveServer2StandardTestServer(stub);
            using var connection = new StandardCancellationTestConnection(StandardParameters(server));
            await connection.OpenAsync();
            using var statement = connection.CreateStatement();
            statement.SqlQuery = "SELECT 42";
            Task<QueryResult> execution = statement.ExecuteQueryAsync().AsTask();
            try
            {
                Assert.Same(requestReceived.Task,
                    await Task.WhenAny(requestReceived.Task, Task.Delay(TimeSpan.FromSeconds(10))));
                statement.Cancel();
                Assert.Same(execution, await Task.WhenAny(execution, Task.Delay(TimeSpan.FromSeconds(10))));
                await Assert.ThrowsAsync<TimeoutException>(() => execution);
                await AssertStandardConnectionInvalidated(connection);
            }
            finally
            {
                releaseResponse.SetResult(true);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CancelBeforeStandardRpcStarts_PreservesConnection(bool cancelWhileQueued)
        {
            var requestReceived = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseResponse = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var stub = new HiveServer2StubHandler
            {
                OnExecuteStatement = _ =>
                {
                    requestReceived.SetResult(true);
                    if (!releaseResponse.Task.Wait(TimeSpan.FromSeconds(15)))
                    {
                        throw new TimeoutException("The test did not release the server response.");
                    }
                    return MockResult.SingleBigint(42);
                },
            };
            using var server = new HiveServer2StandardTestServer(stub);
            using var connection = new StandardCancellationTestConnection(StandardParameters(server));
            await connection.OpenAsync();
            using var statement = connection.CreateStatement();
            statement.SqlQuery = "SELECT 42";
            Task<QueryResult> execution = statement.ExecuteQueryAsync().AsTask();
            try
            {
                Assert.Same(requestReceived.Task,
                    await Task.WhenAny(requestReceived.Task, Task.Delay(TimeSpan.FromSeconds(10))));
                using var cancellation = new CancellationTokenSource();
                if (!cancelWhileQueued) cancellation.Cancel();
                Task<TGetInfoResp> queued = connection.Client.GetInfo(
                    new TGetInfoReq(connection.SessionHandle!, TGetInfoType.CLI_DBMS_NAME), cancellation.Token);
                if (cancelWhileQueued)
                {
                    Assert.False(queued.IsCompleted);
                    cancellation.Cancel();
                }
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
                Assert.True(connection.Protocol.Transport.IsOpen);
            }
            finally
            {
                releaseResponse.SetResult(true);
            }

            QueryResult result = await execution;
            using var stream = result.Stream!;
            using var batch = await stream.ReadNextRecordBatchAsync();
            Assert.NotNull(batch);
            Assert.Equal(42L, Assert.IsType<Int64Array>(batch.Column(0)).GetValue(0));
            TGetInfoResp info = await connection.Client.GetInfo(
                new TGetInfoReq(connection.SessionHandle!, TGetInfoType.CLI_DBMS_NAME));
            Assert.Equal("Test Hive", info.InfoValue.StringValue);
        }

        private static Dictionary<string, string> StandardParameters(HiveServer2StandardTestServer server)
        {
            var parameters = MockServerScenario.CopyParameters(HiveMockServer.DefaultStandardParameters);
            parameters[HiveServer2Parameters.HostName] = server.HostName;
            parameters[HiveServer2Parameters.Port] = server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            parameters[ApacheParameters.QueryTimeoutSeconds] = "5";
            return parameters;
        }

        private static async Task AssertStandardConnectionInvalidated(StandardCancellationTestConnection connection)
        {
            Assert.False(connection.Protocol.Transport.IsOpen);
            Assert.DoesNotContain("CancelOperation", connection.Protocol.Requests);
            int requests = connection.Protocol.Requests.Count;
            using var nextStatement = connection.CreateStatement();
            nextStatement.SqlQuery = "SELECT 99";
            HiveServer2Exception error = await Assert.ThrowsAsync<HiveServer2Exception>(
                () => nextStatement.ExecuteQueryAsync().AsTask());
            Assert.Equal(AdbcStatusCode.InvalidState, error.Status);
            Assert.Contains("new connection", error.Message);
            Assert.Equal(requests, connection.Protocol.Requests.Count);
        }

        private sealed class StandardCancellationTestConnection : HiveServer2StandardConnection
        {
            public StandardCancellationTestConnection(IReadOnlyDictionary<string, string> properties) : base(properties)
            {
            }

            public CancellationTestProtocol Protocol { get; private set; } = null!;

            protected override async Task<TProtocol> CreateProtocolAsync(
                TTransport transport, CancellationToken cancellationToken = default)
            {
                Protocol = new CancellationTestProtocol(await base.CreateProtocolAsync(transport, cancellationToken));
                return Protocol;
            }
        }

        private sealed class CancellationTestConnection : HiveServer2HttpConnection
        {
            public CancellationTestConnection(IReadOnlyDictionary<string, string> properties) : base(properties)
            {
            }

            public CancellationTestProtocol Protocol { get; private set; } = null!;

            protected override async Task<TProtocol> CreateProtocolAsync(
                TTransport transport, CancellationToken cancellationToken = default)
            {
                Protocol = new CancellationTestProtocol(await base.CreateProtocolAsync(transport, cancellationToken));
                return Protocol;
            }
        }

        private sealed class CancellationTestProtocol : TProtocolDecorator
        {
            private string? _message;

            public CancellationTestProtocol(TProtocol protocol) : base(protocol)
            {
            }

            public Action<string?, bool>? OnWrite { get; set; }
            public Action<string>? OnRead { get; set; }
            public List<string> Requests { get; } = new();
            public List<string> Responses { get; } = new();

            public override async Task WriteMessageBeginAsync(TMessage message, CancellationToken cancellationToken)
            {
                await base.WriteMessageBeginAsync(message, cancellationToken);
                _message = message.Name;
                Requests.Add(message.Name);
                // Cancel only after real bytes have reached the transport's buffer.
                OnWrite?.Invoke(_message, false);
            }

            public override async Task WriteFieldBeginAsync(TField field, CancellationToken cancellationToken)
            {
                await base.WriteFieldBeginAsync(field, cancellationToken);
                OnWrite?.Invoke(_message, true);
            }

            public override async ValueTask<TMessage> ReadMessageBeginAsync(CancellationToken cancellationToken)
            {
                TMessage message = await base.ReadMessageBeginAsync(cancellationToken);
                Responses.Add(message.Name);
                OnRead?.Invoke(message.Name);
                return message;
            }
        }
    }
}
