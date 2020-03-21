using System.Diagnostics;
using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  [PublicAPI]
  [DebuggerDisplay("Ext: {Offset} {Length}")]
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
}