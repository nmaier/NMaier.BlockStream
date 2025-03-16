using System;

using JetBrains.Annotations;

using K4os.Compression.LZ4;

using NMaier.BlockStream.Internal;

namespace NMaier.BlockStream.Transformers;

/// <summary>
///   Transforms data, compressing it with the fast LZ4 compression algorithms
/// </summary>
[PublicAPI]
public sealed class LZ4CompressorTransformer : IBlockTransformer
{
  /// <inheritdoc />
  public bool MayChangeSize => true;

  /// <inheritdoc />
  public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
  {
    var target = new byte[LZ4Codec.MaximumOutputSize(block.Length)];
    var len = LZ4Codec.Encode(block, target);
    return target.AsSpan(0, len);
  }

  /// <inheritdoc />
  public int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block)
  {
    if (!input.Overlaps(block)) {
      var read = LZ4Codec.Decode(input, block);
      if (read < 0) {
        ThrowHelpers.ThrowInvalidLZ4Data();
      }

      return read;
    }

    if (input.Length < block.Length) {
      input = input.ToArray();
      var read = LZ4Codec.Decode(input, block);
      if (read < 0) {
        ThrowHelpers.ThrowInvalidLZ4Data();
      }

      return read;
    }

    var block2 = new byte[block.Length];
    var rv = LZ4Codec.Decode(input, block2);
    if (rv < 0) {
      ThrowHelpers.ThrowInvalidLZ4Data();
    }

    block2.AsSpan(0, rv).CopyTo(block);
    return rv;
  }
}