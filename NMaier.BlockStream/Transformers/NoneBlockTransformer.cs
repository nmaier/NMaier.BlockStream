using System;
using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  [PublicAPI]
  public sealed class NoneBlockTransformer : IBlockTransformer
  {
    public bool MayChangeSize => false;

    public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
    {
      return block;
    }

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