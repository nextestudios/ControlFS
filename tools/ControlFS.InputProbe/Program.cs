using System.Diagnostics;
using ControlFS.Core.Actions;
using ControlFS.Core.Contracts;
using ControlFS.Core.Input;
using ControlFS.Infrastructure.Input.Sdl3;

// Uso: dotnet run -- [segundos] [--east-confirms]
var seconds = args.Length > 0 && int.TryParse(args[0], out var s) ? s : 10;
var convention = args.Contains("--east-confirms") ? ConfirmBackConvention.EastConfirms : ConfirmBackConvention.SouthConfirms;
var clock = Stopwatch.StartNew();
var router = new InputRouter(new ActionMap(convention), InputSettings.Default,
    action => Console.WriteLine($"[{clock.Elapsed.TotalSeconds,7:F3}s] ação: {action}"));
router.ActiveDeviceChanged += key => Console.WriteLine($"[{clock.Elapsed.TotalSeconds,7:F3}s] dispositivo ativo: {key ?? "(nenhum)"}");

using var backend = new Sdl3InputBackend(InputSettings.Default);
var sink = new ProbeSink(router);
if (!backend.Initialize(sink, out var error))
{
    Console.Error.WriteLine($"Falha ao inicializar SDL3: {error}");
    return 1;
}
Console.WriteLine($"Backend: {backend.BackendDescription}. Convenção: {convention}. Observando por {seconds}s (Ctrl+C encerra).");
var end = TimeSpan.FromSeconds(seconds);
while (clock.Elapsed < end)
{
    backend.Pump();
    router.Tick(clock.Elapsed);
    Thread.Sleep(8); // cadência ativa ~120 Hz; o app reduz a cadência em segundo plano
}
Console.WriteLine($"Dispositivos no fim: {backend.Devices.Count}");
foreach (var d in backend.Devices) Console.WriteLine($"  {d.SessionKey} {d.Name} [{d.TypeName}] família={d.Family} vid={d.VendorId:X4} pid={d.ProductId:X4} gamepad={d.IsGamepad} virtual={d.IsVirtual} guid={d.StableId}");
return 0;

sealed class ProbeSink(InputRouter router) : IInputSink
{
    public void OnControl(string deviceKey, PhysicalControl control, bool pressed, TimeSpan timestamp)
    {
        Console.WriteLine($"  bruto: {deviceKey} {control} {(pressed ? "↓" : "↑")}");
        router.OnControl(deviceKey, control, pressed, timestamp);
    }

    public void OnDeviceAdded(InputDeviceInfo device) =>
        Console.WriteLine($"+ conectado: {device.Name} ({device.SessionKey}, {device.TypeName}, família={device.Family}, gamepad={device.IsGamepad})");

    public void OnDeviceRemoved(string deviceKey)
    {
        Console.WriteLine($"- desconectado: {deviceKey}");
        router.OnDeviceRemoved(deviceKey);
    }
}
