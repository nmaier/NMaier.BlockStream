using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  [PublicAPI]
  public sealed class CompositeTransformer : IBlockTransformer
  {
    private readonly IBlockTransformer[] transformers;

    public CompositeTransformer(params IBlockTransformer[] transformers)
    {
      this.transformers = transformers;
      MayChangeSize = transformers.Any(e => e.MayChangeSize);
    }

    public bool MayChangeSize { get; }

    [SuppressMessage("ReSharper", "LoopCanBeConvertedToQuery")]
    public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
    {
      foreach (var t in transformers) {
        block = t.TransformBlock(block);
      }

      return block;
    }

    public int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block)
    {
      int rv = input.Length;
      foreach (var t in transformers.Reverse()) {
        rv = t.UntransformBlock(input.Slice(0, rv), block);
        input = block.Slice(0, rv);
      }

      return rv;
    }

    public Span<byte> UntransformBlock(ReadOnlySpan<byte> input)
    {
      throw new NotImplementedException();
    }
  }
}