using System;
using System.Linq;

using JetBrains.Annotations;

namespace NMaier.BlockStream.Transformers;

/// <summary>
///   Transforms a data using a composite of different transformers
/// </summary>
[PublicAPI]
public sealed class CompositeTransformer : IBlockTransformer
{
  private readonly IBlockTransformer[] transformers;

  /// <summary>
  ///   Create a new composite transformer out of given transformers.
  /// </summary>
  /// <remarks>The array of transformers will be used in the order when transforming, and reverse than when un-transforming</remarks>
  /// <param name="transformers">Transformers to composite</param>
  public CompositeTransformer(params IBlockTransformer[] transformers)
  {
    this.transformers = transformers;
    MayChangeSize = transformers.Any(e => e.MayChangeSize);
  }

  /// <inheritdoc />
  public bool MayChangeSize { get; }

  /// <inheritdoc />
  public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
  {
    // ReSharper disable once LoopCanBeConvertedToQuery
    foreach (var t in transformers) {
      block = t.TransformBlock(block);
    }

    return block;
  }

  /// <inheritdoc />
  public int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block)
  {
    var rv = input.Length;
    foreach (var t in transformers.Reverse()) {
      rv = t.UntransformBlock(input.Slice(0, rv), block);
      input = block.Slice(0, rv);
    }

    return rv;
  }
}
