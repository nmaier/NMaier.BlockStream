using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using JetBrains.Annotations;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using NMaier.BlockStream.Transformers;

namespace NMaier.BlockStream.Tests
{
  [TestClass]
  public sealed class BlockStreamTests
  {
    private static void BlockStreamSequentialTestRunner(IBlockTransformer transformer)
    {
      var buf = new byte[1 << 20];
      var outbuf = new byte[1 << 20];
      buf.AsSpan().Fill(0x03);
      using var ms = new KeepOpenMemoryStream();
      using (var writer = new SequentialBlockWriteOnceStream(ms, transformer)) {
        Assert.IsTrue(writer.CanWrite);
        Assert.IsFalse(writer.CanRead);
        Assert.IsFalse(writer.CanSeek);
        Assert.IsFalse(writer.CanTimeout);
        for (var i = 0; i < buf.Length; i += (int)Math.Floor(buf.Length / 20.0) + 1) {
          buf[i] = (byte)(i % byte.MaxValue);
          writer.Write(buf.AsSpan(0, i));
          writer.Flush(true);
        }

        for (var i = 0; i < buf.Length; i += (int)Math.Floor(buf.Length / 20.0) + 1) {
          buf[i] = (byte)(i % byte.MaxValue);
          writer.Write(buf, 0, i);
          writer.Flush(true);
        }

        Assert.AreEqual(writer.Position, 19923020);

        // ReSharper disable AccessToDisposedClosure
        _ = Assert.ThrowsException<NotSupportedException>(() => writer.Seek(0, 0));
        _ = Assert.ThrowsException<NotSupportedException>(() => writer.SetLength(0));
        _ = Assert.ThrowsException<NotSupportedException>(() => writer.Read(new byte[1]));
        _ = Assert.ThrowsException<NotSupportedException>(
          () => writer.Read(new byte[1], 0, 1));
        _ = Assert.ThrowsException<NotSupportedException>(() => writer.Position = 0);
        _ = Assert.ThrowsException<NotSupportedException>(() => writer.Length);
        // ReSharper restore AccessToDisposedClosure
      }

      _ = ms.Seek(0, SeekOrigin.Begin);
      using (var reader = new SequentialBlockReadOnlyStream(ms, transformer)) {
        Assert.IsTrue(reader.CanRead);
        Assert.IsFalse(reader.CanWrite);
        Assert.IsFalse(reader.CanSeek);
        Assert.IsFalse(reader.CanTimeout);
        Assert.AreEqual(reader.Position, 0);
        for (var i = 0; i < buf.Length; i += (int)Math.Floor(buf.Length / 20.0) + 1) {
          outbuf.AsSpan(0, i).Clear();
          var read = reader.Read(outbuf.AsSpan(0, i));
          Assert.AreEqual(i, read);
          Assert.IsTrue(buf.AsSpan(0, i).SequenceEqual(outbuf.AsSpan(0, i)));
        }

        for (var i = 0; i < buf.Length; i += (int)Math.Floor(buf.Length / 20.0) + 1) {
          outbuf.AsSpan(0, i).Clear();
          var read = reader.Read(outbuf, 0, i);
          Assert.AreEqual(i, read);
          Assert.IsTrue(buf.AsSpan(0, i).SequenceEqual(outbuf.AsSpan(0, i)));
        }

        Assert.AreEqual(reader.Position, 19923020);

        // ReSharper disable AccessToDisposedClosure
#if NETFRAMEWORK
      _ =
 Assert.ThrowsException<NotSupportedException>(() => reader.Write(new byte[1], 0, 1));
#else
        _ = Assert.ThrowsException<NotSupportedException>(
          () => reader.Write(new byte[1]));
#endif
        _ = Assert.ThrowsException<NotSupportedException>(() => reader.Seek(0, 0));
        _ = Assert.ThrowsException<NotSupportedException>(() => reader.SetLength(0));
        _ = Assert.ThrowsException<NotSupportedException>(() => reader.Position = 0);
        _ = Assert.ThrowsException<NotSupportedException>(() => reader.Length);
        // ReSharper restore AccessToDisposedClosure
      }

      _ = ms.Seek(0, SeekOrigin.Begin);
      using (var reader = new SequentialBlockReadOnlyStream(ms, transformer)) {
        reader.ReadFullBlock(new byte[10]);
        reader.ReadFullBlock(new byte[10]);
        reader.ReadFullBlock(new byte[10]);
        Assert.AreEqual(reader.Position, 30);
      }

      using var ms2 = new MemoryStream();
      using (var writer =
             new SequentialBlockWriteOnceStream(ms2, transformer, leaveOpen: true)) {
        writer.Write(
          new byte[] {
            0x1,
            0xff
          });
      }

      ms2.Seek(0, SeekOrigin.Begin);
      using (var reader =
             new SequentialBlockReadOnlyStream(ms2, transformer, leaveOpen: true)) {
        Span<byte> actual = stackalloc byte[2];
        reader.ReadFullBlock(actual);
        Assert.IsTrue(
          actual.SequenceEqual(
            new byte[] {
              0x1,
              0xff
            }));
      }
    }


    private static void BlockStreamTestRunner(IBlockTransformer transformer)
    {
      BlockStreamWriterSizeTestRunner(transformer, null);
      BlockStreamWriterSizeTestRunner(transformer, new BlockCache());
      BlockStreamSequentialTestRunner(transformer);
      BlockStreamWriteOnceTestRunner(transformer, null);
      BlockStreamWriteOnceTestRunner(transformer, new BlockCache());
    }

    private static void BlockStreamWriteOnceTestRunner(IBlockTransformer transformer,
      [CanBeNull] IBlockCache cache)
    {
      using var ms = new KeepOpenMemoryStream();
      const int COUNT = 100_000;

      var r = new byte[100];
      var g = RandomNumberGenerator.Create();
      g.GetBytes(r);
      ms.Write(r, 0, r.Length);

      long expectedLength;
      using (var writer = new BlockWriteOnceStream(ms, transformer, blockSize: 512)) {
        Assert.IsTrue(writer.CanWrite);
        Assert.IsFalse(writer.CanRead);
        Assert.IsFalse(writer.CanSeek);
        Assert.IsFalse(writer.CanTimeout);
        using var binaryWriter = new BinaryWriter(writer, Encoding.ASCII, true);
        for (var i = 0; i < COUNT; ++i) {
          binaryWriter.Write(i);
        }

        writer.Write(new byte[1 << 22]);
        expectedLength = writer.Length;
      }

      using (var reader = new BlockReadOnlyStream(
               ms,
               transformer,
               blockSize: 512,
               cache: cache)) {
        Assert.AreEqual(expectedLength, reader.Length);
        Assert.IsTrue(reader.CanRead);
        Assert.IsTrue(reader.CanSeek);
        Assert.IsFalse(reader.CanWrite);
        Assert.IsFalse(reader.CanTimeout);
        Assert.AreEqual(reader.Position, 0);
        _ = reader.Seek(0, SeekOrigin.End);
        Assert.AreEqual(reader.Position, reader.Length);
        _ = reader.Seek(-10, SeekOrigin.Current);
        Assert.AreEqual(reader.Position, reader.Length - 10);
        reader.Position -= 10;
        Assert.AreEqual(reader.Position, reader.Length - 20);
        _ = reader.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual(reader.Position, 0);


        Assert.AreEqual(reader.Seek(0, SeekOrigin.End), reader.Length);
        Assert.AreEqual(reader.Seek(-10, SeekOrigin.Current), reader.Length - 10);
        Assert.AreEqual(reader.Seek(0, SeekOrigin.Begin), 0);

        // ReSharper disable AccessToDisposedClosure
        _ = Assert.ThrowsException<ArgumentOutOfRangeException>(
          () => reader.Seek(-10, SeekOrigin.Begin));
        _ = Assert.ThrowsException<ArgumentOutOfRangeException>(
          () => reader.Seek(-10, SeekOrigin.Current));
        _ = Assert.ThrowsException<ArgumentOutOfRangeException>(
          () => reader.Seek(-reader.Length - 10, SeekOrigin.End));
        _ = Assert.ThrowsException<ArgumentOutOfRangeException>(
          () => reader.Seek(0, (SeekOrigin)(-1)));
        // ReSharper restore AccessToDisposedClosure

        // ReSharper disable AccessToDisposedClosure
        _ = Assert.ThrowsException<NotSupportedException>(() => reader.SetLength(0));
#if NETFRAMEWORK
        _ = Assert.ThrowsException<NotSupportedException>(
          () => reader.Write(new byte[1], 0, 1));
#else
        _ = Assert.ThrowsException<NotSupportedException>(
          () => reader.Write(new byte[1]));
#endif
        // ReSharper restore AccessToDisposedClosure

        using var binaryReader = new BinaryReader(reader, Encoding.ASCII, true);
        using var binaryCursorReader = new BinaryReader(
          reader.CreateCursor(),
          Encoding.ASCII,
          true);
        for (var i = 0; i < COUNT; ++i) {
          Assert.AreEqual(i, binaryReader.ReadInt32());
          Assert.AreEqual(i, binaryCursorReader.ReadInt32());
        }

        var buf = new byte[1 << 22];
        Assert.AreEqual(buf.Length, reader.Read(buf));
        Assert.IsTrue(buf.All(i => i == 0));
      }

      _ = ms.Seek(0, SeekOrigin.Begin);
      var r2 = new byte[r.Length];
      ms.ReadFullBlock(r2);
      Assert.IsTrue(r.AsSpan().SequenceEqual(r2));

      using var ms2 = new MemoryStream();
      using (var writer2 = new BlockWriteOnceStream(
               ms2,
               transformer,
               leaveOpen: true,
               blockSize: 512)) {
        writer2.Write(
          new byte[] {
            0x1,
            0xff
          });
      }

      ms2.Seek(0, SeekOrigin.Begin);
      using (var reader2 = new BlockReadOnlyStream(
               ms2,
               transformer,
               leaveOpen: true,
               blockSize: 512)) {
        Span<byte> actual = stackalloc byte[2];
        reader2.ReadFullBlock(actual);
        Assert.IsTrue(
          actual.SequenceEqual(
            new byte[] {
              0x1,
              0xff
            }));
      }

      Assert.AreNotEqual(0, ms2.Position);
    }

    private static void BlockStreamWriterSizeTestRunner(IBlockTransformer transformer,
      [CanBeNull] IBlockCache cache)
    {
      const int ITEMS = 10_000;

      using var ms = new KeepOpenMemoryStream();
      var r = new byte[100];
      var g = RandomNumberGenerator.Create();
      g.GetBytes(r);
      ms.Write(r, 0, r.Length);

      using (var writer = new BlockRandomAccessStream(ms, transformer, cache: cache)) {
        Assert.IsTrue(writer.CanRead);
        Assert.IsTrue(writer.CanSeek);
        Assert.IsTrue(writer.CanWrite);
        Assert.IsFalse(writer.CanTimeout);

        using (var binaryWriter = new BinaryWriter(writer, Encoding.ASCII, true)) {
          for (var i = 0; i < ITEMS; ++i) {
            binaryWriter.Write(i);
          }
        }

        writer.Flush();
        writer.Flush(true);

        Assert.AreEqual(writer.Position, writer.Length);
        _ = writer.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual(writer.Position, 0);
        Assert.AreEqual(writer.Length, sizeof(int) * ITEMS);

        using var binaryReader = new BinaryReader(writer, Encoding.ASCII, true);
        for (var i = 0; i < ITEMS; ++i) {
          Assert.AreEqual(i, binaryReader.ReadInt32());
        }
      }

      _ = ms.Seek(0, SeekOrigin.Begin);
      var r2 = new byte[r.Length];
      ms.ReadFullBlock(r2);
      Assert.IsTrue(r.AsSpan().SequenceEqual(r2));

      using (var writer = new BlockRandomAccessStream(ms, transformer, cache: cache)) {
        Assert.AreEqual(writer.Position, 0);
        Assert.AreEqual(writer.Length, sizeof(int) * ITEMS);

        using (var binaryReader = new BinaryReader(writer, Encoding.ASCII, true)) {
          for (var i = 0; i < ITEMS; ++i) {
            Assert.AreEqual(i, binaryReader.ReadInt32());
          }
        }

        Assert.AreEqual(writer.Position, writer.Length);
        Assert.AreEqual(writer.Length, sizeof(int) * ITEMS);

        if (transformer.MayChangeSize) {
          void WriteSome()
          {
            // ReSharper disable AccessToDisposedClosure
            _ = writer.Seek(sizeof(int), SeekOrigin.Begin);
            using var binaryWriter = new BinaryWriter(writer, Encoding.ASCII, true);
            for (var i = 0; i < ITEMS; ++i) {
              binaryWriter.Write(i);
            }
            // ReSharper restore AccessToDisposedClosure
          }

          _ = Assert.ThrowsException<IOException>(WriteSome);
          Assert.AreEqual(sizeof(int), writer.Position);
        }

        _ = writer.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual(writer.Position, 0);
        Assert.AreEqual(writer.Length, sizeof(int) * ITEMS);

        using (var binaryReader = new BinaryReader(writer, Encoding.ASCII, true)) {
          for (var i = 0; i < ITEMS; ++i) {
            Assert.AreEqual(i, binaryReader.ReadInt32());
          }
        }

        Assert.AreEqual(writer.Position, writer.Length);
        _ = writer.Seek(-4, SeekOrigin.End);
        Assert.AreEqual(writer.Position + 4, writer.Length);
        Assert.AreEqual(4, writer.Read(new byte[5]));
        Assert.AreEqual(writer.Position, writer.Length);
        Assert.AreEqual(0, writer.Read(new byte[5]));

        writer.SetLength(writer.Length + 10);
        Assert.AreNotEqual(writer.Position, writer.Length);
        var buffer = new byte[10];
        Assert.AreEqual(10, writer.Read(buffer));
        Assert.AreEqual(writer.Position, writer.Length);
        Assert.IsTrue(buffer.All(b => b == 0));

        Assert.AreEqual(writer.Seek(-5, SeekOrigin.End), writer.Length - 5);
        Assert.AreEqual(5, writer.Read(buffer));
        Assert.AreEqual(writer.Position, writer.Length);
        Assert.IsTrue(buffer.All(b => b == 0));

        Assert.AreEqual(writer.Seek(5, SeekOrigin.End), writer.Length + 5);
        Assert.AreEqual(0, writer.Read(buffer));
        Assert.AreEqual(writer.Position, writer.Length);
        Assert.IsTrue(buffer.All(b => b == 0));


        writer.SetLength(10);
        Assert.AreEqual(writer.Length, 10);
        Assert.AreEqual(writer.Position, 10);

        writer.Position = 3;
        writer.SetLength(5);
        Assert.AreEqual(writer.Length, 5);
        Assert.AreEqual(writer.Position, 3);

        writer.SetLength(0);
        Assert.AreEqual(writer.Length, 0);
        Assert.AreEqual(writer.Position, 0);

        writer.Flush(true);
      }

      _ = ms.Seek(0, SeekOrigin.Begin);
      ms.ReadFullBlock(r2);
      Assert.IsTrue(r.AsSpan().SequenceEqual(r2));

      using var ms2 = new MemoryStream();
      using (var writer2 = new BlockRandomAccessStream(
               ms2,
               transformer,
               leaveOpen: true,
               blockSize: 512)) {
        writer2.Write(
          new byte[] {
            0x1,
            0xff
          });
      }

      _ = ms2.Seek(0, SeekOrigin.Begin);
      using (var reader2 = new BlockRandomAccessStream(
               ms2,
               transformer,
               leaveOpen: true,
               blockSize: 512)) {
        Span<byte> actual = stackalloc byte[2];
        reader2.ReadFullBlock(actual);
        Assert.IsTrue(
          actual.SequenceEqual(
            new byte[] {
              0x1,
              0xff
            }));
      }

      Assert.AreNotEqual(0, ms2.Position);
    }

    [TestMethod]
    public void BlockRandomAccessStreamTest()
    {
      _ = Assert.ThrowsException<ArgumentException>(
        () => new BlockRandomAccessStream(new FakeStream(false, false, false)));
      _ = Assert.ThrowsException<ArgumentException>(
        () => new BlockRandomAccessStream(new FakeStream(false, true, false)));
      _ = Assert.ThrowsException<ArgumentException>(
        () => new BlockRandomAccessStream(new FakeStream(true, true, false)));
    }

    [TestMethod]
    public void BlockStreamWriterAESTest()
    {
      BlockStreamTestRunner(new AESAndMACTransformer("test"));
    }

    [TestMethod]
    public void BlockStreamWriterChaTest()
    {
      BlockStreamTestRunner(new ChaChaAndPolyTransformer("test2"));
    }

    [TestMethod]
    public void BlockStreamWriterChecksumTest()
    {
      BlockStreamTestRunner(new ChecksumTransformer());
    }

    [TestMethod]
    public void BlockStreamWriterComp2Test()
    {
      BlockStreamTestRunner(
        new CompositeTransformer(
          new TransformerTests.TestTransformer(),
          new NoneBlockTransformer()));
    }

    [TestMethod]
    public void BlockStreamWriterCompLZ4ChaTest()
    {
      BlockStreamTestRunner(
        new CompositeTransformer(
          new LZ4CompressorTransformer(),
          new ChaChaAndPolyTransformer("test4"),
          new NoneBlockTransformer()));
    }

    [TestMethod]
    public void BlockStreamWriterCompTest()
    {
      BlockStreamTestRunner(
        new CompositeTransformer(
          new NoneBlockTransformer(),
          new TransformerTests.TestTransformer()));
    }

    [TestMethod]
    public void BlockStreamWriterEncCompTest()
    {
      BlockStreamTestRunner(new EncryptedCompressedTransformer("test222"));
    }

    [TestMethod]
    public void BlockStreamWriterFileTests()
    {
      const string TMP = "file.bin";
      var trans = new Rot0Transformer();
      using (var fs = new FileStream(TMP, FileMode.Create, FileAccess.ReadWrite))
      using (var writer = new BlockWriteOnceStream(fs, trans)) {
        for (var i = 0; i < 256; ++i) {
          writer.WriteByte((byte)i);
        }

        writer.Flush(true);
      }

      using (var fs = new FileStream(TMP, FileMode.Open, FileAccess.ReadWrite))
      using (var reader = new BlockReadOnlyStream(fs, trans)) {
        for (var i = 0; i < 256; ++i) {
          Assert.AreEqual((byte)i, reader.ReadByte());
        }

        reader.Flush();
      }

      using (var fs = new FileStream(TMP, FileMode.Open, FileAccess.ReadWrite))
      using (var stream = new BlockRandomAccessStream(fs, trans)) {
        for (var i = 0; i < 256; ++i) {
          Assert.AreEqual((byte)i, stream.ReadByte());
        }

        _ = stream.Seek(0, SeekOrigin.Begin);

        for (var i = 0; i < 256; ++i) {
          stream.WriteByte((byte)i);
        }

        for (var i = 0; i < 256; ++i) {
          stream.WriteByte((byte)i);
        }

        stream.Flush(true);
      }

      using (var fs = new FileStream(TMP, FileMode.Open, FileAccess.ReadWrite))
      using (var stream = new BlockRandomAccessStream(fs, trans)) {
        // ReSharper disable AccessToDisposedClosure
        _ = Assert.ThrowsException<ArgumentOutOfRangeException>(
          () => stream.Seek(-10, SeekOrigin.Begin));
        _ = Assert.ThrowsException<ArgumentOutOfRangeException>(
          () => stream.Seek(-10, SeekOrigin.Current));
        _ = Assert.ThrowsException<ArgumentOutOfRangeException>(
          () => stream.Seek(-stream.Length - 10, SeekOrigin.End));
        _ = Assert.ThrowsException<ArgumentOutOfRangeException>(
          () => stream.Seek(0, (SeekOrigin)(-1)));
        // ReSharper restore AccessToDisposedClosure

        Assert.AreEqual(stream.Seek(0, SeekOrigin.End), stream.Length);
        Assert.AreEqual(stream.Seek(-10, SeekOrigin.Current), stream.Length - 10);
        Assert.AreEqual(stream.Seek(0, SeekOrigin.Begin), 0);


        for (var i = 0; i < 256; ++i) {
          Assert.AreEqual((byte)i, stream.ReadByte());
        }

        for (var i = 0; i < 256; ++i) {
          stream.WriteByte((byte)i);
        }

        stream.Flush(true);
      }

      using (var fs = new FileStream(TMP, FileMode.Open, FileAccess.ReadWrite))
      using (var stream = new BlockRandomAccessStream(fs, trans)) {
        for (var i = 0; i < 256; ++i) {
          Assert.AreEqual((byte)i, stream.ReadByte());
        }

        for (var i = 0; i < 256; ++i) {
          Assert.AreEqual((byte)i, stream.ReadByte());
        }

        stream.Flush(true);
      }
    }

    [TestMethod]
    public void BlockStreamWriterLZ4Test()
    {
      BlockStreamTestRunner(new LZ4CompressorTransformer());
    }

    [TestMethod]
    public void BlockStreamWriterNone2Test()
    {
      using var ms = new KeepOpenMemoryStream();
      const int COUNT = 100_000;

      using (var writer = new BlockRandomAccessStream(ms)) {
        using (var binaryWriter = new BinaryWriter(writer, Encoding.ASCII, true)) {
          for (var i = 0; i < COUNT; ++i) {
            binaryWriter.Write(i);
          }
        }

        Assert.AreEqual(writer.Position, writer.Length);
        _ = writer.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual(writer.Position, 0);
        Assert.AreEqual(writer.Length, sizeof(int) * COUNT);

        using var binaryReader = new BinaryReader(writer, Encoding.ASCII, true);
        for (var i = 0; i < COUNT; ++i) {
          Assert.AreEqual(i, binaryReader.ReadInt32());
        }
      }

      using (var writer = new BlockRandomAccessStream(ms)) {
        Assert.AreEqual(writer.Position, 0);
        Assert.AreEqual(writer.Length, sizeof(int) * COUNT);
        using (var binaryReader = new BinaryReader(writer, Encoding.ASCII, true)) {
          for (var i = 0; i < COUNT; ++i) {
            Assert.AreEqual(i, binaryReader.ReadInt32());
          }
        }

        _ = writer.Seek(sizeof(int), SeekOrigin.Begin);
        using (var binaryWriter = new BinaryWriter(writer, Encoding.ASCII, true)) {
          for (var i = 0; i < COUNT; ++i) {
            binaryWriter.Write(i);
          }
        }

        Assert.AreEqual(writer.Position, writer.Length);
        _ = writer.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual(writer.Position, 0);
        Assert.AreEqual(writer.Length, sizeof(int) * (COUNT + 1));

        using (var binaryReader = new BinaryReader(writer, Encoding.ASCII, false)) {
          for (var i = 0; i < COUNT + 1; ++i) {
            Assert.AreEqual(Math.Max(i - 1, 0), binaryReader.ReadInt32());
          }
        }

        Assert.AreEqual(writer.Position, writer.Length);
        _ = writer.Seek(-4, SeekOrigin.End);
        Assert.AreEqual(writer.Position + 4, writer.Length);
        Assert.AreEqual(4, writer.Read(new byte[5]));
        Assert.AreEqual(writer.Position, writer.Length);
        Assert.AreEqual(0, writer.Read(new byte[5]));

        writer.SetLength(0);
        Assert.AreEqual(writer.Length, 0);
        Assert.AreEqual(writer.Position, 0);
      }
    }

    [TestMethod]
    public void BlockStreamWriterNoneTest()
    {
      BlockStreamTestRunner(new NoneBlockTransformer());
    }

    [TestMethod]
    public void BlockStreamWriterSizeTest()
    {
      BlockStreamTestRunner(new TransformerTests.TestTransformer());
    }

    [TestMethod]
    public void BlockWriteOnceStream()
    {
      _ = Assert.ThrowsException<ArgumentException>(
        () => new BlockWriteOnceStream(new FakeStream(false, false, false)));
    }

    [TestMethod]
    public void SequentialBlockReadOnlyStream()
    {
      _ = Assert.ThrowsException<ArgumentException>(
        () => new SequentialBlockReadOnlyStream(new FakeStream(false, false, false)));
    }

    [TestMethod]
    public void SequentialBlockWriteOnceStream()
    {
      _ = Assert.ThrowsException<ArgumentException>(
        () => new SequentialBlockWriteOnceStream(new FakeStream(false, false, false)));
    }

    private sealed class BlockCache : IBlockCache
    {
      private readonly Dictionary<long, byte[]> items = new();

      public void Cache(Span<byte> block, long offset)
      {
        items[offset] = block.ToArray();
      }

      public void Invalidate(long offset)
      {
        _ = items.Remove(offset);
      }

      public bool TryReadBlock(Span<byte> block, long offset)
      {
        if (!items.TryGetValue(offset, out var b)) {
          return false;
        }

        b.AsSpan(0, block.Length).CopyTo(block);
        return true;
      }

      public void Dispose()
      {
        items.Clear();
      }
    }

    private sealed class FakeStream : Stream
    {
      public FakeStream(bool canRead, bool canSeek, bool canWrite)
      {
        CanRead = canRead;
        CanSeek = canSeek;
        CanWrite = canWrite;
      }

      public override bool CanRead { get; }

      public override bool CanSeek { get; }

      public override bool CanWrite { get; }

      public override long Length => 0;

      public override long Position { get; set; }

      public override void Flush()
      {
        throw new NotImplementedException();
      }

      public override int Read(byte[] buffer, int offset, int count)
      {
        throw new NotImplementedException();
      }

      public override long Seek(long offset, SeekOrigin origin)
      {
        throw new NotImplementedException();
      }

      public override void SetLength(long value)
      {
        throw new NotImplementedException();
      }

      public override void Write(byte[] buffer, int offset, int count)
      {
        throw new NotImplementedException();
      }
    }

    private sealed class Rot0Transformer : IBlockTransformer
    {
      public bool MayChangeSize => false;

      public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
      {
        var rv = block.ToArray();
        rv[0] = (byte)(256 - rv[0]);
        return rv;
      }

      public int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block)
      {
        if (input.Overlaps(block, out var off) && off == 0) {
          block[0] = (byte)(256 - block[0]);
          return input.Length;
        }

        input.CopyTo(block);
        block[0] = (byte)(256 - block[0]);
        return input.Length;
      }
    }
  }
}
