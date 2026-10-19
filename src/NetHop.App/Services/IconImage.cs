using Microsoft.Windows.Storage.Pickers;
using NetHop.Core.Profiles;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace NetHop.App.Services;

/// <summary>
/// Custom profile icons: an image the user picks, scaled to fit 64×64 and stored in the profile as a PNG data URI.
/// </summary>
public static class IconImage
{
    private const int Size = 64;

    private static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".tif", ".tiff"];

    /// <summary>Lets the user pick an image. Null when cancelled or when the image cannot be read (a notification says why).</summary>
    public static async Task<string?> PickAsync()
    {
        // The Windows App SDK picker also works when NetHop runs as administrator, unlike Windows.Storage.Pickers.
        var picker = new FileOpenPicker(App.Current!.MainWindowForDialogs.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
        };
        foreach (var extension in Extensions)
            picker.FileTypeFilter.Add(extension);

        var file = await picker.PickSingleFileAsync();
        if (file is null)
            return null;

        try
        {
            return await ToDataUriAsync(file.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Runtime.InteropServices.COMException)
        {
            UserActionRunner.Notify(Loc.Get("Icon_ImageError"), ex.Message);
            return null;
        }
    }

    private static async Task<string> ToDataUriAsync(string path)
    {
        using var file = File.OpenRead(path);
        var decoder = await BitmapDecoder.CreateAsync(file.AsRandomAccessStream());

        var scale = Math.Min(1.0, (double)Size / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale)),
            ScaledHeight = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale)),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        using var pixels = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);

        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetSoftwareBitmap(pixels);
        await encoder.FlushAsync();

        var bytes = new byte[output.Size];
        using (var stream = output.GetInputStreamAt(0).AsStreamForRead())
            await stream.ReadExactlyAsync(bytes);

        if (bytes.Length > Profile.MaxImageIconBytes)
            throw new InvalidDataException(Loc.Get("Icon_ImageTooLarge"));
        return Profile.ImageIconPrefix + Convert.ToBase64String(bytes);
    }
}
