using System.Text;
using Moq;
using Sunset.Application.Common;
using Sunset.Application.Exceptions;
using Sunset.Domain.Entities;

namespace Sunset.UnitTests.Common;

public class ImageDimensionReaderTests
{
    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    private static byte[] Segment(byte marker, byte[] payload) =>
        Concat([0xFF, marker, (byte)((payload.Length + 2) >> 8), (byte)((payload.Length + 2) & 0xFF)], payload);

    private static byte[] Exif(int orientation) =>
        Concat(Encoding.ASCII.GetBytes("Exif\0\0"), Encoding.ASCII.GetBytes("MM"), [0x00, 0x2A, 0x00, 0x00, 0x00, 0x08], [0x00, 0x01],
            [0x01, 0x12, 0x00, 0x03, 0x00, 0x00, 0x00, 0x01, 0x00, (byte)orientation, 0x00, 0x00], [0, 0, 0, 0]);

    private static byte[] Jpeg(int width, int height, int? orientation = null)
    {
        var parts = new List<byte[]> { new byte[] { 0xFF, 0xD8 } };
        if (orientation is not null) parts.Add(Segment(0xE1, Exif(orientation.Value)));
        parts.Add(Segment(0xDB, new byte[65]));
        parts.Add(Segment(0xC0, [8, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 3, 1, 0x11, 0, 2, 0x11, 1, 3, 0x11, 1]));
        parts.Add(Segment(0xDA, [3, 1, 0, 2, 0x11, 3, 0x11, 0, 0x3F, 0]));
        parts.Add([0x12, 0x34]);
        parts.Add([0xFF, 0xD9]);
        return Concat([.. parts]);
    }

    private static byte[] Chunk(string type, byte[] data) =>
        Concat([(byte)(data.Length >> 24), (byte)(data.Length >> 16), (byte)(data.Length >> 8), (byte)data.Length], Encoding.ASCII.GetBytes(type), data, [0xAA, 0xBB, 0xCC, 0xDD]);

    private static byte[] Png(uint width, uint height) =>
        Concat([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A],
            Chunk("IHDR", [(byte)(width >> 24), (byte)(width >> 16), (byte)(width >> 8), (byte)width, (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height, 8, 2, 0, 0, 0]),
            Chunk("IDAT", Encoding.ASCII.GetBytes("PIXELDATA")), Chunk("IEND", []));

    [Fact]
    public void Jpeg_ReadsWidthAndHeightFromTheFrameHeader() =>
        Assert.Equal(new ImageDimensions(4032, 3024), ImageDimensionReader.TryRead(Jpeg(4032, 3024)));

    [Fact]
    public void Png_ReadsWidthAndHeightFromIhdr() =>
        Assert.Equal(new ImageDimensions(1920, 1080), ImageDimensionReader.TryRead(Png(1920, 1080)));

    [Theory]
    [InlineData(1, 4032, 3024)]
    [InlineData(3, 4032, 3024)]
    [InlineData(6, 3024, 4032)]   // portrait shot stored sideways: shown as 3024 x 4032
    [InlineData(8, 3024, 4032)]
    public void Jpeg_SwapsTheSizeWhenTheOrientationRotatesTheImage(int orientation, int expectedWidth, int expectedHeight) =>
        Assert.Equal(new ImageDimensions(expectedWidth, expectedHeight), ImageDimensionReader.TryRead(Jpeg(4032, 3024, orientation)));

    [Fact]
    public void ItWorksOnWhatTheStripperLeavesBehind()
    {
        var clean = ImageMetadataStripper.TryStrip(Jpeg(4032, 3024, 6), out _)!;

        Assert.Equal(new ImageDimensions(3024, 4032), ImageDimensionReader.TryRead(clean));
    }

    [Fact]
    public void ZeroSizesAndGarbageAreRejected()
    {
        Assert.Null(ImageDimensionReader.TryRead(Png(0, 100)));
        Assert.Null(ImageDimensionReader.TryRead(Jpeg(0, 10)));
        Assert.Null(ImageDimensionReader.TryRead([1, 2, 3, 4, 5]));
        Assert.Null(ImageDimensionReader.TryRead([0xFF, 0xD8, 0xFF, 0xD9]));
    }
}

public class PhotoDimensionsTests
{
    [Fact]
    public void ThePhotoKeepsTheSize_OrNoneAtAll()
    {
        var sized = new Photo(Guid.NewGuid(), Guid.NewGuid(), "https://x/a.jpg", null, 1200, 800);
        var unsized = new Photo(Guid.NewGuid(), Guid.NewGuid(), "https://x/b.jpg", null);

        Assert.Equal((1200, 800), (sized.Width, sized.Height));
        Assert.Null(unsized.Width);
        Assert.Null(unsized.Height);
    }

    [Theory]
    [InlineData(1200, null)]
    [InlineData(null, 800)]
    [InlineData(0, 800)]
    [InlineData(-5, -5)]
    public void ASizeThatIsHalfGivenOrNotPositiveIsRefused(int? width, int? height) =>
        Assert.Throws<ArgumentException>(() => new Photo(Guid.NewGuid(), Guid.NewGuid(), "https://x/a.jpg", null, width, height));
}
