using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

using JetBrains.Annotations;

namespace NMaier.BlockStream
{
  /// <summary>
  ///   Transforms data using AES-CTR and verifies data using a HMAC-SHA256.
  ///   <remarks>This transformer is slow</remarks>
  /// </summary>
  [PublicAPI]
  public sealed class AESAndMACTransformer : IBlockTransformer
  {
    private readonly byte[] cryptoKey;
    private readonly byte[] macKey;

    /// <summary>
    ///   Create a new transformer given the specified key.
    /// </summary>
    /// <remarks>The key is not used verbatim, but used as input into a KDF deriving the actual keys for AES and MAC</remarks>
    /// <param name="key">Key to use</param>
    public AESAndMACTransformer(string key) : this(Encoding.UTF8.GetBytes(key))
    {
    }

    /// <summary>
    ///   Create a new transformer given the specified key.
    /// </summary>
    /// <remarks>The key is not used verbatim, but used as input into a KDF deriving the actual keys for AES and MAC</remarks>
    /// <param name="key">Key to use</param>
    public AESAndMACTransformer(byte[] key)
    {
      cryptoKey = key.DeriveKeyBytesReasonablySafeNotForStorage(24);
      macKey = key.Concat(cryptoKey).ToArray()
        .DeriveKeyBytesReasonablySafeNotForStorage(64);
    }

    /// <inheritdoc />
    public bool MayChangeSize => true;

    /// <inheritdoc />
    public ReadOnlySpan<byte> TransformBlock(ReadOnlySpan<byte> block)
    {
      using var hmac = new HMACSHA256(macKey);
      var rv = new byte[block.Length + 32];
      var inputBuffer = block.ToArray();
      using var aes = new AesCounterMode();
      using var enc = aes.CreateEncryptor(cryptoKey, Array.Empty<byte>());
      var encrypted = enc.TransformFinalBlock(inputBuffer, 0, inputBuffer.Length);
      var hash = hmac.ComputeHash(encrypted);
      hash.CopyTo(rv.AsSpan());
      encrypted.CopyTo(rv.AsSpan(hash.Length));
      return rv;
    }

    /// <inheritdoc />
    public int UntransformBlock(ReadOnlySpan<byte> input, Span<byte> block)
    {
      using var hmac = new HMACSHA256(macKey);
      var hash = hmac.ComputeHash(input.Slice(32).ToArray());
      if (!input.Slice(0, 32).SequenceEqual(hash)) {
        ThrowHelpers.ThrowInvalidMAC();
      }

      using var aes = new AesCounterMode();
      using var enc = aes.CreateDecryptor(cryptoKey, Array.Empty<byte>());
      var dec = enc.TransformFinalBlock(input.Slice(32).ToArray(), 0, input.Length - 32);
      dec.CopyTo(block);
      return dec.Length;
    }

    private sealed class AesCounterMode : SymmetricAlgorithm
    {
      private readonly AesManaged aes;
      private readonly byte[] counter;

      public AesCounterMode(byte[]? counter = null)
      {
        this.counter = counter ?? new byte[16];
        aes = new AesManaged {
          BlockSize = 128,
          Mode = CipherMode.ECB,
          Padding = PaddingMode.None,
          KeySize = 192
        };
      }

      public override ICryptoTransform CreateDecryptor(byte[] rgbKey, byte[]? _)
      {
        return new CounterModeCryptoTransform(aes, rgbKey, counter);
      }

      public override ICryptoTransform CreateEncryptor(byte[] rgbKey, byte[]? _)
      {
        return new CounterModeCryptoTransform(aes, rgbKey, counter);
      }

      public override void GenerateIV()
      {
        // IV not needed in Counter Mode
      }

      public override void GenerateKey()
      {
        aes.GenerateKey();
      }
    }

    private sealed class CounterModeCryptoTransform : ICryptoTransform
    {
      private static readonly byte[] rgbIV = new byte[16];
      private readonly byte[] counter;
      private readonly ICryptoTransform counterEncryptor;
      private readonly byte[] maskBuffer;
      private int maskAvail;

      public CounterModeCryptoTransform(SymmetricAlgorithm symmetricAlgorithm, byte[] key,
        byte[] counter)
      {
        this.counter = counter;
        counterEncryptor = symmetricAlgorithm.CreateEncryptor(key, rgbIV);
        InputBlockSize = symmetricAlgorithm.BlockSize / 8;
        maskBuffer = new byte[InputBlockSize];
        maskAvail = 0;
      }

      public bool CanReuseTransform => false;
      public bool CanTransformMultipleBlocks => true;

      public int InputBlockSize { get; }
      public int OutputBlockSize => InputBlockSize;

      public int TransformBlock(byte[] inputBuffer, int inputOffset, int inputCount,
        byte[] outputBuffer, int outputOffset)
      {
        for (var i = 0; i < inputCount; i++) {
          if (maskAvail == 0) {
            EncryptCounterThenIncrement();
          }

          var maskByte = maskBuffer[maskBuffer.Length - maskAvail--];
          outputBuffer[outputOffset + i] =
            (byte)(inputBuffer[inputOffset + i] ^ maskByte);
        }

        return inputCount;
      }

      public byte[] TransformFinalBlock(byte[] inputBuffer, int inputOffset,
        int inputCount)
      {
        var output = new byte[inputCount];
        if (TransformBlock(inputBuffer, inputOffset, inputCount, output, 0) !=
            inputCount) {
          ThrowHelpers.ThrowCorruptData();
        }

        return output;
      }

      public void Dispose()
      {
      }

      private void EncryptCounterThenIncrement()
      {
        _ = counterEncryptor.TransformBlock(counter, 0, counter.Length, maskBuffer, 0);
        for (var i = counter.Length - 1; i >= 0; i--) {
          if (++counter[i] != 0) {
            break;
          }
        }

        maskAvail = maskBuffer.Length;
      }
    }
  }
}
