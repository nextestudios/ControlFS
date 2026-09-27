using ControlFS.Core.Preview;

namespace ControlFS.Application.State;

/// <summary>
/// Edição leve de um arquivo de texto (#62), linha a linha: a linha em foco é editada no teclado virtual; inserir, apagar e
/// desfazer valem para a linha em foco. Nada vai para o disco até Salvar (troca atômica com cópia do original).
/// </summary>
public sealed class TextEditor
{
    /// <summary>Alterações que dá para desfazer.</summary>
    public const int MaxUndo = 100;

    private readonly List<TextLine> _lines;
    private readonly List<(TextLine[] Lines, int Cursor)> _undo = [];

    internal TextEditor(TextEditDocument document, long length, DateTime lastWriteUtc)
    {
        Document = document;
        _lines = [.. document.Lines];
        Length = length;
        LastWriteUtc = lastWriteUtc;
    }

    public TextEditDocument Document { get; }
    public IReadOnlyList<TextLine> Lines => _lines;

    /// <summary>Linha em foco (0-based).</summary>
    public int Cursor { get; private set; }

    /// <summary>Tamanho e data do arquivo quando foi aberto: se mudarem até salvar, alguém mexeu nele.</summary>
    internal long Length { get; }
    internal DateTime LastWriteUtc { get; }

    public bool CanUndo => _undo.Count > 0;

    public bool IsModified => !_lines.SequenceEqual(Document.Lines);

    internal void MoveCursor(int line) => Cursor = Math.Clamp(line, 0, _lines.Count - 1);

    internal void ReplaceLine(string text)
    {
        if (_lines[Cursor].Text == text) return;
        Remember();
        _lines[Cursor] = _lines[Cursor] with { Text = text };
    }

    /// <summary>Linha nova abaixo (ou acima) da em foco, que passa a ser a em foco. A última linha continua sem quebra, se era assim.</summary>
    internal void Insert(string text, bool below)
    {
        Remember();
        var at = below ? Cursor + 1 : Cursor;
        if (below && Cursor == _lines.Count - 1 && _lines[Cursor].Ending.Length == 0)
        {
            _lines[Cursor] = _lines[Cursor] with { Ending = Document.NewLine };
            _lines.Insert(at, new TextLine(text, string.Empty));
        }
        else
        {
            _lines.Insert(at, new TextLine(text, Document.NewLine));
        }
        Cursor = at;
    }

    /// <summary>Apaga a linha em foco; a única linha vira vazia. Apagar a última passa a quebra dela para a anterior.</summary>
    internal void DeleteLine()
    {
        Remember();
        if (_lines.Count == 1)
        {
            _lines[0] = _lines[0] with { Text = string.Empty };
            return;
        }
        if (Cursor == _lines.Count - 1) _lines[Cursor - 1] = _lines[Cursor - 1] with { Ending = _lines[Cursor].Ending };
        _lines.RemoveAt(Cursor);
        Cursor = Math.Min(Cursor, _lines.Count - 1);
    }

    internal bool Undo()
    {
        if (_undo.Count == 0) return false;
        var (lines, cursor) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _lines.Clear();
        _lines.AddRange(lines);
        Cursor = Math.Clamp(cursor, 0, _lines.Count - 1);
        return true;
    }

    /// <summary>Quantas linhas diferem do arquivo original (resumo da confirmação).</summary>
    public int ChangedLines()
    {
        var original = Document.Lines;
        var common = 0;
        while (common < Math.Min(original.Count, _lines.Count) && original[common] == _lines[common]) common++;
        var tail = 0;
        while (tail < Math.Min(original.Count, _lines.Count) - common && original[^(tail + 1)] == _lines[^(tail + 1)]) tail++;
        return Math.Max(original.Count, _lines.Count) - common - tail;
    }

    private void Remember()
    {
        _undo.Add(([.. _lines], Cursor));
        if (_undo.Count > MaxUndo) _undo.RemoveAt(0);
    }
}
