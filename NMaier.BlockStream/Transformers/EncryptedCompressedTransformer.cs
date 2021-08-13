using System;
using System.Text;

using JetBrains.Annotations;

namespace NMaier.BlockStream.Transformers
{
  /// <summary>
  ///   Encrypts (using <see cref="ChaChaAndPolyTransformer" /> and compresses (using <see cref="LZ4CompressorTransformer" />
  ///   ) data
  /// </summary>
  [PublicAPI]
  public sealed class EncryptedCompressedTransformer : IBlockTransformer
  {
    private readonly LZ4CompressorTransformer compressor;
    private readonly ChaChaAndPolyTransformer crypter;

    /// <summary>
    ///   Create a new transformer given the specified key.
    /// </summary>
    /// <remarks>
    ///   The key is not used verbatim, but used as input into a KDF deriving the actual keys for ChaCha20 and Poly1305
    ///   MAC
    /// </remarks>
    /// <param name="key">Key to use</param>
    public EncryptedCompressedTransformer(string key) : this(Encoding.UTF8.GetBytes(key))
    {
    }

    /// <summary>
    ///   Create a new transformer given the specified key.
    /// </summary>
    /// <remarks>
    ///   The key is not used verbatim, but used as input into a KDF deriving the actual keys for ChaCha20 and Poly1305
    ///   MAC
    /// </remarks>
    /// <param name="key">Key to use</param>
    public EncryptedCompressedTransformer(byte[] key)
    {
      crypter = new ChaChaAndPolyTransformer(key);
      compressor = new LZ4CompressorTransformer();
    }

    /// <inheritdoc />
    public bool MayChangeSize => true;

    /// <inheritdoc />
    public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
    {
      return crypter.TransformBlock(compressor.TransformBlock(block));
    }

    /// <inheritdoc />
    public int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block)
    {
      var rv = crypter.UntransformBlock(input, block);
      return compressor.UntransformBlock(block.Slice(0, rv), block);
    }
  }
}
