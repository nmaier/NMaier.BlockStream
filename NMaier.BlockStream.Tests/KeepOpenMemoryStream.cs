using System.IO;

#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable CA2215
#pragma warning restore IDE0079 // Remove unnecessary suppression

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
