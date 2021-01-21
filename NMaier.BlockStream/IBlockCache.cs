using System;

using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  [PublicAPI]
  public interface IBlockCache : IDisposable
  {
    void Cache(Span<byte> block, long offset);
    void Invalidate(long offset);
    bool TryReadBlock(Span<byte> block, long offset);
  }
}
