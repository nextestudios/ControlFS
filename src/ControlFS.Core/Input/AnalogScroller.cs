using ControlFS.Core.Actions;

namespace ControlFS.Core.Input;

/// <summary>
/// Converte o analógico direito (x, y em -1..1; y negativo = para cima, convenção SDL) em passos de rolagem
/// (<see cref="InputAction.ScrollUp"/>…): zona morta com histerese (drift nunca rola), um passo logo ao inclinar (toque
/// curto rola pouco), taxa proporcional à inclinação (curva quadrática: precisão perto do centro) e aceleração progressiva
/// enquanto a inclinação é mantida. Voltar ao centro para na hora e descarta o que estava acumulado. Um eixo por vez: o
/// outro só assume quando é claramente dominante. Não é thread-safe.
/// </summary>
public sealed class AnalogScroller(InputSettings settings)
{
    /// <summary>Teto de passos num único tick (uma pausa longa da janela não vira uma rajada).</summary>
    private const int MaxStepsPerTick = 3;

    private double _x;
    private double _y;
    private bool _engaged;
    private bool _latched;
    private bool _vertical = true;
    private TimeSpan _since;
    private TimeSpan _last;
    private double _pending;

    /// <summary>Rolando agora (inclinado além da zona morta e não travado).</summary>
    public bool IsScrolling => _engaged && !_latched;

    public void Update(double x, double y, TimeSpan now)
    {
        _x = Math.Clamp(x, -1, 1);
        _y = Math.Clamp(y, -1, 1);
        var magnitude = Math.Sqrt((_x * _x) + (_y * _y));
        if (magnitude < (_engaged || _latched ? settings.ScrollRelease : settings.ScrollDeadzone))
        {
            // Centro: para na hora e solta a trava (a próxima inclinação é um gesto novo).
            _engaged = false;
            _latched = false;
            _pending = 0;
            return;
        }
        if (_latched) return;
        if (!_engaged)
        {
            _engaged = true;
            _since = _last = now;
            _pending = 1; // o primeiro passo sai no próximo tick: responde ao toque, mesmo curto
            _vertical = Math.Abs(_y) >= Math.Abs(_x);
            return;
        }
        var ax = Math.Abs(_x);
        var ay = Math.Abs(_y);
        if (_vertical && ax >= ay * settings.DominanceRatio) SwitchAxis(vertical: false, now);
        else if (!_vertical && ay >= ax * settings.DominanceRatio) SwitchAxis(vertical: true, now);
    }

    /// <summary>Chamado a cada quadro do laço de entrada; emite os passos devidos desde o último tick.</summary>
    public void Tick(TimeSpan now, Action<InputAction> emit)
    {
        if (!IsScrolling) return;
        var elapsed = now - _last;
        _last = now;
        if (elapsed > TimeSpan.Zero) _pending += Rate(now) * elapsed.TotalSeconds;
        var steps = 0;
        while (_pending >= 1 && steps < MaxStepsPerTick)
        {
            _pending -= 1;
            steps++;
            emit(Direction);
        }
        if (_pending >= 1) _pending = 0.99;
    }

    /// <summary>
    /// Troca de contexto (modal aberto/fechado) ou R3 pressionado: para e ignora a inclinação atual até o analógico
    /// voltar ao centro, como os botões mantidos do <see cref="InputRouter"/>.
    /// </summary>
    public void Latch()
    {
        _pending = 0;
        _engaged = false;
        _latched = Math.Sqrt((_x * _x) + (_y * _y)) >= settings.ScrollRelease;
    }

    /// <summary>Esquece tudo (desconexão, janela sem foco).</summary>
    public void Reset()
    {
        _x = _y = 0;
        _engaged = _latched = false;
        _pending = 0;
    }

    /// <summary>Passos por segundo agora: proporcional à inclinação do eixo ativo, acelerando enquanto é mantida.</summary>
    public double Rate(TimeSpan now)
    {
        var component = _vertical ? Math.Abs(_y) : Math.Abs(_x);
        var travel = Math.Clamp((component - settings.ScrollRelease) / (1 - settings.ScrollRelease), 0, 1);
        var curve = travel * travel;
        var rate = settings.ScrollMinRate + ((settings.ScrollMaxRate - settings.ScrollMinRate) * curve);
        var sustained = settings.ScrollAccelerationTime <= TimeSpan.Zero ? 1 : Math.Clamp((now - _since) / settings.ScrollAccelerationTime, 0, 1);
        return rate * (1 + ((settings.ScrollMaxBoost - 1) * sustained * travel));
    }

    private InputAction Direction => _vertical
        ? (_y < 0 ? InputAction.ScrollUp : InputAction.ScrollDown)
        : (_x < 0 ? InputAction.ScrollLeft : InputAction.ScrollRight);

    private void SwitchAxis(bool vertical, TimeSpan now)
    {
        _vertical = vertical;
        _since = now; // aceleração recomeça no eixo novo
        _pending = Math.Min(_pending, 0.5);
    }
}
