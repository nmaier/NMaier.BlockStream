using System.IO;

namespace NMaier.BlockStream.Tests
{
  internal sealed class KeepOpenMemoryStream : MemoryStream
  {
    public override void Close()
    {
    }

    protected override void Dispose(bool disposing)
    {
    }
  }
}