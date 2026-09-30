using System.Runtime.CompilerServices;
using ControlFS.App.Resources;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ControlFS.App.Controls;

/// <summary>
/// Retorno de "mouse em cima" (#295): um clareado suave (escurecido no tema claro), neutro, nunca na cor de destaque, para que o
/// que está sob o ponteiro nunca se pareça com o foco do controle/teclado (#182: o destaque do sistema sumiu justamente porque a
/// lista rolava sob o ponteiro parado e parecia um segundo foco). Por isso só aparece enquanto o mouse se mexeu há pouco
/// (<see cref="NoteMove"/>): rolar ou mover o foco com o controle debaixo de um ponteiro parado não acende nada. O elemento
/// focado nunca recebe hover (o foco manda), e o fundo de antes volta ao sair. Sem animação própria além da troca curta do fundo.
/// </summary>
internal static class Hover
{
    /// <summary>Por quanto tempo depois de mexer o mouse um elemento novo sob o ponteiro ainda conta como "em cima".</summary>
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(1.5);

    private static long _lastMove = long.MinValue;

    /// <summary>Opacidade de um elemento sob o ponteiro quando o fundo dele não tem um tom de hover próprio (teclas, chips).</summary>
    private const double DimmedOpacity = 0.82;

    private sealed class State
    {
        public bool Focused;
        public bool Card;
        public Brush? Rest;
        public bool Hovering;
    }

    private static readonly ConditionalWeakTable<Border, State> Tracked = [];

    /// <summary>A janela chama a cada movimento do ponteiro: só mouse e caneta (toque não tem "em cima").</summary>
    public static void NoteMove(PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType != PointerDeviceType.Touch) _lastMove = Environment.TickCount64;
    }

    private static bool IsMouseActive(PointerRoutedEventArgs e) =>
        e.Pointer.PointerDeviceType != PointerDeviceType.Touch && Environment.TickCount64 - _lastMove < Window.TotalMilliseconds;

    /// <summary>
    /// Anel de foco (linhas, abas, atalhos, cartões): registra o tratamento uma vez por elemento (elementos reciclados não
    /// acumulam tratadores) e guarda se ele está focado. Chamado por <see cref="Theme.ApplyFocus"/> e <see cref="Theme.ApplyCardFocus"/>.
    /// </summary>
    public static void Track(Border border, bool focused, bool card)
    {
        var state = Tracked.GetValue(border, b =>
        {
            var created = new State();
            b.PointerEntered += (_, e) =>
            {
                if (created.Focused || !IsMouseActive(e)) return;
                created.Rest = b.Background;
                created.Hovering = true;
                b.Background = created.Card ? Theme.HoverCard : Theme.HoverWash;
            };
            b.PointerExited += (_, _) => Release(b, created);
            b.PointerCanceled += (_, _) => Release(b, created);
            return created;
        });
        state.Focused = focused;
        state.Card = card;
        state.Hovering = false; // quem chama acabou de pôr o fundo dele: o hover anterior já não vale
    }

    private static void Release(Border border, State state)
    {
        if (!state.Hovering) return;
        state.Hovering = false;
        if (!state.Focused) border.Background = state.Rest;
    }

    /// <summary>
    /// Elemento reconstruído a cada mudança (blocos e linhas de menu, teclas): <paramref name="hover"/> é o fundo sob o ponteiro;
    /// sem um, o elemento inteiro fica um pouco mais apagado. Não faz nada se <paramref name="active"/> for falso (focado, indisponível).
    /// </summary>
    public static void Attach(Border element, bool active, Brush? hover = null)
    {
        if (!active) return;
        Brush? rest = null;
        element.PointerEntered += (_, e) =>
        {
            if (!IsMouseActive(e)) return;
            if (hover is null) element.Opacity = DimmedOpacity;
            else
            {
                rest = element.Background;
                element.Background = hover;
            }
        };
        void Leave(object? sender, PointerRoutedEventArgs e)
        {
            if (hover is null) element.Opacity = 1;
            else if (rest is not null) element.Background = rest;
            rest = null;
        }
        element.PointerExited += Leave;
        element.PointerCanceled += Leave;
    }

    /// <summary>Qualquer elemento clicável sem fundo próprio (células de cabeçalho, chips): só a opacidade muda.</summary>
    public static void AttachDim(FrameworkElement element)
    {
        element.PointerEntered += (_, e) =>
        {
            if (IsMouseActive(e)) element.Opacity = DimmedOpacity;
        };
        element.PointerExited += (_, _) => element.Opacity = 1;
        element.PointerCanceled += (_, _) => element.Opacity = 1;
    }
}
