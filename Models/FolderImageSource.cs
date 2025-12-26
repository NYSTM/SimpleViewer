using SimpleViewer.Utils;
using System.IO;
using System.Windows.Media.Imaging;

namespace SimpleViewer.Models;

public class FolderImageSource : ImageSourceBase, IImageSource
{
    private readonly List<string> _filePaths;

    public FolderImageSource(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            _filePaths = new List<string>();
            return;
        }
        _filePaths = Directory.GetFiles(folderPath)
            .Where(path => IsImageFile(path))
            .OrderBy(path => path, new NaturalStringComparer())
            .ToList();
    }

    public Task<int> GetPageCountAsync() => Task.FromResult(_filePaths.Count);

    public async Task<BitmapSource?> GetPageImageAsync(int index)
    {
        if (index < 0 || index >= _filePaths.Count) return null;
        return await Task.Run(() => {
            try { return SkiaImageLoader.LoadImage(File.ReadAllBytes(_filePaths[index])); }
            catch { return null; }
        });
    }

    public async Task<BitmapSource?> GetThumbnailAsync(int index, int width)
    {
        if (index < 0 || index >= _filePaths.Count) return null;
        return await Task.Run(() => {
            try { return SkiaImageLoader.LoadThumbnail(File.ReadAllBytes(_filePaths[index]), width); }
            catch { return null; }
        });
    }

    public void Dispose() { }
}