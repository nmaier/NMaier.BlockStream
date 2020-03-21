using System;
using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  [PublicAPI]
  public interface IBlockTransformer
  {
    bool MayChangeSize { get; }
    ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block);
    int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block);
  }
}