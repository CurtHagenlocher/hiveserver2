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
using System.Data.SqlTypes;
using System.Threading.Tasks;
using AdbcDrivers.HiveServer2.TestServer;
using Apache.Arrow;
using Apache.Arrow.Adbc;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using Xunit;

namespace AdbcDrivers.Tests.HiveServer2.Hive2.MockServer
{
    /// <summary>
    /// Per-primitive-type round trips: configure the mock to return a known
    /// column shape, execute through the real driver, and verify the Arrow
    /// array type and contents (including null handling via the wire-format
    /// nulls bitmap). Covers <c>HiveServer2Reader.GetArray</c> and the
    /// string→date/decimal/timestamp + double→float conversion paths.
    /// </summary>
    [Trait("Category", "MockServer")]
    public class MockServerDataTypeTests
    {
        [Fact]
        public Task BooleanColumn_ReadsAsBooleanArray() =>
            RunAsync(MockResult.Builder().Bool("flag", true, false, null, true).Build(),
                (BooleanArray c) =>
                {
                    Assert.Equal(4, c.Length);
                    Assert.True(c.GetValue(0));
                    Assert.False(c.GetValue(1));
                    Assert.Null(c.GetValue(2));
                    Assert.True(c.GetValue(3));
                });

        [Fact]
        public Task BooleanColumn_LargeBatch_ExercisesVectorPackPath()
        {
            // 50 rows pushes past the 32-byte SIMD block so the vectorized
            // boolean packing path and its scalar tail are both exercised
            // end-to-end, with interleaved nulls.
            var expected = new bool?[50];
            for (int i = 0; i < expected.Length; i++)
            {
                expected[i] = (i % 7 == 0) ? (bool?)null : (i % 3 == 0);
            }

            return RunAsync(MockResult.Builder().Bool("v", expected).Build(),
                (BooleanArray c) =>
                {
                    Assert.Equal(expected.Length, c.Length);
                    for (int i = 0; i < expected.Length; i++)
                    {
                        Assert.Equal(expected[i], c.GetValue(i));
                    }
                });
        }

        [Fact]
        public Task TinyintColumn_ReadsAsInt8Array() =>
            RunAsync(MockResult.Builder().Tinyint("v", 1, -1, null, sbyte.MaxValue).Build(),
                (Int8Array c) =>
                {
                    Assert.Equal((sbyte)1, c.GetValue(0));
                    Assert.Equal((sbyte)-1, c.GetValue(1));
                    Assert.Null(c.GetValue(2));
                    Assert.Equal(sbyte.MaxValue, c.GetValue(3));
                });

        [Fact]
        public Task SmallintColumn_ReadsAsInt16Array() =>
            RunAsync(MockResult.Builder().Smallint("v", 1, null, short.MinValue).Build(),
                (Int16Array c) =>
                {
                    Assert.Equal((short)1, c.GetValue(0));
                    Assert.Null(c.GetValue(1));
                    Assert.Equal(short.MinValue, c.GetValue(2));
                });

        [Fact]
        public Task IntColumn_ReadsAsInt32Array() =>
            RunAsync(MockResult.Builder().Int("v", 1, null, int.MinValue, int.MaxValue).Build(),
                (Int32Array c) =>
                {
                    Assert.Equal(1, c.GetValue(0));
                    Assert.Null(c.GetValue(1));
                    Assert.Equal(int.MinValue, c.GetValue(2));
                    Assert.Equal(int.MaxValue, c.GetValue(3));
                });

        [Fact]
        public Task BigintColumn_ReadsAsInt64Array() =>
            RunAsync(MockResult.Builder().Bigint("v", 0L, long.MaxValue, null).Build(),
                (Int64Array c) =>
                {
                    Assert.Equal(0L, c.GetValue(0));
                    Assert.Equal(long.MaxValue, c.GetValue(1));
                    Assert.Null(c.GetValue(2));
                });

        [Fact]
        public Task DoubleColumn_ReadsAsDoubleArray() =>
            RunAsync(MockResult.Builder().Double("v", 1.5, -3.14, null).Build(),
                (DoubleArray c) =>
                {
                    Assert.Equal(1.5, c.GetValue(0));
                    Assert.Equal(-3.14, c.GetValue(1));
                    Assert.Null(c.GetValue(2));
                });

        [Fact]
        public Task FloatColumn_ConvertsDoubleWireToFloatArray() =>
            RunAsync(MockResult.Builder().Float("v", 1.5f, -3.14f, null).Build(),
                (FloatArray c) =>
                {
                    // Float schema with double wire → driver converts to FloatArray
                    // when DataTypeConversion.Scalar is in effect (the default).
                    Assert.Equal(1.5f, c.GetValue(0));
                    Assert.Equal(-3.14f, c.GetValue(1));
                    Assert.Null(c.GetValue(2));
                });

        [Fact]
        public Task FloatColumn_LargeBatch_NarrowsWithNulls()
        {
            // 50 values exercise the vectorized double->float narrow path plus
            // the scalar tail, with interleaved nulls (the validity bitmap is
            // reused from the double source). Float values promoted to double on
            // the wire narrow back exactly.
            var expected = new float?[50];
            for (int i = 0; i < expected.Length; i++)
            {
                expected[i] = (i % 7 == 0) ? (float?)null : (i * 1.5f - 3.25f);
            }

            return RunAsync(MockResult.Builder().Float("v", expected).Build(),
                (FloatArray c) =>
                {
                    Assert.Equal(expected.Length, c.Length);
                    for (int i = 0; i < expected.Length; i++)
                    {
                        Assert.Equal(expected[i], c.GetValue(i));
                    }
                });
        }

        [Fact]
        public Task StringColumn_ReadsAsStringArray() =>
            RunAsync(MockResult.Builder().String("v", "alpha", "beta", null, "").Build(),
                (StringArray c) =>
                {
                    Assert.Equal("alpha", c.GetString(0));
                    Assert.Equal("beta", c.GetString(1));
                    Assert.True(c.IsNull(2));
                    Assert.Equal(string.Empty, c.GetString(3));
                });

        [Fact]
        public async Task StringColumn_Empty_DecodesWithoutError()
        {
            // A zero-length string column must decode cleanly (the value buffer
            // starts empty and is wrapped as a zero-length slice). The driver
            // signals end-of-stream with a null or zero-row batch; both are fine.
            using var scenario = HiveMockServer.Create();
            scenario.Stub.OnExecuteStatement = _ => MockResult.Builder().String("v" /* no values */).Build();

            using var statement = scenario.NewStatement();
            statement.SqlQuery = "SELECT v FROM t WHERE false";
            var result = await statement.ExecuteQueryAsync();
            using var stream = result.Stream!;
            using var batch = await stream.ReadNextRecordBatchAsync();
            if (batch != null)
            {
                Assert.Equal(0, batch.Length);
                var c = Assert.IsType<StringArray>(batch.Column(0));
                Assert.Equal(0, c.Length);
            }
        }

        [Fact]
        public Task StringColumn_LargeBatch_GrowsValueBuffer()
        {
            // ~2,500 strings of varying length push the value buffer well past
            // its 64 KB initial capacity, exercising the grow-and-wrap path with
            // nulls and empty strings interleaved.
            var expected = new string?[2500];
            for (int i = 0; i < expected.Length; i++)
            {
                expected[i] = (i % 11 == 0) ? null
                            : (i % 13 == 0) ? string.Empty
                            : $"row-{i}-" + new string((char)('a' + (i % 26)), i % 60);
            }

            return RunAsync(MockResult.Builder().String("v", expected).Build(),
                (StringArray c) =>
                {
                    Assert.Equal(expected.Length, c.Length);
                    for (int i = 0; i < expected.Length; i++)
                    {
                        if (expected[i] == null)
                        {
                            Assert.True(c.IsNull(i));
                        }
                        else
                        {
                            Assert.Equal(expected[i], c.GetString(i));
                        }
                    }
                });
        }

        [Fact]
        public Task BinaryColumn_LargeElement_GrowsBeyondInitialCapacity()
        {
            // The second element (200 KB) is larger than twice the current
            // capacity, exercising the "required size wins over doubling" branch.
            var small = new byte[] { 9, 8, 7 };
            var big = new byte[200_000];
            for (int i = 0; i < big.Length; i++)
            {
                big[i] = (byte)(i % 251);
            }

            return RunAsync(MockResult.Builder().Binary("v", small, big, null).Build(),
                (BinaryArray c) =>
                {
                    Assert.Equal(small, c.GetBytes(0).ToArray());
                    Assert.Equal(big, c.GetBytes(1).ToArray());
                    Assert.True(c.IsNull(2));
                });
        }

        [Fact]
        public async Task VarcharAndCharColumns_ReadAsStringArray()
        {
            using var scenario = HiveMockServer.Create();
            scenario.Stub.OnExecuteStatement = _ => MockResult.Builder()
                .Varchar("v", "x", "yy")
                .Char("c", length: 4, "abcd", "wxyz")
                .Build();

            using var statement = scenario.NewStatement();
            statement.SqlQuery = "SELECT v, c FROM t";
            var result = await statement.ExecuteQueryAsync();
            using var stream = result.Stream!;
            using var batch = await stream.ReadNextRecordBatchAsync();
            Assert.NotNull(batch);

            var v = Assert.IsType<StringArray>(batch.Column(0));
            var c = Assert.IsType<StringArray>(batch.Column(1));
            Assert.Equal("x", v.GetString(0));
            Assert.Equal("yy", v.GetString(1));
            Assert.Equal("abcd", c.GetString(0));
            Assert.Equal("wxyz", c.GetString(1));
        }

        [Fact]
        public Task BinaryColumn_ReadsAsBinaryArray() =>
            RunAsync(MockResult.Builder().Binary("v", new byte[] { 1, 2, 3 }, System.Array.Empty<byte>(), null).Build(),
                (BinaryArray c) =>
                {
                    Assert.Equal(new byte[] { 1, 2, 3 }, c.GetBytes(0).ToArray());
                    Assert.Empty(c.GetBytes(1).ToArray());
                    Assert.True(c.IsNull(2));
                });

        [Fact]
        public Task DateColumn_ConvertsToDate32Array() =>
            RunAsync(MockResult.Builder().Date("d",
                new DateTime(2024, 1, 15),
                new DateTime(2020, 12, 31),
                null).Build(),
                (Date32Array c) =>
                {
                    Assert.Equal(new DateTime(2024, 1, 15), c.GetDateTime(0));
                    Assert.Equal(new DateTime(2020, 12, 31), c.GetDateTime(1));
                    Assert.True(c.IsNull(2));
                });

        [Fact]
        public async Task TimestampColumn_ConvertsToTimestampArray()
        {
            var value = new DateTime(2024, 6, 1, 12, 34, 56).AddTicks(1234560);
            using var scenario = HiveMockServer.Create();
            scenario.Stub.OnExecuteStatement = _ => MockResult.Builder().Timestamp("t",
                value,
                new DateTime(1999, 12, 31, 23, 59, 59),
                null).Build();

            using var statement = scenario.NewStatement();
            statement.SqlQuery = "SELECT t FROM t";
            QueryResult result = await statement.ExecuteQueryAsync();
            using IArrowArrayStream stream = result.Stream!;
            var streamType = Assert.IsType<TimestampType>(stream.Schema.FieldsList[0].DataType);
            Assert.Equal(TimeUnit.Microsecond, streamType.Unit);
            Assert.Null(streamType.Timezone);

            using RecordBatch batch = await stream.ReadNextRecordBatchAsync();
            Assert.NotNull(batch);
            var batchType = Assert.IsType<TimestampType>(batch.Schema.FieldsList[0].DataType);
            var column = Assert.IsType<TimestampArray>(batch.Column(0));
            var arrayType = Assert.IsType<TimestampType>(column.Data.DataType);
            Assert.Equal(streamType.Unit, batchType.Unit);
            Assert.Equal(streamType.Timezone, batchType.Timezone);
            Assert.Equal(batchType.Unit, arrayType.Unit);
            Assert.Equal(batchType.Timezone, arrayType.Timezone);
            Assert.Equal(new DateTimeOffset(value, TimeSpan.Zero), column.GetTimestamp(0));
            Assert.Equal(new DateTimeOffset(1999, 12, 31, 23, 59, 59, TimeSpan.Zero), column.GetTimestamp(1));
            Assert.True(column.IsNull(2));
        }

        [Fact]
        public Task DecimalColumn_ConvertsToDecimal128Array() =>
            RunAsync(MockResult.Builder().Decimal("d", precision: 10, scale: 2, 4.56m, -7.89m, null).Build(),
                (Decimal128Array c) =>
                {
                    Assert.Equal(SqlDecimal.Parse("4.56"), c.GetSqlDecimal(0));
                    Assert.Equal(SqlDecimal.Parse("-7.89"), c.GetSqlDecimal(1));
                    Assert.Null(c.GetSqlDecimal(2));
                });

        [Fact]
        public async Task MultiColumnResult_PreservesSchemaOrder()
        {
            using var scenario = HiveMockServer.Create();
            scenario.Stub.OnExecuteStatement = _ => MockResult.Builder()
                .Bigint("id", 10, 20)
                .String("name", "a", "b")
                .Bool("active", true, false)
                .Build();

            using var statement = scenario.NewStatement();
            statement.SqlQuery = "SELECT id, name, active FROM t";
            var result = await statement.ExecuteQueryAsync();
            using var stream = result.Stream!;
            Assert.Equal(3, stream.Schema.FieldsList.Count);
            Assert.Equal("id", stream.Schema.FieldsList[0].Name);
            Assert.Equal("name", stream.Schema.FieldsList[1].Name);
            Assert.Equal("active", stream.Schema.FieldsList[2].Name);

            using var batch = await stream.ReadNextRecordBatchAsync();
            Assert.NotNull(batch);
            Assert.Equal(2, batch.Length);
            var ids = Assert.IsType<Int64Array>(batch.Column(0));
            var names = Assert.IsType<StringArray>(batch.Column(1));
            var active = Assert.IsType<BooleanArray>(batch.Column(2));
            Assert.Equal(20L, ids.GetValue(1));
            Assert.Equal("a", names.GetString(0));
            Assert.True(active.GetValue(0));
        }

        [Fact]
        public async Task EmptyResultSet_ReturnsEndOfStream()
        {
            using var scenario = HiveMockServer.Create();
            scenario.Stub.OnExecuteStatement = _ => MockResult.Builder().Bigint("v" /* no values */).Build();

            using var statement = scenario.NewStatement();
            statement.SqlQuery = "SELECT v FROM t WHERE false";
            var result = await statement.ExecuteQueryAsync();
            using var stream = result.Stream!;

            using var batch = await stream.ReadNextRecordBatchAsync();
            // The driver may surface a zero-row batch or a null; both signal end-of-stream
            // and the next read must be null.
            if (batch != null)
            {
                Assert.Equal(0, batch.Length);
            }
            using var next = await stream.ReadNextRecordBatchAsync();
            Assert.Null(next);
        }

        /// <summary>
        /// Executes the canned <paramref name="result"/>, materializes the
        /// first batch, casts its first column to <typeparamref name="T"/>,
        /// and runs <paramref name="assertions"/> while the batch is still
        /// alive. The batch and stream are disposed after the callback
        /// returns; this matters because some Arrow array types release
        /// their backing buffers on dispose, which would NRE if the caller
        /// accessed the column afterward.
        /// </summary>
        private static async Task RunAsync<T>(MockResult result, Action<T> assertions) where T : IArrowArray
        {
            using var scenario = HiveMockServer.Create();
            scenario.Stub.OnExecuteStatement = _ => result;

            using var statement = scenario.NewStatement();
            statement.SqlQuery = "SELECT * FROM t";
            QueryResult queryResult = await statement.ExecuteQueryAsync();
            using IArrowArrayStream stream = queryResult.Stream!;
            using RecordBatch batch = await stream.ReadNextRecordBatchAsync();
            Assert.NotNull(batch);
            var column = Assert.IsType<T>(batch.Column(0));
            assertions(column);
        }
    }
}
