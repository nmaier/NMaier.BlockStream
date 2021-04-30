using System.Diagnostics;

using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  [PublicAPI]
  [DebuggerDisplay("Ext: {Offset} {Length}")]
#if NETFRAMEWORK || NETSTANDARD
  public readonly struct Extent
  {
    public readonly long Offset;
    public readonly short Length;

    internal Extent(long offset, short length)
    {
      Offset = offset;
      Length = length;
    }
  }
#else
  public sealed record Extent(long Offset, short Length);
#endif
}
