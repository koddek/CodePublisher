using QRCoder;

namespace CodePublisher.Features.Deployment.Android;

/// <summary>
/// Renders a pairing payload as a PNG QR code. `PngByteQRCode` is pure managed (no System.Drawing, no
/// native codec), so the desktop and browser builds take the same path; the caller turns the bytes into
/// a bitmap. Kept apart from <see cref="AndroidQrPairing"/> so the protocol logic stays free of the
/// encoder dependency.
/// </summary>
public static class AndroidQrPairingImage
{
    public const int DefaultPixelsPerModule = 8;

    public static byte[] CreatePng(string payload, int pixelsPerModule = DefaultPixelsPerModule)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("A pairing payload is required to render a QR code.", nameof(payload));
        }

        if (pixelsPerModule < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerModule), "A QR module is at least one pixel.");
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule);
    }
}
