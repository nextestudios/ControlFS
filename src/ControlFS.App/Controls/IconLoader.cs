using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.WindowsRuntime;
using ControlFS.Core.Contracts;
using ControlFS.Core.Icons;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace ControlFS.App.Controls;

/// <summary>
/// Carrega ícones do sistema para as linhas da lista sem travar a UI: o símbolo aparece na hora e a imagem entra quando
/// chega. Pedidos iguais (mesma extensão) viram um só; linhas recicladas cancelam o pedido. Cache limitado, por tipo e
/// tamanho em pixels; trocar a escala (DPI) esvazia o cache. Usar somente na thread de UI.
/// </summary>
public sealed class IconLoader(IIconProvider provider)
{
    /// <summary>Tamanho do ícone na linha, em pixels independentes de dispositivo.</summary>
    public const double IconSize = 32;

    private readonly LruCache<string, ImageSource?> _cache = new(512);
    private readonly Dictionary<string, Flight> _inflight = [];
    private readonly ConditionalWeakTable<Image, CancellationTokenSource> _pending = [];

    private sealed class Flight(Task<IconImage?> task, CancellationTokenSource cancellation)
    {
        public Task<IconImage?> Task { get; } = task;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public int Waiters { get; set; }
    }

    public int SizePx { get; private set; } = (int)IconSize;

    /// <summary>A escala mudou (outro monitor, outro DPI): os ícones em cache ficaram do tamanho errado.</summary>
    public event Action? Invalidated;

    public void SetScale(double scale)
    {
        var px = Math.Max(16, (int)Math.Round(IconSize * scale));
        if (px == SizePx) return;
        SizePx = px;
        _cache.Clear();
        _inflight.Clear();
        Invalidated?.Invoke();
    }

    /// <summary>Mostra o símbolo de reserva e, quando houver, troca pelo ícone do sistema.</summary>
    public void Load(Image image, UIElement fallback, IconRequest? request)
    {
        Cancel(image);
        if (request is null)
        {
            Show(image, fallback, null, null);
            return;
        }
        var key = request.Key + "@" + SizePx;
        image.Tag = key;
        if (_cache.TryGet(key, out var cached))
        {
            Show(image, fallback, cached, key);
            return;
        }
        Show(image, fallback, null, key);
        var cts = new CancellationTokenSource();
        _pending.AddOrUpdate(image, cts);
        _ = LoadAsync(image, fallback, request, key, cts.Token);
    }

    /// <summary>Linha reciclada pela virtualização: o pedido dela não interessa mais.</summary>
    public void Cancel(Image image)
    {
        if (_pending.TryGetValue(image, out var previous))
        {
            previous.Cancel();
            previous.Dispose();
            _pending.Remove(image);
        }
        image.Tag = null;
    }

    private async Task LoadAsync(Image image, UIElement fallback, IconRequest request, string key, CancellationToken cancellationToken)
    {
        if (!_inflight.TryGetValue(key, out var flight))
        {
            var cts = new CancellationTokenSource();
            flight = new Flight(provider.GetIconAsync(request, SizePx, cts.Token), cts);
            _inflight[key] = flight;
        }
        flight.Waiters++;
        IconImage? pixels;
        try
        {
            pixels = await flight.Task.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ninguém mais espera este ícone: o provedor pode pular o trabalho.
            if (--flight.Waiters == 0 && _inflight.TryGetValue(key, out var current) && current == flight)
            {
                _inflight.Remove(key);
                flight.Cancellation.Cancel();
            }
            return;
        }
        flight.Waiters--;
        if (_inflight.TryGetValue(key, out var same) && same == flight) _inflight.Remove(key);
        if (!_cache.TryGet(key, out var source))
        {
            source = pixels is null ? null : ToBitmap(pixels);
            if (key.EndsWith("@" + SizePx, StringComparison.Ordinal)) _cache.Set(key, source); // não guarda tamanho antigo
        }
        if (Equals(image.Tag, key)) Show(image, fallback, source, key);
    }

    private static void Show(Image image, UIElement fallback, ImageSource? source, string? key)
    {
        image.Source = source;
        image.Visibility = source is null ? Visibility.Collapsed : Visibility.Visible;
        fallback.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
        image.Tag = key;
    }

    private static WriteableBitmap ToBitmap(IconImage icon)
    {
        var bitmap = new WriteableBitmap(icon.Width, icon.Height);
        using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(icon.Pixels.Span);
        bitmap.Invalidate();
        return bitmap;
    }
}
