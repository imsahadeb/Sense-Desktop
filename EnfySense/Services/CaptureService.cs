using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace EnfyLiveScreenClient.Services;

public sealed class CaptureService : IDisposable
{
    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private Bitmap? _sourceBitmap;
    private Bitmap? _outputBitmap;
    private Graphics? _sourceGraphics;
    private Graphics? _outputGraphics;

    public byte[] CaptureScreen(int targetWidth, int targetHeight, long jpegQuality)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Screen capture is only supported on Windows.");
        }

        int sourceWidth = Math.Max(GetSystemMetrics(0), 1);
        int sourceHeight = Math.Max(GetSystemMetrics(1), 1);
        int outputWidth = targetWidth > 0 ? targetWidth : sourceWidth;
        int outputHeight = targetHeight > 0 ? targetHeight : sourceHeight;

        // Ensure source buffer is ready
        if (_sourceBitmap == null || _sourceBitmap.Width != sourceWidth || _sourceBitmap.Height != sourceHeight)
        {
            AppLogger.Log($"Initializing source capture buffer: {sourceWidth}x{sourceHeight}", LogLevel.Debug);
            _sourceGraphics?.Dispose();
            _sourceBitmap?.Dispose();
            _sourceBitmap = new Bitmap(sourceWidth, sourceHeight, PixelFormat.Format24bppRgb);
            _sourceGraphics = Graphics.FromImage(_sourceBitmap);
        }

        // Ensure output buffer is ready
        if (_outputBitmap == null || _outputBitmap.Width != outputWidth || _outputBitmap.Height != outputHeight)
        {
            AppLogger.Log($"Initializing output resize buffer: {outputWidth}x{outputHeight}", LogLevel.Debug);
            _outputGraphics?.Dispose();
            _outputBitmap?.Dispose();
            _outputBitmap = new Bitmap(outputWidth, outputHeight, PixelFormat.Format24bppRgb);
            _outputGraphics = Graphics.FromImage(_outputBitmap);
            _outputGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
        }

        // Capture
        try
        {
            _sourceGraphics!.CopyFromScreen(0, 0, 0, 0, _sourceBitmap.Size);
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "GDI+ CopyFromScreen failed");
            throw;
        }

        // Resize
        if (sourceWidth == outputWidth && sourceHeight == outputHeight)
        {
            _outputGraphics!.DrawImage(_sourceBitmap, 0, 0);
        }
        else
        {
            _outputGraphics!.DrawImage(_sourceBitmap, 0, 0, outputWidth, outputHeight);
        }

        using var memoryStream = new MemoryStream();
        var jpegEncoder = GetJpegEncoder();
        if (jpegEncoder == null)
        {
            AppLogger.Log("No JPEG encoder found, using default Save format.", LogLevel.Warn);
            _outputBitmap.Save(memoryStream, ImageFormat.Jpeg);
            return memoryStream.ToArray();
        }

        using var encoderParameters = new EncoderParameters(1);
        encoderParameters.Param[0] = new EncoderParameter(
            Encoder.Quality,
            Math.Clamp(jpegQuality, 25L, 90L));
        
        try
        {
            _outputBitmap.Save(memoryStream, jpegEncoder, encoderParameters);
        }
        catch (Exception ex)
        {
            AppLogger.Log(ex, "JPEG Encoding/Save failed");
            throw;
        }
        
        return memoryStream.ToArray();
    }

    private static ImageCodecInfo? GetJpegEncoder()
    {
        foreach (var codec in ImageCodecInfo.GetImageEncoders())
        {
            if (codec.FormatID == ImageFormat.Jpeg.Guid)
            {
                return codec;
            }
        }

        AppLogger.Log("JPEG Encoder not found in system! Falling back to default Save method.", LogLevel.Error);
        return null;
    }

    public void Dispose()
    {
        _sourceGraphics?.Dispose();
        _sourceBitmap?.Dispose();
        _outputGraphics?.Dispose();
        _outputBitmap?.Dispose();
    }
}
