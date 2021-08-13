using System;

using JetBrains.Annotations;

namespace NMaier.BlockStream.Transformers
{
  /// <summary>
  ///   Transforms data, but not really
  /// </summary>
  [PublicAPI]
  public sealed class NoneBlockTransformer : IBlockTransformer
  {
    /// <inheritdoc />
    public bool MayChangeSize => false;

    /// <inheritdoc />
    public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
    {
      return block;
    }

    /// <inheritdoc />
    public int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block)
    {
      if (input.Overlaps(block, out var offset) && offset == 0) {
        return input.Length;
      }

      input.CopyTo(block);
      return input.Length;
    }
  }
}
