using ControlFS.Core.Input;

namespace ControlFS.UnitTests.Input;

public class InputCadenceTests
{
    [Fact]
    public void Fast_reading_only_with_the_window_active_and_a_controller_connected()
    {
        Assert.Equal(new InputPoll(true, InputCadence.Active, false), InputCadence.Decide(true, controllerConnected: true, syncInputToDisplay: true, lightInBackground: true));
        Assert.Equal(new InputPoll(false, InputCadence.Active, false), InputCadence.Decide(true, controllerConnected: true, syncInputToDisplay: false, lightInBackground: true));
        // Sem controle: teclado e mouse chegam por eventos; a leitura lenta só nota a conexão de um (sem relógio de 1 ms).
        Assert.Equal(new InputPoll(false, InputCadence.NoController, false), InputCadence.Decide(true, controllerConnected: false, syncInputToDisplay: true, lightInBackground: true));
    }

    [Fact]
    public void Background_only_watches_connections_and_silences_the_devices()
    {
        Assert.Equal(new InputPoll(false, InputCadence.LightBackground, true), InputCadence.Decide(false, controllerConnected: true, syncInputToDisplay: true, lightInBackground: true));
        Assert.Equal(new InputPoll(false, InputCadence.Background, true), InputCadence.Decide(false, controllerConnected: true, syncInputToDisplay: true, lightInBackground: false));
    }
}
