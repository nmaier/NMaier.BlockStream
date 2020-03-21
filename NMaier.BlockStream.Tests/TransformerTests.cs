using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace NMaier.BlockStream.Tests
{
  [TestClass]
  public sealed class TransformerTests
  {
    [TestMethod]
    public void AesBlock()
    {
      Test(new AESAndMACTransformer("ttest"));
    }

    [TestMethod]
    public void ChaBlock()
    {
      Test(new ChaChaAndPolyTransformer("ttest"));
    }

    [TestMethod]
    public void Composite()
    {
      Test(new CompositeTransformer(new NoneBlockTransformer(), new TestTransformer()));
      Test(new CompositeTransformer(new NoneBlockTransformer(), new LZ4CompressorTransformer(),
                                    new AESAndMACTransformer("comptest")));
    }

    [TestMethod]
    public void LZ4Block()
    {
      Test(new LZ4CompressorTransformer());
    }

    [TestMethod]
    public void NoneBlock()
    {
      Test(new NoneBlockTransformer());
    }

    void Test(IBlockTransformer transformer)
    {
      var buffer = Enumerable.Range(0, 128).Select(v => (byte)v).ToArray();
      var cmp = Enumerable.Range(0, 128).Select(v => (byte)v).ToArray();
      var trans = transformer.TransformBlock(buffer);
      if (!transformer.MayChangeSize) {
        Assert.AreEqual(trans.Length, cmp.Length);
      }

      Assert.IsTrue(buffer.AsSpan().SequenceEqual(cmp));
      var back = new byte[buffer.Length * 2];
      var backspan = back.AsSpan(0, transformer.UntransformBlock(trans, back));
      Assert.IsTrue(backspan.SequenceEqual(cmp));
    }

    internal sealed class TestTransformer : IBlockTransformer
    {
      public bool MayChangeSize => true;

      public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
      {
        var buf = new byte[block.Length + 1];
        buf[0] = 255;
        block.CopyTo(buf.AsSpan(1));
        return buf;
      }

      public int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block)
      {
        input.Slice(1).CopyTo(block);
        return input.Length - 1;
      }
    }
  }
}