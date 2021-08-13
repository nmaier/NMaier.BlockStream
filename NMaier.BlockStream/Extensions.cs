using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

using JetBrains.Annotations;

using NMaier.BlockStream.Internal;
#if NETFRAMEWORK
using System.Buffers;

#endif

namespace NMaier.BlockStream
{
  [PublicAPI]
  public static class Extensions
  {
    /// <summary>
    ///   Read the full block from a stream, or throw an IOException if not enough data to fill the block is available
    /// </summary>
    /// <exception cref="EndOfStreamException">Stream does not contain enough data to fulfill the request.</exception>
    /// <exception cref="ArgumentException">Requested length larger than buffer</exception>
    /// <exception cref="IOException">Any exceptions the stream may raise</exception>
    /// <param name="stream">Stream to read from</param>
    /// <param name="buffer">Target buffer</param>
    /// <param name="length">Length to read. If this value is less than 0, then the length of the buffer will be used</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReadFullBlock(this Stream stream, byte[] buffer, int length = -1)
    {
      ReadFullBlock(
        stream,
        buffer.AsSpan(0, length >= 0 ? length : buffer.Length),
        length);
    }

    /// <summary>
    ///   Read the full block from a stream, or throw an IOException if not enough data to fill the block is available
    /// </summary>
    /// <exception cref="EndOfStreamException">Stream does not contain enough data to fulfill the request.</exception>
    /// <exception cref="ArgumentException">Requested length larger than buffer</exception>
    /// <exception cref="IOException">Any exceptions the stream may raise</exception>
    /// <param name="stream">Stream to read from</param>
    /// <param name="buffer">Target buffer</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReadFullBlock(this Stream stream, ArraySegment<byte> buffer)
    {
      ReadFullBlock(
        stream,
        (buffer.Array ?? throw new ArgumentException("Invalid segment", nameof(buffer)))
        .AsSpan(buffer.Offset, buffer.Count),
        buffer.Count);
    }


    /// <summary>
    ///   Read the full block from a stream, or throw an IOException if not enough data to fill the block is available
    /// </summary>
    /// <exception cref="EndOfStreamException">Stream does not contain enough data to fulfill the request.</exception>
    /// <exception cref="ArgumentException">Requested length larger than buffer</exception>
    /// <exception cref="IOException">Any exceptions the stream may raise</exception>
    /// <param name="stream">Stream to read from</param>
    /// <param name="buffer">Target buffer</param>
    /// <param name="offset">Offset in the target buffer</param>
    /// <param name="length">Length to read.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ReadFullBlock(this Stream stream, byte[] buffer, int offset,
      int length)
    {
#if NETFRAMEWORK
      while (length > 0) {
        var read = stream.Read(buffer, offset, length);
        if (read == length) {
          break;
        }

        if (read == 0) {
          ThrowHelpers.ThrowTruncatedRead();
        }

        length -= read;
        offset += read;
      }
#else
      ReadFullBlock(stream, buffer.AsSpan(offset, length), length);
#endif
    }

    /// <summary>
    ///   Reads a full block into the target.
    /// </summary>
    /// <exception cref="EndOfStreamException">Stream does not contain enough data to fulfill the request.</exception>
    /// <exception cref="ArgumentException">Requested length larger than buffer</exception>
    /// <exception cref="IOException">Any exceptions the stream may raise</exception>
    /// <param name="stream">Stream to read from</param>
    /// <param name="buffer">Target buffer</param>
    /// <param name="length">Optional length; defaults to full length of the buffer</param>
#if NETFRAMEWORK
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
#endif
    public static void ReadFullBlock(this Stream stream, Span<byte> buffer,
      int length = -1)
    {
      length = length <= 0 ? buffer.Length : length;
      var remaining = length;
      if (remaining > buffer.Length) {
        ThrowHelpers.ThrowArgumentException("Insufficient buffer", nameof(buffer));
      }

#if NETFRAMEWORK
      var temp = ArrayPool<byte>.Shared.Rent(length);
      try {
        ReadFullBlock(stream, temp, 0, length);
        temp.AsSpan(0, length).CopyTo(buffer);
      }
      finally {
        ArrayPool<byte>.Shared.Return(temp);
      }
#else
      while (remaining > 0) {
        var read = stream.Read(buffer);
        if (read == remaining) {
          return;
        }

        if (read == 0) {
          ThrowHelpers.ThrowTruncatedRead();
        }

        remaining -= read;
        buffer = buffer.Slice(read);
      }
#endif
    }

    [SuppressMessage(
      "Security",
      "CA5379:Ensure Key Derivation Function algorithm is sufficiently strong",
      Justification = "<Pending>")]
    internal static byte[] DeriveKeyBytesReasonablySafeNotForStorage(
      this byte[] passphrase, int length)
    {
      // This is essentially OK. We never store the pass phrase or the derived key ourselves.
      // This will still allow an attacker to brute-force pass phrases fast-ish, if that's what he's after.
      using var der = new Rfc2898DeriveBytes(
        passphrase,
        new byte[] {
          0xf9,
          0x03,
          0x02,
          0xea,
          0x42,
          0x23,
          0xab,
          0xff
        },
        100);
      return der.GetBytes(length);
    }

#if NETFRAMEWORK
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Write(this Stream stream, ReadOnlySpan<byte> buffer)
    {
      stream.Write(buffer.ToArray(), 0, buffer.Length);
    }
#endif
  }
}
