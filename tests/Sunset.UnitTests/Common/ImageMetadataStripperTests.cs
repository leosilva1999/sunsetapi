using System.Text;
using Sunset.Application.Common;

namespace Sunset.UnitTests.Common;

public class ImageMetadataStripperTests
{
    private static readonly byte[] ScanData = [0x12, 0xFF, 0x00, 0x34, 0xFF, 0xD0, 0x56];

    private static byte[] Ascii(string s) => Encoding.ASCII.GetBytes(s);

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] Segment(byte marker, byte[] payload) =>
        Concat([0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)((payload.Length + 2) & 0xFF)], payload);

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    // EXIF payload (after the APP1 length): big-endian IFD0 with Orientation = <orientation> and a
    // blob that stands in for GPS data, which must never survive.
    private static byte[] ExifPayload(int orientation, bool littleEndian = false)
    {
        var tiff = littleEndian
            ? Concat(Ascii("II"), [0x2A, 0x00, 0x08, 0x00, 0x00, 0x00], [0x01, 0x00], [0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, (byte)orientation, 0x00, 0x00, 0x00], [0, 0, 0, 0])
            : Concat(Ascii("MM"), [0x00, 0x2A, 0x00, 0x00, 0x00, 0x08], [0x00, 0x01], [0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, (byte)orientation, 0x00, 0x00], [0, 0, 0, 0]);
        return Concat(Ascii("Exif\0\0"), tiff, Ascii("GPSMARKER-LAT-23.5-LON-46.6"));
    }

    private static byte[] Jfif() =>
        Segment(0xE0, Concat(Ascii("JFIF\0"), [0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]));

    private static byte[] Jpeg(byte[]? exif, bool withJunk = true)
    {
        var parts = new List<byte[]> { new byte[] { 0xFF, 0xD8 }, Jfif() };
        if (exif is not null) parts.Add(Segment(0xE1, exif));
        if (withJunk)
        {
            parts.Add(Segment(0xE1, Ascii("http://ns.adobe.com/xap/1.0/\0XMPMARKER")));
            parts.Add(Segment(0xED, Ascii("Photoshop 3.0\0IPTCMARKER")));
            parts.Add(Segment(0xFE, Ascii("COMMENTMARKER")));
        }
        parts.Add(Segment(0xDB, new byte[65]));
        parts.Add(Segment(0xC0, [8, 0, 1, 0, 1, 3, 1, 0x11, 0, 2, 0x11, 1, 3, 0x11, 1]));
        parts.Add(Segment(0xC4, new byte[17]));
        parts.Add(Segment(0xDA, [3, 1, 0, 2, 0x11, 3, 0x11, 0, 0x3F, 0]));
        parts.Add(ScanData);
        parts.Add([0xFF, 0xD9]);
        if (withJunk) parts.Add(Ascii("TRAILINGMARKER"));
        return Concat([.. parts]);
    }

    [Fact]
    public void Jpeg_RemovesGpsExifXmpIptcCommentAndTrailingData()
    {
        var input = Jpeg(ExifPayload(1));

        var output = ImageMetadataStripper.TryStrip(input, out var contentType);

        Assert.NotNull(output);
        Assert.Equal("image/jpeg", contentType);
        foreach (var marker in new[] { "GPSMARKER", "XMPMARKER", "IPTCMARKER", "COMMENTMARKER", "TRAILINGMARKER", "Exif" })
            Assert.False(Contains(output, Ascii(marker)), $"{marker} survived");
    }

    [Fact]
    public void Jpeg_KeepsPixelDataAndStructureUntouched()
    {
        var output = ImageMetadataStripper.TryStrip(Jpeg(ExifPayload(1)), out _)!;

        Assert.Equal([0xFF, 0xD8], output[..2]);
        Assert.True(Contains(output, Ascii("JFIF\0")));
        Assert.True(Contains(output, Concat(ScanData, [0xFF, 0xD9])));
        Assert.Equal([0xFF, 0xD9], output[^2..]);
        Assert.True(Contains(output, [0xFF, 0xDB])); // DQT
        Assert.True(Contains(output, [0xFF, 0xC0])); // SOF0
        Assert.True(Contains(output, [0xFF, 0xC4])); // DHT
    }

    [Theory]
    [InlineData(6, false)]
    [InlineData(3, false)]
    [InlineData(8, true)]
    public void Jpeg_KeepsOnlyTheOrientationFlag_SoPortraitPhotosStayUpright(int orientation, bool littleEndianSource)
    {
        var output = ImageMetadataStripper.TryStrip(Jpeg(ExifPayload(orientation, littleEndianSource)), out _)!;

        // A rewritten, minimal EXIF segment: Orientation tag (0x0112, SHORT, 1 value) and nothing else.
        var expectedTag = Concat([0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, (byte)orientation]);
        Assert.True(Contains(output, expectedTag));
        Assert.False(Contains(output, Ascii("GPSMARKER")));

        var exifAt = output.AsSpan().IndexOf(Ascii("Exif\0\0"));
        Assert.True(exifAt > 0);
        // JFIF stays the first segment; the minimal EXIF follows it
        Assert.True(output.AsSpan().IndexOf(Ascii("JFIF\0")) < exifAt);
        Assert.Equal(0x22, output[exifAt - 1]); // APP1 length: 34
    }

    [Fact]
    public void Jpeg_WithoutOrientationOrWithOrientation1_HasNoExifAtAll()
    {
        Assert.False(Contains(ImageMetadataStripper.TryStrip(Jpeg(null), out _)!, Ascii("Exif")));
        Assert.False(Contains(ImageMetadataStripper.TryStrip(Jpeg(ExifPayload(1)), out _)!, Ascii("Exif")));
    }

    [Fact]
    public void Jpeg_KeepsIccColourProfile_ButDropsOtherApp2Data()
    {
        var input = Concat(
            [0xFF, 0xD8], Jfif(),
            Segment(0xE2, Ascii("ICC_PROFILE\0ICCKEPT")),
            Segment(0xE2, Ascii("MPF\0MPFDROPPED")),
            Segment(0xDB, new byte[65]),
            Segment(0xC0, [8, 0, 1, 0, 1, 1, 1, 0x11, 0]),
            Segment(0xDA, [1, 1, 0, 0, 0x3F, 0]),
            ScanData, [0xFF, 0xD9]);

        var output = ImageMetadataStripper.TryStrip(input, out _)!;

        Assert.True(Contains(output, Ascii("ICCKEPT")));
        Assert.False(Contains(output, Ascii("MPFDROPPED")));
    }

    [Fact]
    public void Jpeg_AlreadyClean_IsReturnedUnchanged()
    {
        var clean = Jpeg(null, withJunk: false);

        var output = ImageMetadataStripper.TryStrip(clean, out _);

        Assert.Equal(clean, output);
    }

    [Fact]
    public void Jpeg_Truncated_OrWithoutImageData_IsRejected()
    {
        var valid = Jpeg(ExifPayload(1));

        Assert.Null(ImageMetadataStripper.TryStrip(valid[..(valid.Length / 2)], out _));
        // headers only, no frame/scan
        Assert.Null(ImageMetadataStripper.TryStrip(Concat([0xFF, 0xD8], Jfif(), [0xFF, 0xD9]), out _));
    }

    // ---------------------------------------------------------------- PNG

    private static byte[] Chunk(string type, byte[] data) =>
        Concat([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length], Ascii(type), data, [0xAA, 0xBB, 0xCC, 0xDD]);

    private static byte[] Png(bool withJunk = true)
    {
        var parts = new List<byte[]> { new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, Chunk("IHDR", new byte[13]), Chunk("gAMA", [0, 0, 0xB1, 0x8F]) };
        if (withJunk)
        {
            parts.Add(Chunk("tEXt", Ascii("Comment\0TEXTMARKER")));
            parts.Add(Chunk("eXIf", Ascii("EXIFMARKER-GPS")));
            parts.Add(Chunk("tIME", new byte[7]));
            parts.Add(Chunk("iTXt", Ascii("XML:com.adobe.xmp\0\0\0\0\0XMPMARKER")));
        }
        parts.Add(Chunk("IDAT", Ascii("PIXELDATA")));
        parts.Add(Chunk("IEND", []));
        if (withJunk) parts.Add(Ascii("TRAILINGMARKER"));
        return Concat([.. parts]);
    }

    [Fact]
    public void Png_RemovesTextExifTimeAndTrailingData_KeepsTheImage()
    {
        var output = ImageMetadataStripper.TryStrip(Png(), out var contentType);

        Assert.NotNull(output);
        Assert.Equal("image/png", contentType);
        foreach (var marker in new[] { "TEXTMARKER", "EXIFMARKER", "XMPMARKER", "TRAILINGMARKER", "tEXt", "eXIf", "tIME", "iTXt" })
            Assert.False(Contains(output, Ascii(marker)), $"{marker} survived");
        Assert.True(Contains(output, Ascii("PIXELDATA")));
        Assert.True(Contains(output, Ascii("gAMA")));
        Assert.True(Contains(output, Ascii("IHDR")));
        Assert.Equal(Chunk("IEND", []), output[^12..]);
    }

    [Fact]
    public void Png_AlreadyClean_IsReturnedUnchanged()
    {
        var clean = Png(withJunk: false);

        Assert.Equal(clean, ImageMetadataStripper.TryStrip(clean, out _));
    }

    [Fact]
    public void Png_WithoutIhdrFirst_WithoutIdat_OrTruncated_IsRejected()
    {
        var signature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.Null(ImageMetadataStripper.TryStrip(Concat(signature, Chunk("IDAT", [1]), Chunk("IEND", [])), out _));
        Assert.Null(ImageMetadataStripper.TryStrip(Concat(signature, Chunk("IHDR", new byte[13]), Chunk("IEND", [])), out _));
        var valid = Png();
        Assert.Null(ImageMetadataStripper.TryStrip(valid[..(valid.Length - 30)], out _));
    }

    // ---------------------------------------------------------------- other input

    [Theory]
    [InlineData("not an image at all")]
    [InlineData("GIF89a....")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>")]
    [InlineData("")]
    public void NonJpegNonPng_IsRejected(string content) =>
        Assert.Null(ImageMetadataStripper.TryStrip(Ascii(content), out _));
}
