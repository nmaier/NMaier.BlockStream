using System;
using System.Text;
using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  [PublicAPI]
  public sealed class EncryptedCompressedTransformer : IBlockTransformer
  {
    private LZ4CompressorTransformer compressor;
    private ChaChaAndPolyTransformer crypter;

    public EncryptedCompressedTransformer(string key) : this(Encoding.UTF8.GetBytes(key))
    {
    }

    public EncryptedCompressedTransformer(byte[] key)
    {
      crypter = new ChaChaAndPolyTransformer(key);
      compressor = new LZ4CompressorTransformer();
    }

    public bool MayChangeSize => true;

    public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
    {
      return crypter.TransformBlock(compressor.TransformBlock(block));
    }

    public int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block)
    {
      var rv = crypter.UntransformBlock(input, block);
      return compressor.UntransformBlock(block.Slice(0, rv), block);
    }
  }
}