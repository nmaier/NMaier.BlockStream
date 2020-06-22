using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NMaier.BlockStream.Tests
{
  [TestClass]
  [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
  public sealed class BlockStreamTests
  {
    private static void BlockStreamWriterOnceTest(IBlockTransformer transformer, IBlockCache cache)
    {
      using var ms = new KeepOpenMemoryStream();
      const int COUNT = 100_000;

      long expectedLength;
      using (var writer = new BlockWriteOnceStream(ms, transformer)) {
        using var binaryWriter = new BinaryWriter(writer, Encoding.ASCII, true);
        for (var i = 0; i < COUNT; ++i) {
          binaryWriter.Write(i);
        }

        writer.Write(new byte[1 << 22]);
        expectedLength = writer.Length;
      }

      using (var writer = new BlockReadOnlyStream(ms, transformer, cache: cache)) {
        Assert.AreEqual(expectedLength, writer.Length);
        using var binaryReader = new BinaryReader(writer, Encoding.ASCII, true);
        using var binaryCursorReader = new BinaryReader(writer.CreateCursor(), Encoding.ASCII, true);
        for (var i = 0; i < COUNT; ++i) {
          Assert.AreEqual(i, binaryReader.ReadInt32());
          Assert.AreEqual(i, binaryCursorReader.ReadInt32());
        }

        var buf = new byte[1 << 22];
        Assert.AreEqual(buf.Length, writer.Read(buf));
        Assert.IsTrue(buf.All(i => i == 0));
      }
    }


    private static void BlockStreamWriterSizeTestInternal(IBlockTransformer transformer)
    {
      BlockStreamWriterSizeTestRunner(transformer, null);
      BlockStreamWriterSizeTestRunner(transformer, new BlockCache());
    }

    private static void BlockStreamWriterSizeTestRunner(IBlockTransformer transformer, [CanBeNull] IBlockCache cache)
    {
      BlockStreamWriterOnceTest(transformer, cache);

      using var ms = new KeepOpenMemoryStream();
      var items = 10_000;
      using (var writer = new BlockRandomAccessStream(ms, transformer, cache: cache)) {
        using (var binaryWriter = new BinaryWriter(writer, Encoding.ASCII, true)) {
          for (var i = 0; i < items; ++i) {
            binaryWriter.Write(i);
          }
        }

        Assert.AreEqual(writer.Position, writer.Length);
        writer.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual(writer.Position, 0);
        Assert.AreEqual(writer.Length, sizeof(int) * items);

        using var binaryReader = new BinaryReader(writer, Encoding.ASCII, true);
        for (var i = 0; i < items; ++i) {
          Assert.AreEqual(i, binaryReader.ReadInt32());
        }
      }

      using (var writer = new BlockRandomAccessStream(ms, transformer, cache: cache)) {
        Assert.AreEqual(writer.Position, 0);
        Assert.AreEqual(writer.Length, sizeof(int) * items);
        using (var binaryReader = new BinaryReader(writer, Encoding.ASCII, true)) {
          for (var i = 0; i < items; ++i) {
            Assert.AreEqual(i, binaryReader.ReadInt32());
          }
        }

        Assert.AreEqual(writer.Position, writer.Length);
        Assert.AreEqual(writer.Length, sizeof(int) * items);

        if (transformer.MayChangeSize) {
          Assert.ThrowsException<IOException>(() => {
            writer.Seek(sizeof(int), SeekOrigin.Begin);
            using var binaryWriter = new BinaryWriter(writer, Encoding.ASCII, true);
            for (var i = 0; i < items; ++i) {
              binaryWriter.Write(i);
            }
          });
          Assert.AreEqual(sizeof(int), writer.Position);
        }

        writer.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual(writer.Position, 0);
        Assert.AreEqual(writer.Length, sizeof(int) * items);

        using (var binaryReader = new BinaryReader(writer, Encoding.ASCII, true)) {
          for (var i = 0; i < items; ++i) {
            Assert.AreEqual(i, binaryReader.ReadInt32());
          }
        }

        Assert.AreEqual(writer.Position, writer.Length);
        writer.Seek(-4, SeekOrigin.End);
        Assert.AreEqual(writer.Position + 4, writer.Length);
        Assert.AreEqual(4, writer.Read(new byte[5]));
        Assert.AreEqual(writer.Position, writer.Length);
        Assert.AreEqual(0, writer.Read(new byte[5]));

        writer.SetLength(0);
        Assert.AreEqual(writer.Length, 0);
        Assert.AreEqual(writer.Position, 0);






        writer.Flush(true);
      }
    }

    [TestMethod]
    public void BlockStreamWriterAESTest()
    {
      BlockStreamWriterSizeTestInternal(new AESAndMACTransformer("test"));
    }

    [TestMethod]
    public void BlockStreamWriterChaTest()
    {
      BlockStreamWriterSizeTestInternal(new ChaChaAndPolyTransformer("test2"));
    }

    [TestMethod]
    public void BlockStreamWriterChecksumTest()
    {
      BlockStreamWriterSizeTestInternal(new ChecksumTransformer());
    }

    [TestMethod]
    public void BlockStreamWriterComp2Test()
    {
      BlockStreamWriterSizeTestInternal(
        new CompositeTransformer(new TransformerTests.TestTransformer(), new NoneBlockTransformer()));
    }

    [TestMethod]
    public void BlockStreamWriterCompAesLZ4Test()
    {
      BlockStreamWriterSizeTestInternal(
        new CompositeTransformer(new NoneBlockTransformer(), new AESAndMACTransformer("test3"),
                                 new LZ4CompressorTransformer()));
    }

    [TestMethod]
    public void BlockStreamWriterCompLZ4ChaTest()
    {
      BlockStreamWriterSizeTestInternal(
        new CompositeTransformer(new LZ4CompressorTransformer(), new ChaChaAndPolyTransformer("test4"),
                                 new NoneBlockTransformer()));
    }

    [TestMethod]
    public void BlockStreamWriterCompTest()
    {
      BlockStreamWriterSizeTestInternal(
        new CompositeTransformer(new NoneBlockTransformer(), new TransformerTests.TestTransformer()));
    }

    [TestMethod]
    public void BlockStreamWriterEncCompTest()
    {
      BlockStreamWriterSizeTestInternal(new EncryptedCompressedTransformer("test222"));
    }

    [TestMethod]
    public void BlockStreamWriterLZ4Test()
    {
      BlockStreamWriterSizeTestInternal(new LZ4CompressorTransformer());
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
        writer.Seek(0, SeekOrigin.Begin);
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

        writer.Seek(sizeof(int), SeekOrigin.Begin);
        using (var binaryWriter = new BinaryWriter(writer, Encoding.ASCII, true)) {
          for (var i = 0; i < COUNT; ++i) {
            binaryWriter.Write(i);
          }
        }

        Assert.AreEqual(writer.Position, writer.Length);
        writer.Seek(0, SeekOrigin.Begin);
        Assert.AreEqual(writer.Position, 0);
        Assert.AreEqual(writer.Length, sizeof(int) * (COUNT + 1));

        using (var binaryReader = new BinaryReader(writer, Encoding.ASCII, false)) {
          for (var i = 0; i < COUNT + 1; ++i) {
            Assert.AreEqual(Math.Max(i - 1, 0), binaryReader.ReadInt32());
          }
        }

        Assert.AreEqual(writer.Position, writer.Length);
        writer.Seek(-4, SeekOrigin.End);
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
      BlockStreamWriterSizeTestInternal(new NoneBlockTransformer());
    }

    [TestMethod]
    public void BlockStreamWriterSizeTest()
    {
      BlockStreamWriterSizeTestInternal(new TransformerTests.TestTransformer());
    }

    private sealed class BlockCache : IBlockCache
    {
      readonly Dictionary<long, byte[]> items = new Dictionary<long, byte[]>();

      public void Cache(Span<byte> block, long offset)
      {
        items[offset] = block.ToArray();
      }

      public void Invalidate(long offset)
      {
        items.Remove(offset);
      }

      public bool TryReadBlock(Span<byte> block, long offset)
      {
        if (items.TryGetValue(offset, out var b)) {
          b.AsSpan(0, block.Length).CopyTo(block);
          return true;
        }

        return false;
      }

      public void Dispose()
      {
        items.Clear();
      }
    }
  }
}