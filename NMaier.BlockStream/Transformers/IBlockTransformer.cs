using System;

using JetBrains.Annotations;

namespace NMaier.BlockStream.Transformers
{
  /// <summary>
  ///   Transforms data
  /// </summary>
  [PublicAPI]
  public interface IBlockTransformer
  {
    /// <summary>
    ///   This transformer may change produce a differently sized output than the input
    /// </summary>
    bool MayChangeSize { get; }

    /// <summary>
    ///   Transforms a single block
    /// </summary>
    /// <remarks>The output block may refer to the same location as the input block.</remarks>
    /// <param name="block">Input block</param>
    /// <returns>Output block</returns>
    ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block);

    /// <summary>
    ///   Un-transforms a single block, reversing the transformation for <see cref="TransformBlock" />
    /// </summary>
    /// <remarks>Input and output blocks may refer into the same memory buffer</remarks>
    /// <param name="input">Input block</param>
    /// <param name="block">Target block</param>
    /// <returns>Output length</returns>
    int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block);
  }
}
